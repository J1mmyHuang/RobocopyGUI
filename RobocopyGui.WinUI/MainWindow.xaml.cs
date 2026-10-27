using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

        static string Clean(string text) { return (text ?? "").Trim().Trim('"'); }

        // ---------- input ----------

        void Input_Changed(object sender, TextChangedEventArgs e) { if (ready) ToIdle(); }
        void Option_Changed(object sender, SelectionChangedEventArgs e) { if (ready) ToIdle(); }
        void HashToggle_Toggled(object sender, RoutedEventArgs e) { if (!ready) return; HashCard.IsExpanded = HashToggle.IsOn; ToIdle(); }

        void ToIdle()
        {
            if (phase == Phase.Running) return;
            // A changed path or option is a new task: drop the finished run's numbers so the progress card doesn't show 100% next to "等待开始". The log stays until the next start.
            if (phase != Phase.Idle) { workflow.Reset(); exceptions.Clear(); }
            phase = Phase.Idle; current = null; inputError = null; notice = null; Refresh();
        }

        async void BrowseSource_Click(object sender, RoutedEventArgs e) { string path = await PickFolderAsync(); if (path != null) SourceBox.Text = path; }
        async void BrowseDestination_Click(object sender, RoutedEventArgs e) { string path = await PickFolderAsync(); if (path != null) DestinationBox.Text = path; }

        async Task<string> PickFolderAsync()
        {
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync(); return folder != null ? folder.Path : null;
        }

        void LocationCard_DragOver(object sender, DragEventArgs e)
        {
            if (phase == Phase.Running || !e.DataView.Contains(StandardDataFormats.StorageItems)) { e.AcceptedOperation = DataPackageOperation.None; return; }
            e.AcceptedOperation = DataPackageOperation.Link; e.DragUIOverride.Caption = "设为源文件夹";
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
                var folder = (await e.DataView.GetStorageItemsAsync()).OfType<StorageFolder>().FirstOrDefault();
                if (folder != null) SourceBox.Text = folder.Path; else inputError = "只能把文件夹拖到源位置。";
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
            var o = Selected(); inputError = CopyWorkflow.ValidatePaths(o.Source, o.Destination); notice = null;
            if (inputError != null) { Refresh(); return; }
            if (o.Mode == TransferMode.Move && !await ConfirmMoveAsync(o)) return;
            current = o; phase = Phase.Running; stage = WorkflowStage.Scanning; failureMessage = null;
            workflow.Reset(); logs.Clear(); exceptions.Clear(); speed.Reset();
            string ignored; while (pendingLogs.TryDequeue(out ignored)) { } HashRecord skipped; while (pendingFailures.TryDequeue(out skipped)) { }
            cancellation = new CancellationTokenSource(); Refresh();
            try { await workflow.RunAsync(o, cancellation.Token); phase = Phase.Completed; }
            catch (OperationCanceledException) { phase = Phase.Terminated; pendingLogs.Enqueue("任务已停止。"); }
            catch (Exception ex) { phase = Phase.Failed; failureMessage = ex.Message; pendingLogs.Enqueue("任务失败：" + ex.Message); }
            finally { workflow.ReleaseRobocopy(); cancellation.Dispose(); cancellation = null; Drain(); Refresh(); }
        }

        void StopButton_Click(object sender, RoutedEventArgs e) { if (cancellation != null) cancellation.Cancel(); workflow.KillRobocopy(); }

        async Task<bool> ConfirmMoveAsync(RunOptions o)
        {
            bool raw = o.UsesRobocopyMove; var body = new StackPanel { Spacing = 12 };
            string[] lines = raw
                ? new[] { "已关闭完整性校验。Robocopy 会在每个文件复制成功后立即删除源文件，不比对文件内容。", "目标中已存在且相同的文件会被跳过，它们的源文件会保留。", "如果需要确认文件完整，请取消并打开“完整性校验”。" }
                : new[] { "全部文件复制完成并通过 " + o.Algorithm + " 校验后，才会删除源文件夹中的文件；删除后变空的文件夹（包括源文件夹本身）也会一并删除。", "复制失败、校验出现异常，或你终止任务时，不会删除任何源文件。" };
            foreach (string line in lines) body.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
            var paths = new TextBlock { Text = "源：" + o.Source + "\n目标：" + o.Destination + (raw ? "\n参数：" + RobocopyRunner.Switches(true) : ""), FontFamily = (FontFamily)Root.Resources["MonoFont"], FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Brush("TextFillColorSecondaryBrush") };
            body.Children.Add(new Border { Child = paths, Padding = new Thickness(12, 10, 12, 10), Background = Theme.Brush("CardBackgroundFillColorSecondaryBrush"), BorderBrush = Theme.Brush("CardStrokeColorDefaultBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) });
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
                Title = raw ? "使用 Robocopy /MOVE 剪切？" : "确认剪切？", Content = body,
                PrimaryButtonText = raw ? "仍然剪切" : "开始剪切", CloseButtonText = "取消", DefaultButton = raw ? ContentDialogButton.Close : ContentDialogButton.Primary,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
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
            ModeCard.Description = raw ? "剪切：/MOVE 复制后立即删除源文件" : moveHash ? "剪切：全部校验通过后删除源文件" : "复制：保留源文件夹中的文件";
            HashCard.Description = o.Verify ? "复制完成后比对源与目标文件的哈希值" : "已关闭：不比对源与目标文件的内容";
            AdvancedCard.Description = "实际参数：" + RobocopyRunner.Switches(raw);
            CommandText.Text = "robocopy " + RobocopyRunner.BuildArguments(hasSource ? o.Source : "<源文件夹>", hasDest ? o.Destination : "<目标文件夹>", raw);
            DropOverlay.Visibility = dragging ? Visibility.Visible : Visibility.Collapsed;
            bool recognized = phase == Phase.Idle && hasSource && !hasDest;
            HintText.Text = running ? "任务运行中，路径已锁定。" : recognized ? "已识别源文件夹。目标文件夹请点击“浏览…”选择。" : "可以把文件夹拖到这里，设为源文件夹。";
            HintText.Foreground = HintIcon.Foreground = Theme.Brush(recognized ? "AccentTextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush");

            PageTitle.Text = o.Mode == TransferMode.Move ? (raw ? "剪切文件（Robocopy /MOVE）" : "剪切文件并验证完整性") : (o.Verify ? "复制文件并验证完整性" : "复制文件");
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
                    if (dragging) return "松开鼠标即可设为源文件夹。";
                    if (!hasSource && !hasDest) return "选择源文件夹和目标文件夹，开始前可以调整选项。";
                    if (!hasDest) return "已识别源文件夹，请选择目标文件夹。";
                    return moveHash ? "全部文件通过校验后，才会删除源文件。" : raw ? "已关闭完整性校验，复制成功后会立即删除源文件。" : !o.Verify ? "已关闭完整性校验，只复制、不比对文件内容。" : "路径已就绪，确认选项后点击“开始”。";
                case Phase.Running:
                    switch (stage)
                    {
                        case WorkflowStage.Scanning: return "正在扫描源文件夹，路径与选项已锁定。";
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
                    else if (i == 0) { state = hasSource && hasDest ? StepState.Done : StepState.Active; desc = hasSource && hasDest ? "源与目标已就绪" : hasSource ? "已识别源文件夹，请选择目标" : "选择源文件夹与目标位置"; }
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
            if (i == 2) return o.UsesRobocopyMove ? "/MOVE：复制后删除源文件" : "使用 Windows 内置 Robocopy";
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
            bool moveHash = o.Mode == TransferMode.Move && o.Verify, raw = o.UsesRobocopyMove;
            string title = null, message = null; var severity = InfoBarSeverity.Informational;
            if (inputError != null) { title = "路径错误"; message = inputError; severity = InfoBarSeverity.Error; }
            else if (phase == Phase.Idle)
            {
                if (moveHash) { title = "剪切会删除源文件"; message = "复制并通过 " + o.Algorithm + " 校验后才删除源文件，变空的文件夹也会一并删除；复制失败、校验异常或终止时不会删除。"; severity = InfoBarSeverity.Warning; }
                else if (raw) { title = "将使用 Robocopy /MOVE"; message = "关闭校验后，每个文件复制成功就会从源文件夹删除，不比对文件内容；目标中已相同的文件会被跳过并保留在源文件夹。"; severity = InfoBarSeverity.Warning; }
            }
            else if (phase == Phase.Running)
            {
                if (moveHash && stage == WorkflowStage.DeletingSource) { title = "正在删除源文件"; message = "只删除已在目标位置校验一致的文件；终止后，尚未删除的源文件会全部保留。"; }
                else if (moveHash) { title = "源文件尚未删除"; message = "只有全部文件校验一致后，才会开始删除源文件。"; }
                else if (raw && stage == WorkflowStage.Copying) { title = "源文件正在被删除"; message = "Robocopy /MOVE 会在每个文件复制成功后立即删除源文件。"; severity = InfoBarSeverity.Warning; }
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
                    message = workflow.RemainingSourceFiles > 0 ? "已通过 Robocopy /MOVE 移动文件；" + workflow.RemainingSourceFiles + " 个文件在目标中已相同，被跳过并保留在源文件夹。没有比对文件内容。" : p.ScanFiles + " 个文件已通过 Robocopy /MOVE 移动，源文件已删除；没有比对文件内容。";
                }
                else if (!o.Verify) { title = "复制完成（未校验）"; message = p.ScanFiles + " 个文件已复制。已关闭哈希校验，没有比对文件内容。"; }
                else if (p.HashFailed > 0) { title = "任务完成，但有异常文件"; message = "通过 " + p.HashPassed + "，异常 " + p.HashFailed + "。请在“异常文件”中查看。"; severity = InfoBarSeverity.Warning; }
                else { title = "任务完成"; message = p.ScanFiles + " 个文件已复制，" + o.Algorithm + " 校验全部通过。"; severity = InfoBarSeverity.Success; }
            }
            else if (phase == Phase.Terminated)
            {
                title = "任务已终止"; severity = InfoBarSeverity.Warning;
                message = moveHash ? (p.DeleteDone > 0 ? "已删除 " + p.DeleteDone + " 个校验一致的源文件；其余源文件全部保留。" : "源文件夹中的文件没有被删除；已复制到目标位置的文件保留。")
                    : raw ? "已被 Robocopy /MOVE 移动的文件已从源文件夹删除；其余文件仍保留在源文件夹。"
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
                    if (!hasSource || !hasDest) text = "请选择源文件夹和目标文件夹";
                    else if (o.Mode == TransferMode.Move) { text = raw ? "准备就绪 · /MOVE 模式，开始前会再次确认" : "准备就绪 · 剪切模式，开始前会再次确认"; if (raw) { glyph = GlyphWarning; brush = "SystemFillColorCautionBrush"; } }
                    else text = o.Verify ? "准备就绪 · 点击“开始”运行 Robocopy" : "准备就绪 · 将跳过哈希校验";
                    break;
                case Phase.Running:
                    brush = "AccentTextFillColorPrimaryBrush";
                    if (stage == WorkflowStage.Scanning) { glyph = GlyphSearch; text = "正在扫描源文件夹 · 已发现 " + p.ScanFiles + " 个文件"; }
                    else if (stage == WorkflowStage.Copying) { glyph = raw ? GlyphCut : GlyphCopy; text = p.CopyTotal == 0 ? "无需复制，文件已匹配" : (raw ? "Robocopy /MOVE 正在移动 · " : "Robocopy 正在复制 · ") + Format.Bytes(p.CopyDone) + " / " + Format.Bytes(p.CopyTotal); }
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
