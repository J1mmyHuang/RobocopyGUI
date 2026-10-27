// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RobocopyGui.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace RobocopyGui.WinUI
{
    public sealed partial class MainWindow : Window
    {
        enum Phase { Idle, Running, Completed, Terminated, Failed }

        const string GlyphClock = "", GlyphInfo = "", GlyphSearch = "", GlyphCopy = "", GlyphCut = "", GlyphShield = "", GlyphDone = "", GlyphWarning = "", GlyphStop = "", GlyphError = "";
        static readonly int[] BlockSizes = { 1, 2, 4, 8, 16 };

        readonly CopyWorkflow workflow = new CopyWorkflow();
        readonly ConcurrentQueue<string> pendingLogs = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<HashRecord> pendingFailures = new ConcurrentQueue<HashRecord>();
        readonly ObservableCollection<LogLine> logs = new ObservableCollection<LogLine>();
        readonly ObservableCollection<ExceptionRow> exceptions = new ObservableCollection<ExceptionRow>();
        readonly List<StepView> stepViews = new List<StepView>();
        readonly SpeedMeter speed = new SpeedMeter();
        readonly DispatcherQueueTimer timer;
        readonly bool ready;
        CancellationTokenSource cancellation;
        Phase phase = Phase.Idle;
        WorkflowStage stage = WorkflowStage.Scanning;
        RunOptions current;
        TransferPaths runPaths, preview;   // what the running/finished task uses; what the current input would use
        SourceKind? previewKind;
        int previewVersion;
        string failureMessage, inputError, notice;
        bool dragging;

        public MainWindow()
        {
            InitializeComponent();
            Title = "RobocopyGUI";
            ExtendsContentIntoTitleBar = true; SetTitleBar(AppTitleBar); AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
            AppWindow.Resize(new SizeInt32((int)(1360 * scale), (int)(860 * scale)));
            var presenter = AppWindow.Presenter as OverlappedPresenter; if (presenter != null) { presenter.PreferredMinimumWidth = (int)(1120 * scale); presenter.PreferredMinimumHeight = (int)(720 * scale); }
            SourceBox.AllowDrop = false; DestinationBox.AllowDrop = false;
            AlgorithmBox.ItemsSource = HashVerifier.Algorithms; AlgorithmBox.SelectedIndex = 0;
            BlockBox.ItemsSource = BlockSizes.Select(s => s + " MB").ToList(); BlockBox.SelectedIndex = 2;
            LogList.ItemsSource = logs; ExceptionList.ItemsSource = exceptions;
            workflow.StageChanged = delegate (WorkflowStage s) { stage = s; Refresh(); };
            workflow.Log = pendingLogs.Enqueue; workflow.Failure = pendingFailures.Enqueue;
            timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(150); timer.Tick += delegate { Drain(); Refresh(); }; timer.Start();
            Root.ActualThemeChanged += delegate { ApplyCaptionColors(); Refresh(); }; ApplyCaptionColors();
            Closed += delegate { timer.Stop(); if (cancellation != null) cancellation.Cancel(); workflow.KillRobocopy(); };
            ready = true; Refresh();
        }

        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);

        // Caption buttons are drawn by the window frame and follow the system theme, not the XAML theme; keep them readable on Mica in both.
        void ApplyCaptionColors()
        {
            var bar = AppWindow.TitleBar; bool dark = Root.ActualTheme == ElementTheme.Dark;
            bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = Colors.Transparent;
            bar.ButtonForegroundColor = bar.ButtonHoverForegroundColor = bar.ButtonPressedForegroundColor = dark ? Colors.White : Colors.Black;
            bar.ButtonInactiveForegroundColor = dark ? ColorHelper.FromArgb(0x5D, 0xFF, 0xFF, 0xFF) : ColorHelper.FromArgb(0x5C, 0, 0, 0);
            bar.ButtonHoverBackgroundColor = dark ? ColorHelper.FromArgb(0x0F, 0xFF, 0xFF, 0xFF) : ColorHelper.FromArgb(0x09, 0, 0, 0);
            bar.ButtonPressedBackgroundColor = dark ? ColorHelper.FromArgb(0x0A, 0xFF, 0xFF, 0xFF) : ColorHelper.FromArgb(0x06, 0, 0, 0);
        }

        RunOptions Selected()
        {
            return new RunOptions
            {
                Source = Clean(SourceBox.Text), Destination = Clean(DestinationBox.Text), Verify = HashToggle.IsOn,
                Algorithm = AlgorithmBox.SelectedItem as string ?? "SHA-256", BlockSize = BlockSizes[Math.Max(0, BlockBox.SelectedIndex)] * 1024 * 1024,
                Mode = ModeSelector.SelectedIndex == 1 ? TransferMode.Move : TransferMode.Copy,
            };
        }

        RunOptions View { get { return phase == Phase.Idle || current == null ? Selected() : current; } }

        TransferPaths Paths { get { return phase == Phase.Idle || runPaths == null ? preview : runPaths; } }

        SourceKind? Kind { get { return phase == Phase.Idle || runPaths == null ? previewKind : runPaths.Kind; } }

        string KindName { get { return Kind == SourceKind.File ? "源文件" : "源文件夹"; } }

        // Robocopy's move switch differs: /MOV for one file (keeps its folder), /MOVE for a folder.
        string MoveSwitch { get { return RobocopyRunner.MoveSwitch(Kind ?? SourceKind.Folder); } }

        static string Clean(string text) { return (text ?? "").Trim().Trim('"'); }

        // ---------- input ----------

        void Input_Changed(object sender, TextChangedEventArgs e) { if (!ready) return; UpdatePreview(); ToIdle(); }

        // Telling a file from a folder touches the disk, which can stall on an unreachable network path, so it runs off the UI thread.
        async void UpdatePreview()
        {
            string source = Clean(SourceBox.Text), destination = Clean(DestinationBox.Text); int version = ++previewVersion;
            var inspected = source.Length == 0 ? Tuple.Create<SourceKind?, TransferPaths>(null, null) : await Task.Run(() => Inspect(source, destination));
            if (version != previewVersion) return;
            previewKind = inspected.Item1; preview = inspected.Item2; Refresh();
        }

        static Tuple<SourceKind?, TransferPaths> Inspect(string source, string destination)
        {
            try
            {
                SourceKind? kind = File.Exists(source) ? SourceKind.File : Directory.Exists(source) ? SourceKind.Folder : (SourceKind?)null;
                return Tuple.Create(kind, kind != null && destination.Length > 0 ? TransferPaths.Resolve(source, destination) : null);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is IOException || ex is UnauthorizedAccessException) { return Tuple.Create<SourceKind?, TransferPaths>(null, null); }
        }
        void Option_Changed(object sender, SelectionChangedEventArgs e) { if (ready) ToIdle(); }
        void HashToggle_Toggled(object sender, RoutedEventArgs e) { if (!ready) return; HashCard.IsExpanded = HashToggle.IsOn; ToIdle(); }

        void ToIdle()
        {
            if (phase == Phase.Running) return;
            // A changed path or option is a new task: drop the finished run's numbers so the progress card doesn't show 100% next to "等待开始". The log stays until the next start.
            if (phase != Phase.Idle) { workflow.Reset(); exceptions.Clear(); }
            phase = Phase.Idle; current = null; inputError = null; notice = null; Refresh();
        }

        void BrowseSource_Click(SplitButton sender, SplitButtonClickEventArgs args) { PickSourceFolder_Click(sender, null); }
        async void PickSourceFolder_Click(object sender, RoutedEventArgs e) { string path = await PickFolderAsync(); if (path != null) SourceBox.Text = path; }
        async void PickSourceFile_Click(object sender, RoutedEventArgs e) { string path = await PickFileAsync(); if (path != null) SourceBox.Text = path; }
        async void BrowseDestination_Click(object sender, RoutedEventArgs e) { string path = await PickFolderAsync(); if (path != null) DestinationBox.Text = path; }

        async Task<string> PickFolderAsync()
        {
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync(); return folder != null ? folder.Path : null;
        }

        async Task<string> PickFileAsync()
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync(); return file != null ? file.Path : null;
        }

        void LocationCard_DragOver(object sender, DragEventArgs e)
        {
            if (phase == Phase.Running || !e.DataView.Contains(StandardDataFormats.StorageItems)) { e.AcceptedOperation = DataPackageOperation.None; return; }
            e.AcceptedOperation = DataPackageOperation.Link; e.DragUIOverride.Caption = "设为源";
            if (!dragging) { dragging = true; Refresh(); }
        }

        void LocationCard_DragLeave(object sender, DragEventArgs e)
        {
            var p = e.GetPosition(LocationCard);
            if (p.X <= 0 || p.Y <= 0 || p.X >= LocationCard.ActualWidth || p.Y >= LocationCard.ActualHeight) { dragging = false; Refresh(); }
        }

        async void LocationCard_Drop(object sender, DragEventArgs e)
        {
            dragging = false;
            if (phase != Phase.Running && e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count == 1) SourceBox.Text = items[0].Path; else inputError = "一次只能拖入一个文件夹或一个文件。";
            }
            Refresh();
        }

        void LogTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            bool log = LogTab.IsSelected; LogView.Visibility = log ? Visibility.Visible : Visibility.Collapsed; ExceptionView.Visibility = log ? Visibility.Collapsed : Visibility.Visible;
        }

        // ---------- run ----------

        async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (phase == Phase.Running) return;
            var o = Selected(); inputError = CopyWorkflow.Validate(o); notice = null;
            if (inputError != null) { Refresh(); return; }
            var paths = o.ResolvePaths();
            if (o.Mode == TransferMode.Move && !await ConfirmMoveAsync(o, paths)) return;
            current = o; runPaths = paths; phase = Phase.Running; stage = WorkflowStage.Scanning; failureMessage = null;
            workflow.Reset(); logs.Clear(); exceptions.Clear(); speed.Reset();
            string ignored; while (pendingLogs.TryDequeue(out ignored)) { } HashRecord skipped; while (pendingFailures.TryDequeue(out skipped)) { }
            cancellation = new CancellationTokenSource(); Refresh();
            try { await workflow.RunAsync(o, cancellation.Token); phase = Phase.Completed; }
            catch (OperationCanceledException) { phase = Phase.Terminated; pendingLogs.Enqueue("任务已停止。"); }
            catch (Exception ex) { phase = Phase.Failed; failureMessage = ex.Message; pendingLogs.Enqueue("任务失败：" + ex.Message); }
            finally { workflow.ReleaseRobocopy(); cancellation.Dispose(); cancellation = null; Drain(); Refresh(); }
        }

        void StopButton_Click(object sender, RoutedEventArgs e) { if (cancellation != null) cancellation.Cancel(); workflow.KillRobocopy(); }

        async Task<bool> ConfirmMoveAsync(RunOptions o, TransferPaths p)
        {
            bool raw = o.UsesRobocopyMove, file = p.Kind == SourceKind.File; string move = RobocopyRunner.MoveSwitch(p.Kind); var body = new StackPanel { Spacing = 12 };
            string first = raw
                ? (file ? "已关闭完整性校验。Robocopy /MOV 会在文件复制成功后立即删除源文件，不比对文件内容；它所在的文件夹不受影响。"
                        : "已关闭完整性校验。Robocopy /MOVE 会在每个文件复制成功后立即删除源文件，并删除移空的文件夹（包括源文件夹本身），不比对文件内容。")
                : (file ? "文件会移动到下面的位置：复制完成并通过 " + o.Algorithm + " 校验后，才删除这个源文件；它所在的文件夹和其中的其他文件不受影响。"
                        : "整个文件夹会移动到下面的位置：全部文件复制完成并通过 " + o.Algorithm + " 校验后，才删除源文件；删除后变空的文件夹（包括源文件夹本身）也会一并删除。");
            string[] lines = raw
                ? new[] { first, "目标中已存在且相同的文件会被跳过，它们的源文件会保留。", "如果需要确认文件完整，请取消并打开“完整性校验”。" }
                : new[] { first, "复制失败、校验出现异常，或你终止任务时，不会删除任何源文件。" };
            foreach (string line in lines) body.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
            var paths = new TextBlock { Text = "源：" + o.Source + "\n移动到：" + p.Result + (raw ? "\n参数：" + RobocopyRunner.Switches(p.Kind, true) : ""), FontFamily = (FontFamily)Root.Resources["MonoFont"], FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Brush("TextFillColorSecondaryBrush") };
            body.Children.Add(new Border { Child = paths, Padding = new Thickness(12, 10, 12, 10), Background = Theme.Brush("CardBackgroundFillColorSecondaryBrush"), BorderBrush = Theme.Brush("CardStrokeColorDefaultBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) });
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
                Title = raw ? "使用 Robocopy " + move + " 剪切？" : "确认剪切？", Content = body,
                PrimaryButtonText = raw ? "仍然剪切" : "开始剪切", CloseButtonText = "取消", DefaultButton = raw ? ContentDialogButton.Close : ContentDialogButton.Primary,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        const string SourceUrl = "https://github.com/J1mmyHuang/RobocopyGUI";

        // AGPL section 5(d): an interactive UI must show the copyright, that there is no warranty, that the work may be conveyed under the license, and how to read it.
        async void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            string version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? ""; int plus = version.IndexOf('+');
            string build = "版本 " + (plus >= 0 ? version.Substring(0, plus) : version) + (plus >= 0 && version.Length >= plus + 8 ? " · 构建自提交 " + version.Substring(plus + 1, 7) : "");
            var body = new StackPanel { Spacing = 12 };
            foreach (string line in new[]
            {
                "Windows 自带 Robocopy 的图形界面 · " + build,
                "Copyright (C) 2026 J1mmyHuang",
                "本程序是自由软件：你可以依据自由软件基金会发布的 GNU Affero 通用公共许可证第 3 版，或（由你选择）任何更高版本，再分发和修改它；另附一条第 7 条附加许可，允许与 Microsoft Windows App SDK、.NET 及 Windows 系统组件组合分发。",
                "本程序按“原样”提供，不附带任何担保，包括适销性或特定用途适用性的默示担保。详见许可证全文。",
                "RobocopyGUI 是独立项目，与 Microsoft 没有关联，也未获其认可或赞助；它调用的是你电脑上 Windows 自带的 robocopy.exe。",
            }) body.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(-11, 0, 0, 0) };
            links.Children.Add(new HyperlinkButton { Content = "源代码", NavigateUri = new Uri(SourceUrl) });
            links.Children.Add(DocumentLink("许可证全文", "LICENSE", "https://www.gnu.org/licenses/agpl-3.0.html"));
            links.Children.Add(DocumentLink("附加许可", "LICENSE-EXCEPTION.txt", SourceUrl + "/blob/main/LICENSE-EXCEPTION.txt"));
            links.Children.Add(DocumentLink("第三方组件声明", "THIRD-PARTY-NOTICES.md", SourceUrl + "/blob/main/THIRD-PARTY-NOTICES.md"));
            body.Children.Add(links);
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
                Title = "关于 RobocopyGUI", Content = body, CloseButtonText = "关闭", DefaultButton = ContentDialogButton.Close,
            };
            await dialog.ShowAsync();
        }

        // The release package ships these files next to the program: open that copy as plain text, or the online copy when running from source.
        static HyperlinkButton DocumentLink(string text, string fileName, string onlineUrl)
        {
            string path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (!File.Exists(path)) return new HyperlinkButton { Content = text, NavigateUri = new Uri(onlineUrl) };
            var link = new HyperlinkButton { Content = text };
            link.Click += delegate { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true }); };
            return link;
        }

        async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (workflow.Records.Count == 0) return;
            var picker = new FileSavePicker { SuggestedFileName = "robocopy_hash_report" }; picker.FileTypeChoices.Add("CSV 文件", new List<string> { ".csv" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync(); if (file == null) return;
            try { HashReport.Write(file.Path, workflow.Records); notice = "完整报告已导出：" + file.Path; }
            catch (Exception ex) { notice = "导出失败：" + ex.Message; }
            Refresh();
        }

        void Drain()
        {
            string line; int count = 0; bool added = false;
            while (count++ < 400 && pendingLogs.TryDequeue(out line)) { if (string.IsNullOrWhiteSpace(line)) continue; logs.Add(LogLine.From(line)); added = true; }
            while (logs.Count > 5000) logs.RemoveAt(0);
            // Scroll after layout has measured the new rows; scrolling synchronously stops a few lines short.
            if (added) DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, delegate { if (logs.Count > 0) LogList.ScrollIntoView(logs[logs.Count - 1]); });
            HashRecord record; while (pendingFailures.TryDequeue(out record)) exceptions.Add(ExceptionRow.From(record));
        }

        // ---------- render: one task state drives steps, badge, info bar, progress and footer ----------

        void Refresh()
        {
            if (!ready) return;
            var o = View; var p = workflow.Progress; bool running = phase == Phase.Running;
            bool hasSource = o.Source.Length > 0, hasDest = o.Destination.Length > 0, moveHash = o.Mode == TransferMode.Move && o.Verify, raw = o.UsesRobocopyMove;

            foreach (Control c in new Control[] { SourceBox, DestinationBox, BrowseSource, BrowseDestination, ModeSelector, HashToggle }) c.IsEnabled = !running;
            AlgorithmCard.IsEnabled = BlockCard.IsEnabled = HashToggle.IsOn && !running;
            ModeIcon.Glyph = o.Mode == TransferMode.Move ? GlyphCut : GlyphCopy;
            var paths = Paths;
            ModeCard.Description = raw ? "剪切：" + MoveSwitch + " 复制后立即删除源文件" : moveHash ? "剪切：全部校验通过后删除源文件" : "复制：源文件保持不变";
            HashCard.Description = o.Verify ? "复制完成后比对源与目标文件的哈希值" : "已关闭：不比对源与目标文件的内容";
            AdvancedCard.Description = "实际参数：" + RobocopyRunner.Switches(Kind ?? SourceKind.Folder, raw);
            CommandText.Text = "robocopy " + (paths != null ? RobocopyRunner.BuildArguments(paths, raw) : RobocopyRunner.BuildArguments(hasSource ? o.Source : "<源>", hasDest ? o.Destination : "<目标文件夹>", raw));
            DropOverlay.Visibility = dragging ? Visibility.Visible : Visibility.Collapsed;
            // The location card always says where the chosen folder or file will actually end up.
            bool recognized = phase == Phase.Idle && Kind != null && !hasDest, preview = phase == Phase.Idle && paths != null;
            string verb = o.Mode == TransferMode.Move ? "移动到：" : "复制到：";
            HintText.Text = running ? "任务运行中，路径已锁定 · " + verb + runPaths.Result
                : phase != Phase.Idle && runPaths != null ? "目标位置：" + runPaths.Result
                : recognized ? "已识别" + KindName + "。目标文件夹请点击“浏览…”选择。"
                : preview ? "将" + verb + paths.Result
                : "可以把文件夹或文件拖到这里，设为源。";
            HintText.Foreground = HintIcon.Foreground = Theme.Brush(recognized || preview ? "AccentTextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush");

            PageTitle.Text = o.Mode == TransferMode.Move ? (raw ? "剪切文件（Robocopy " + MoveSwitch + "）" : "剪切文件并验证完整性") : (o.Verify ? "复制文件并验证完整性" : "复制文件");
            PageSubtitle.Text = Subtitle(o, p, hasSource, hasDest);
            SetBadge(p, hasSource, hasDest, raw);
            SetSteps(o, p, hasSource, hasDest);
            SetProgress(o, p);
            SetInfo(o, p);
            SetFooter(o, p, hasSource, hasDest);

            ExceptionTab.Text = exceptions.Count > 0 ? "异常文件 · " + exceptions.Count : "异常文件";
            bool noExceptions = exceptions.Count == 0; ExceptionEmpty.Visibility = noExceptions ? Visibility.Visible : Visibility.Collapsed; ExceptionHeader.Visibility = ExceptionList.Visibility = noExceptions ? Visibility.Collapsed : Visibility.Visible;
            ExceptionEmptyIcon.Glyph = o.Verify ? GlyphDone : GlyphInfo; ExceptionEmptyIcon.Foreground = Theme.Brush(o.Verify && p.HashTotal > 0 ? "SystemFillColorSuccessBrush" : "TextFillColorTertiaryBrush");
            ExceptionEmptyTitle.Text = !o.Verify ? "未进行哈希校验" : p.HashTotal == 0 ? "暂无异常文件" : "没有异常文件";
            ExceptionEmptyText.Text = !o.Verify ? "已关闭完整性校验，没有可比对的结果。" : p.HashTotal == 0 ? "开始校验后，这里会列出哈希不一致或缺失的文件。" : "所有已校验文件的源哈希与目标哈希一致。";
            LogEmpty.Visibility = logs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            StartButton.IsEnabled = !running && hasSource && hasDest; StopButton.IsEnabled = running;
            ExportButton.IsEnabled = phase == Phase.Completed && workflow.Records.Count > 0;
        }

        string Subtitle(RunOptions o, WorkflowProgress p, bool hasSource, bool hasDest)
        {
            bool moveHash = o.Mode == TransferMode.Move && o.Verify, raw = o.UsesRobocopyMove;
            switch (phase)
            {
                case Phase.Idle:
                    if (dragging) return "松开鼠标即可设为源。";
                    if (!hasSource && !hasDest) return "选择一个文件夹或文件，以及目标文件夹；开始前可以调整选项。";
                    if (!hasDest) return Kind != null ? "已识别" + KindName + "，请选择目标文件夹。" : "请选择目标文件夹。";
                    return moveHash ? "全部文件通过校验后，才会删除源文件。" : raw ? "已关闭完整性校验，复制成功后会立即删除源文件。" : !o.Verify ? "已关闭完整性校验，只复制、不比对文件内容。" : "路径已就绪，确认选项后点击“开始”。";
                case Phase.Running:
                    switch (stage)
                    {
                        case WorkflowStage.Scanning: return "正在扫描" + KindName + "，路径与选项已锁定。";
                        case WorkflowStage.Copying: return raw ? "正在移动，复制成功的源文件会立即删除。" : "路径与选项已锁定，Robocopy 正在后台复制。";
                        case WorkflowStage.Verifying: return moveHash ? "复制已完成，正在校验；源文件尚未删除。" : "复制已完成，正在比对源与目标文件的哈希值。";
                        case WorkflowStage.DeletingSource: return "全部文件校验一致，正在删除源文件。";
                        default: return "正在收尾…";
                    }
                case Phase.Completed:
                    if (moveHash) return workflow.SourceDeletionSkipped ? "有文件未通过校验，源文件全部保留。" : "文件已移动到目标位置，源文件在校验通过后已删除。";
                    if (raw) return "文件已移动到目标位置，未比对文件内容。";
                    if (!o.Verify) return "全部文件已复制，未比对文件内容。";
                    return p.HashFailed > 0 ? "复制已完成，但有文件未通过校验。" : "全部文件已复制并通过校验。";
                case Phase.Terminated: return "任务已由你终止，可以调整后重新开始。";
                default: return "任务没有完成，请查看运行日志。";
            }
        }

        void SetBadge(WorkflowProgress p, bool hasSource, bool hasDest, bool raw)
        {
            if (phase == Phase.Idle) { if (hasSource && !hasDest) Badge.Set(GlyphInfo, "已识别", BadgeTone.Attention); else Badge.Set(GlyphClock, "等待开始", BadgeTone.Neutral); return; }
            if (phase == Phase.Running && stage != WorkflowStage.Completed)
            {
                if (stage == WorkflowStage.Scanning) Badge.Set(GlyphSearch, "正在扫描", BadgeTone.Accent);
                else if (stage == WorkflowStage.Copying) Badge.Set(raw ? GlyphCut : GlyphCopy, raw ? "正在剪切" : "正在复制", BadgeTone.Accent);
                else if (stage == WorkflowStage.Verifying) Badge.Set(GlyphShield, "正在校验", BadgeTone.Accent);
                else Badge.Set(GlyphCut, "正在剪切", BadgeTone.Accent);
                return;
            }
            if (phase == Phase.Terminated) Badge.Set(GlyphStop, "已终止", BadgeTone.Caution);
            else if (phase == Phase.Failed) Badge.Set(GlyphError, "失败", BadgeTone.Critical);
            else if (CompletedWithIssues(p)) Badge.Set(GlyphWarning, "完成，有异常", BadgeTone.Caution);
            else Badge.Set(GlyphDone, "已完成", BadgeTone.Success);
        }

        bool CompletedWithIssues(WorkflowProgress p) { return p.HashFailed > 0 || workflow.SourceDeletionSkipped || p.DeleteKept > 0 || workflow.RemainingSourceFiles > 0; }

        void SetSteps(RunOptions o, WorkflowProgress p, bool hasSource, bool hasDest)
        {
            bool moveHash = o.Mode == TransferMode.Move && o.Verify, raw = o.UsesRobocopyMove;
            var titles = new List<string> { "选择路径", "扫描清单", raw ? "Robocopy 移动" : "Robocopy 复制", "哈希校验" }; if (moveHash) titles.Add("删除源文件"); titles.Add("完成与报告");
            int final = titles.Count - 1, deleteIndex = moveHash ? 4 : -1;
            int at = phase == Phase.Idle ? 0 : stage == WorkflowStage.Scanning ? 1 : stage == WorkflowStage.Copying ? 2 : stage == WorkflowStage.Verifying ? 3 : stage == WorkflowStage.DeletingSource ? deleteIndex : final;
            if (stepViews.Count != titles.Count) { StepsPanel.Children.Clear(); stepViews.Clear(); foreach (var t in titles) { var v = new StepView(); stepViews.Add(v); StepsPanel.Children.Add(v); } }
            for (int i = 0; i < titles.Count; i++)
            {
                StepState state; string desc;
                if (i == 3 && !o.Verify) { state = StepState.Skipped; desc = "已关闭哈希校验"; }
                else if (phase == Phase.Idle)
                {
                    if (i == 0 && inputError != null) { state = StepState.Active; desc = "路径有误，请修改后重试"; }
                    else if (i == 0) { state = hasSource && hasDest ? StepState.Done : StepState.Active; desc = hasSource && hasDest ? "源与目标已就绪" : hasSource ? "已识别" + KindName + "，请选择目标" : "选择源与目标位置"; }
                    else { state = StepState.Pending; desc = PendingText(i, o, deleteIndex, final); }
                }
                else if (phase == Phase.Completed)
                {
                    state = i == deleteIndex && workflow.SourceDeletionSkipped ? StepState.Failed : StepState.Done; desc = DoneText(i, o, p, deleteIndex, final);
                }
                else if (i < at) { state = StepState.Done; desc = DoneText(i, o, p, deleteIndex, final); }
                else if (i == at)
                {
                    state = phase == Phase.Running ? StepState.Active : phase == Phase.Terminated ? StepState.Stopped : StepState.Failed;
                    desc = phase == Phase.Running ? ActiveText(i, o, p, deleteIndex) : phase == Phase.Terminated ? (i == deleteIndex ? "已终止 · 已删除 " + p.DeleteDone + " 个" : "已终止") : "失败";
                }
                else { state = StepState.Pending; desc = phase == Phase.Running ? PendingText(i, o, deleteIndex, final) : i == deleteIndex ? "未执行，未删除任何源文件" : "未执行"; }
                stepViews[i].Set(i + 1, titles[i], desc, state, i < final);
            }
        }

        string PendingText(int i, RunOptions o, int deleteIndex, int final)
        {
            if (i == 1) return "统计文件数量与大小";
            if (i == 2) return o.UsesRobocopyMove ? MoveSwitch + "：复制后删除源文件" : "使用 Windows 内置 Robocopy";
            if (i == 3) return o.Algorithm + " · 复制完成后开始";
            if (i == deleteIndex) return phase == Phase.Running ? "校验全部通过后执行" : "只删除校验一致的文件";
            return o.Verify ? "CSV 完整性报告" : o.Mode == TransferMode.Move ? "移动结果" : "复制结果";
        }

        string ActiveText(int i, RunOptions o, WorkflowProgress p, int deleteIndex)
        {
            if (i == 1) return "已发现 " + p.ScanFiles + " 个文件";
            if (i == 2) return p.CopyTotal == 0 ? "无需复制" : (o.UsesRobocopyMove ? "正在移动 · " : "正在写入目标磁盘 · ") + Format.Percent(p.CopyDone, p.CopyTotal);
            if (i == 3) return o.Algorithm + " · " + Format.Percent(p.HashDone, p.HashTotal);
            if (i == deleteIndex) return "正在删除 · " + p.DeleteDone + " / " + p.DeleteTotal;
            return "正在收尾";
        }

        string DoneText(int i, RunOptions o, WorkflowProgress p, int deleteIndex, int final)
        {
            if (i == 0) return phase == Phase.Running ? "源与目标已锁定" : "源与目标";
            if (i == 1) return p.ScanFiles + " 个文件 · " + Format.Bytes(p.ScanBytes);
            if (i == 2) return p.CopyTotal == 0 ? "无需复制，文件已匹配" : Format.Bytes(p.CopyTotal) + (o.UsesRobocopyMove ? " 已移动" : " 已写入");
            if (i == 3) return p.HashFailed == 0 ? p.HashPassed + " 个文件全部一致" : "通过 " + p.HashPassed + " · 异常 " + p.HashFailed;
            if (i == deleteIndex) return workflow.SourceDeletionSkipped ? "存在校验异常，未删除源文件" : "已删除 " + p.DeleteDone + " 个源文件" + (p.DeleteKept > 0 ? "，保留 " + p.DeleteKept + " 个" : "");
            return o.Verify ? "可以导出 CSV 报告" : "任务完成";
        }

        void SetProgress(RunOptions o, WorkflowProgress p)
        {
            bool running = phase == Phase.Running, raw = o.UsesRobocopyMove, moveHash = o.Mode == TransferMode.Move && o.Verify;
            CopyLabel.Text = raw ? "移动" : "复制";
            if (!p.ScanCompleted) { CopyValue.Text = running ? "等待中" : "—"; CopyBar.Value = 0; CopyDetail.Text = running ? "扫描完成后开始" : "开始任务后显示进度"; }
            else if (p.CopyTotal == 0) { CopyValue.Text = "无需复制"; CopyBar.Value = 0; CopyDetail.Text = ProgressText.Copy(p); }
            else
            {
                CopyValue.Text = Format.Percent(p.CopyDone, p.CopyTotal); CopyBar.Value = Math.Min(1000, p.CopyDone * 1000 / p.CopyTotal);
                string extra = "";
                if (running && stage == WorkflowStage.Copying && p.CopyDone < p.CopyTotal) { speed.Add(p.CopyDone); double bps = speed.BytesPerSecond; if (bps > 0) extra = " · " + Format.Bytes((long)bps) + "/s · 剩余约 " + Remaining((p.CopyTotal - p.CopyDone) / bps); }
                CopyDetail.Text = ProgressText.Copy(p) + extra;
            }
            CopyBar.ShowPaused = phase == Phase.Terminated && stage == WorkflowStage.Copying; CopyBar.ShowError = phase == Phase.Failed && stage == WorkflowStage.Copying;

            HashRow.Opacity = o.Verify ? 1 : 0.45;
            if (!o.Verify) { HashValue.Text = "已关闭"; HashBar.Value = 0; HashDetail.Text = "不比对源与目标文件的内容"; }
            else if (p.HashTotal == 0) { HashValue.Text = running ? "等待中" : phase == Phase.Idle ? "—" : "未执行"; HashBar.Value = 0; HashDetail.Text = o.Algorithm + " · 将在复制完成后开始"; }
            else
            {
                HashValue.Text = Format.Percent(p.HashDone, p.HashTotal); HashBar.Value = Math.Min(1000, p.HashDone * 1000 / p.HashTotal);
                bool unfinished = p.HashDone < p.HashTotal;
                HashDetail.Text = running && stage == WorkflowStage.Verifying ? ProgressText.Hash(p, true) + " · 正在读取文件"
                    : unfinished ? o.Algorithm + " · " + (phase == Phase.Terminated ? "已终止" : "未完成") + " · 通过 " + p.HashPassed + " · 异常 " + p.HashFailed
                    : o.Algorithm + " · " + (p.HashFailed == 0 ? p.HashPassed + " 个文件全部一致" : "通过 " + p.HashPassed + " · 异常 " + p.HashFailed);
            }
            HashBar.ShowPaused = phase == Phase.Terminated && stage == WorkflowStage.Verifying; HashBar.ShowError = p.HashFailed > 0 || (phase == Phase.Failed && stage == WorkflowStage.Verifying);

            bool started = phase != Phase.Idle || p.ScanFiles > 0;
            Stat1Value.Text = started ? p.ScanFiles.ToString("N0") : "—";
            Stat2Label.Text = raw ? "已移动" : "已复制"; Stat2Value.Text = p.ScanCompleted ? Format.Bytes(p.CopyDone) : "—";
            Stat3Value.Text = !o.Verify ? "未校验" : p.HashTotal > 0 ? p.HashPassed.ToString("N0") : "—";
            bool showDeleted = moveHash && p.DeleteTotal > 0;
            Stat4Label.Text = showDeleted ? "已删除源" : "异常"; Stat4Value.Text = showDeleted ? p.DeleteDone.ToString("N0") : (o.Verify && p.HashTotal > 0 ? p.HashFailed.ToString("N0") : "—");
        }

        static string Remaining(double seconds) { if (seconds < 60) return Math.Max(1, (int)Math.Ceiling(seconds)) + " 秒"; if (seconds < 3600) return (int)Math.Ceiling(seconds / 60) + " 分钟"; return (seconds / 3600).ToString("0.#") + " 小时"; }

        void SetInfo(RunOptions o, WorkflowProgress p)
        {
            bool moveHash = o.Mode == TransferMode.Move && o.Verify, raw = o.UsesRobocopyMove, file = Kind == SourceKind.File;
            string title = null, message = null; var severity = InfoBarSeverity.Informational;
            if (inputError != null) { title = "路径错误"; message = inputError; severity = InfoBarSeverity.Error; }
            else if (phase == Phase.Idle)
            {
                if (moveHash)
                {
                    title = "剪切会删除源文件"; severity = InfoBarSeverity.Warning;
                    message = file ? "复制并通过 " + o.Algorithm + " 校验后才删除这个源文件，它所在的文件夹和其他文件不受影响；复制失败、校验异常或终止时不会删除。"
                        : "整个文件夹会移动到目标位置：复制并通过 " + o.Algorithm + " 校验后，才删除源文件和变空的文件夹（包括源文件夹本身）；复制失败、校验异常或终止时不会删除。";
                }
                else if (raw)
                {
                    title = "将使用 Robocopy " + MoveSwitch; severity = InfoBarSeverity.Warning;
                    message = file ? "关闭校验后，文件复制成功就会从源位置删除，不比对文件内容；目标中已有相同文件时会跳过，源文件保留。"
                        : "关闭校验后，每个文件复制成功就会从源文件夹删除，移空的文件夹（包括源文件夹本身）也会删除，不比对文件内容；目标中已相同的文件会被跳过并保留在源文件夹。";
                }
            }
            else if (phase == Phase.Running)
            {
                if (moveHash && stage == WorkflowStage.DeletingSource) { title = "正在删除源文件"; message = "只删除已在目标位置校验一致的文件；终止后，尚未删除的源文件会全部保留。"; }
                else if (moveHash) { title = "源文件尚未删除"; message = "只有全部文件校验一致后，才会开始删除源文件。"; }
                else if (raw && stage == WorkflowStage.Copying) { title = "源文件正在被删除"; message = "Robocopy " + MoveSwitch + " 会在文件复制成功后立即删除源文件。"; severity = InfoBarSeverity.Warning; }
            }
            else if (phase == Phase.Completed)
            {
                if (moveHash)
                {
                    if (workflow.SourceDeletionSkipped) { title = "未删除源文件"; message = "有 " + p.HashFailed + " 个文件未通过校验，为保护数据没有删除任何源文件。请在“异常文件”中查看。"; severity = InfoBarSeverity.Error; }
                    else if (p.DeleteKept > 0) { title = "剪切完成，部分源文件已保留"; message = "已删除 " + p.DeleteDone + " 个源文件，保留 " + p.DeleteKept + " 个（原因见运行日志）。"; severity = InfoBarSeverity.Warning; }
                    else { title = "剪切完成"; message = p.HashPassed + " 个文件已通过 " + o.Algorithm + " 校验并移动到目标位置，源文件已删除。"; severity = InfoBarSeverity.Success; }
                }
                else if (raw)
                {
                    title = "剪切完成（未校验）"; severity = workflow.RemainingSourceFiles > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
                    message = workflow.RemainingSourceFiles > 0 ? "已通过 Robocopy " + MoveSwitch + " 移动文件；" + workflow.RemainingSourceFiles + " 个文件在目标中已相同，被跳过并保留在源位置。没有比对文件内容。" : p.ScanFiles + " 个文件已通过 Robocopy " + MoveSwitch + " 移动，源文件已删除；没有比对文件内容。";
                }
                else if (!o.Verify) { title = "复制完成（未校验）"; message = p.ScanFiles + " 个文件已复制。已关闭哈希校验，没有比对文件内容。"; }
                else if (p.HashFailed > 0) { title = "任务完成，但有异常文件"; message = "通过 " + p.HashPassed + "，异常 " + p.HashFailed + "。请在“异常文件”中查看。"; severity = InfoBarSeverity.Warning; }
                else { title = "任务完成"; message = p.ScanFiles + " 个文件已复制，" + o.Algorithm + " 校验全部通过。"; severity = InfoBarSeverity.Success; }
            }
            else if (phase == Phase.Terminated)
            {
                title = "任务已终止"; severity = InfoBarSeverity.Warning;
                message = moveHash ? (p.DeleteDone > 0 ? "已删除 " + p.DeleteDone + " 个校验一致的源文件；其余源文件全部保留。" : "源文件没有被删除；已复制到目标位置的文件保留。")
                    : raw ? "已被 Robocopy " + MoveSwitch + " 移动的文件已从源位置删除；其余文件仍保留。"
                    : "已复制的文件保留在目标位置，源文件没有任何改动。重新开始时会跳过已完整复制的文件。";
            }
            else { title = "任务失败"; message = failureMessage; severity = InfoBarSeverity.Error; }
            StatusInfo.IsOpen = title != null; if (title != null) { StatusInfo.Title = title; StatusInfo.Message = message; StatusInfo.Severity = severity; }
        }

        void SetFooter(RunOptions o, WorkflowProgress p, bool hasSource, bool hasDest)
        {
            bool raw = o.UsesRobocopyMove; string glyph = GlyphInfo, text, brush = "TextFillColorSecondaryBrush";
            switch (phase)
            {
                case Phase.Idle:
                    if (!hasSource || !hasDest) text = "请选择源（文件夹或文件）和目标文件夹";
                    else if (o.Mode == TransferMode.Move) { text = raw ? "准备就绪 · " + MoveSwitch + " 模式，开始前会再次确认" : "准备就绪 · 剪切模式，开始前会再次确认"; if (raw) { glyph = GlyphWarning; brush = "SystemFillColorCautionBrush"; } }
                    else text = o.Verify ? "准备就绪 · 点击“开始”运行 Robocopy" : "准备就绪 · 将跳过哈希校验";
                    break;
                case Phase.Running:
                    brush = "AccentTextFillColorPrimaryBrush";
                    if (stage == WorkflowStage.Scanning) { glyph = GlyphSearch; text = "正在扫描" + KindName + " · 已发现 " + p.ScanFiles + " 个文件"; }
                    else if (stage == WorkflowStage.Copying) { glyph = raw ? GlyphCut : GlyphCopy; text = p.CopyTotal == 0 ? "无需复制，文件已匹配" : (raw ? "Robocopy " + MoveSwitch + " 正在移动 · " : "Robocopy 正在复制 · ") + Format.Bytes(p.CopyDone) + " / " + Format.Bytes(p.CopyTotal); }
                    else if (stage == WorkflowStage.Verifying) { glyph = GlyphShield; text = "正在计算哈希 · 通过 " + p.HashPassed + " · 异常 " + p.HashFailed; }
                    else if (stage == WorkflowStage.DeletingSource) { glyph = GlyphCut; text = "正在删除源文件 · " + p.DeleteDone + " / " + p.DeleteTotal; }
                    else text = "正在收尾…";
                    break;
                case Phase.Completed:
                    bool issues = CompletedWithIssues(p); glyph = issues ? GlyphWarning : GlyphDone; brush = issues ? "SystemFillColorCautionBrush" : "SystemFillColorSuccessBrush";
                    if (workflow.SourceDeletionSkipped) text = "任务完成 · 有校验异常，未删除源文件";
                    else if (p.DeleteKept > 0) text = "任务完成 · 保留 " + p.DeleteKept + " 个源文件";
                    else if (workflow.RemainingSourceFiles > 0) text = "任务完成 · " + workflow.RemainingSourceFiles + " 个源文件未移动";
                    else text = o.Verify ? "任务完成 · 通过 " + p.HashPassed + " · 异常 " + p.HashFailed : "任务完成 · 已跳过哈希校验";
                    break;
                case Phase.Terminated: glyph = GlyphStop; brush = "SystemFillColorCautionBrush"; text = "任务已停止"; break;
                default: glyph = GlyphError; brush = "SystemFillColorCriticalBrush"; text = "任务失败：" + failureMessage; break;
            }
            if (inputError != null) { glyph = GlyphError; brush = "SystemFillColorCriticalBrush"; text = inputError; }
            if (notice != null) text = notice;
            FooterIcon.Glyph = glyph; FooterIcon.Foreground = Theme.Brush(brush); FooterText.Text = text;
        }

        sealed class SpeedMeter
        {
            readonly Queue<KeyValuePair<DateTime, long>> samples = new Queue<KeyValuePair<DateTime, long>>();
            public void Reset() { samples.Clear(); }
            public void Add(long bytes) { var now = DateTime.UtcNow; samples.Enqueue(new KeyValuePair<DateTime, long>(now, bytes)); while (samples.Count > 2 && (now - samples.Peek().Key).TotalSeconds > 3) samples.Dequeue(); }
            public double BytesPerSecond
            {
                get
                {
                    if (samples.Count < 2) return 0; var first = samples.Peek(); var last = samples.Last(); double seconds = (last.Key - first.Key).TotalSeconds;
                    return seconds > 0.5 ? Math.Max(0, (last.Value - first.Value) / seconds) : 0;
                }
            }
        }
    }
}
