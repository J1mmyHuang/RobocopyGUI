using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RobocopyGui
{
    sealed class FilePlan
    {
        public string Source, Target, RelativePath;
        public long Size, ExistingSize;
        public DateTime ExistingWriteTime;
        public bool NeedsCopy;
    }

    sealed class HashRecord
    {
        public string RelativePath, Result, SourceHash, TargetHash, Detail;
        public string[] ToRow() { return new[] { RelativePath, Result, SourceHash, TargetHash, Detail }; }
    }

    internal sealed class MainForm : Form
    {
        const string UiFont = "Smiley Sans";
        readonly Color Canvas = Color.FromArgb(16, 24, 36), Card = Color.FromArgb(28, 39, 55), Input = Color.FromArgb(38, 52, 70), Accent = Color.FromArgb(40, 190, 190), Muted = Color.FromArgb(164, 184, 204);
        readonly TextBox sourceBox = new TextBox(), destinationBox = new TextBox();
        readonly CheckBox verifyBox = new CheckBox { Text = "复制完成后校验哈希", Checked = true, AutoSize = true };
        readonly ComboBox algorithmBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 112 };
        readonly NumericUpDown chunkBox = new NumericUpDown { Minimum = 1, Maximum = 16, Value = 4, Width = 60 };
        readonly Button startButton = new Button { Text = "开始任务", Width = 110, Height = 34 }, stopButton = new Button { Text = "停止", Width = 84, Height = 34, Enabled = false }, exportButton = new Button { Text = "导出完整报告", Width = 120, Height = 30 };
        readonly ProgressBar copyBar = new ProgressBar { Dock = DockStyle.Fill }, hashBar = new ProgressBar { Dock = DockStyle.Fill };
        readonly Label phaseLabel = new Label { AutoSize = true, Font = new Font(UiFont, 12, FontStyle.Bold) }, copyLabel = new Label { AutoSize = true }, hashLabel = new Label { AutoSize = true }, countersLabel = new Label { AutoSize = true };
        readonly RichTextBox log = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, Font = new Font(UiFont, 9) };
        readonly DataGridView failures = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        readonly TabControl detailsTabs = new TabControl { Dock = DockStyle.Fill, Visible = false };
        TableLayoutPanel mainLayout;
        readonly Label[] stepLabels = new Label[5];
        readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<HashRecord> failureQueue = new ConcurrentQueue<HashRecord>();
        readonly List<FilePlan> plan = new List<FilePlan>();
        readonly List<HashRecord> records = new List<HashRecord>();
        readonly System.Windows.Forms.Timer paintTimer = new System.Windows.Forms.Timer { Interval = 120 };
        CancellationTokenSource cancellation;
        Process activeProcess;
        long copyDone, copyTotal, hashDone, hashTotal, scanFiles, scanBytes;
        int hashPassed, hashFailed;
        volatile string workerStatus = "准备就绪";
        int activeStep;

        public MainForm()
        {
            Text = "Robocopy 复制与完整性校验"; StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(900, 490); Size = new Size(980, 530); BackColor = Canvas; ForeColor = Color.White; Font = new Font(UiFont, 9);
            algorithmBox.Items.AddRange(new object[] { "SHA-256", "SHA-512", "SHA-1", "MD5" }); algorithmBox.SelectedIndex = 0;
            BuildUi(); paintTimer.Tick += delegate { PaintState(); }; paintTimer.Start();
        }

        void BuildUi()
        {
            var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Canvas, Padding = new Padding(16) };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Controls.Add(outer);
            outer.Controls.Add(BuildSteps(), 0, 0); outer.Controls.Add(BuildMain(), 1, 0);
        }

        Control BuildSteps()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Card, Padding = new Padding(16) };
            panel.Controls.Add(new Label { Text = "复制向导", ForeColor = Color.White, Font = new Font(UiFont, 16, FontStyle.Bold), AutoSize = true, Location = new Point(16, 18) });
            string[] names = { "1  选择路径", "2  扫描清单", "3  Robocopy 复制", "4  哈希校验", "5  完成与报告" };
            for (int i = 0; i < names.Length; i++)
            {
                var label = new Label { Text = names[i], AutoSize = false, Width = 148, Height = 38, Location = new Point(16, 75 + i * 52), Padding = new Padding(10, 9, 0, 0), Font = new Font(UiFont, 10), BackColor = Card, ForeColor = Muted };
                stepLabels[i] = label; panel.Controls.Add(label);
            }
            panel.Controls.Add(new Label { Text = "真实进度\n\n复制：依据目标端已确认写入或匹配的字节数。\n\n校验：依据实际参与哈希的读取字节数。", ForeColor = Muted, Font = new Font(UiFont, 9), Width = 148, Height = 155, Location = new Point(16, 380) });
            SetStep(0); return panel;
        }

        Control BuildMain()
        {
            mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, BackColor = Canvas, Padding = new Padding(18, 0, 0, 0) };
            var root = mainLayout;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 140)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            phaseLabel.Text = "第 1 步：选择复制路径"; phaseLabel.ForeColor = Color.White; root.Controls.Add(phaseLabel, 0, 0);
            root.Controls.Add(BuildPathCard(), 0, 1); root.Controls.Add(BuildProgressCard(), 0, 2);
            var logPage = new TabPage("运行日志") { BackColor = Input, Padding = new Padding(8) }; var failurePage = new TabPage("异常文件") { BackColor = Input, Padding = new Padding(8) }; StyleLog(); StyleGrid(); logPage.Controls.Add(log); failurePage.Controls.Add(failures); detailsTabs.TabPages.Add(logPage); detailsTabs.TabPages.Add(failurePage); root.Controls.Add(detailsTabs, 0, 3);
            var detailsToggle = new Button { Text = "显示详细信息 ▾", Width = 130, Height = 30 }; var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, BackColor = Canvas, Padding = new Padding(0, 5, 0, 0) }; StyleButton(startButton, Accent); StyleButton(stopButton, Color.FromArgb(91, 108, 128)); StyleButton(exportButton, Color.FromArgb(55, 76, 100)); StyleButton(detailsToggle, Color.FromArgb(55, 76, 100)); exportButton.Enabled = false; startButton.Click += async delegate { await StartWorkflow(); }; stopButton.Click += delegate { StopWorkflow(); }; exportButton.Click += delegate { ExportReport(); }; detailsToggle.Click += delegate { ToggleDetails(detailsToggle); }; countersLabel.ForeColor = Muted; countersLabel.Padding = new Padding(18, 8, 0, 0); actions.Controls.Add(startButton); actions.Controls.Add(stopButton); actions.Controls.Add(exportButton); actions.Controls.Add(detailsToggle); actions.Controls.Add(countersLabel); root.Controls.Add(actions, 0, 4);
            return root;
        }

        Control BuildPathCard()
        {
            var card = MakeCard("路径与选项"); var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Padding = new Padding(12) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            AddPathRow(grid, 0, "源文件夹", sourceBox, "选择源文件夹"); AddPathRow(grid, 1, "目标文件夹", destinationBox, "选择目标文件夹");
            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Card, WrapContents = false }; verifyBox.ForeColor = Color.White; verifyBox.BackColor = Card; options.Controls.Add(verifyBox); options.Controls.Add(new Label { Text = "算法", ForeColor = Muted, AutoSize = true, Padding = new Padding(20, 6, 5, 0) }); algorithmBox.BackColor = Input; algorithmBox.ForeColor = Color.White; options.Controls.Add(algorithmBox); options.Controls.Add(new Label { Text = "读取块", ForeColor = Muted, AutoSize = true, Padding = new Padding(20, 6, 5, 0) }); chunkBox.BackColor = Input; chunkBox.ForeColor = Color.White; options.Controls.Add(chunkBox); options.Controls.Add(new Label { Text = "MB", ForeColor = Muted, AutoSize = true, Padding = new Padding(5, 6, 0, 0) }); grid.Controls.Add(options, 1, 2); card.Controls.Add(grid); return card;
        }

        Control BuildProgressCard()
        {
            var card = MakeCard("真实进度"); var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 2 }; grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            copyLabel.ForeColor = Muted; hashLabel.ForeColor = Muted; copyBar.Style = ProgressBarStyle.Continuous; hashBar.Style = ProgressBarStyle.Continuous; grid.Controls.Add(new Label { Text = "复制", ForeColor = Color.White, AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, 0, 0); grid.Controls.Add(WrapProgress(copyBar, copyLabel), 1, 0); grid.Controls.Add(new Label { Text = "校验", ForeColor = Color.White, AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, 0, 1); grid.Controls.Add(WrapProgress(hashBar, hashLabel), 1, 1); card.Controls.Add(grid); return card;
        }

        Control WrapProgress(ProgressBar bar, Label label) { var p = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 }; p.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); p.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); label.AutoSize = false; label.Dock = DockStyle.Fill; label.TextAlign = ContentAlignment.MiddleLeft; p.Controls.Add(bar, 0, 0); p.Controls.Add(label, 0, 1); return p; }
        GroupBox MakeCard(string text) { return new GroupBox { Text = text, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(205, 220, 235), BackColor = Card, Font = new Font(UiFont, 9) }; }
        void StyleButton(Button button, Color color) { button.BackColor = color; button.ForeColor = Color.White; button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.Cursor = Cursors.Hand; }
        void AddPathRow(TableLayoutPanel grid, int row, string label, TextBox box, string title)
        {
            grid.Controls.Add(new Label { Text = label, ForeColor = Color.White, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) }, 0, row); box.Dock = DockStyle.Fill; box.BackColor = Input; box.ForeColor = Color.White; box.BorderStyle = BorderStyle.FixedSingle; grid.Controls.Add(box, 1, row);
            var browse = new Button { Text = "浏览…", Dock = DockStyle.Fill }; StyleButton(browse, Color.FromArgb(55, 76, 100)); browse.Click += delegate { using (var picker = new FolderBrowserDialog { Description = title }) if (picker.ShowDialog() == DialogResult.OK) box.Text = picker.SelectedPath; }; grid.Controls.Add(browse, 2, row);
        }
        void StyleLog() { log.BackColor = Input; log.ForeColor = Color.FromArgb(220, 230, 240); }
        void StyleGrid() { failures.BackgroundColor = Input; failures.GridColor = Color.FromArgb(60, 78, 98); failures.EnableHeadersVisualStyles = false; failures.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(52, 70, 91); failures.ColumnHeadersDefaultCellStyle.ForeColor = Color.White; failures.DefaultCellStyle.BackColor = Input; failures.DefaultCellStyle.ForeColor = Color.White; failures.Columns.Add("File", "相对路径"); failures.Columns.Add("Result", "结果"); failures.Columns.Add("Source", "源哈希"); failures.Columns.Add("Target", "目标哈希"); failures.Columns.Add("Detail", "说明"); }

        async Task StartWorkflow()
        {
            if (cancellation != null) return; string source = sourceBox.Text.Trim().Trim('"'), destination = destinationBox.Text.Trim().Trim('"');
            if (!Directory.Exists(source)) { MessageBox.Show("源文件夹不存在或不可访问。", "路径错误", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (string.Equals(Path.GetFullPath(source).TrimEnd('\\'), Path.GetFullPath(destination).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) { MessageBox.Show("源文件夹和目标文件夹不能相同。", "路径错误", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            PrepareRun(); cancellation = new CancellationTokenSource();
            try
            {
                SetStep(1); workerStatus = "正在扫描源文件夹…"; await Task.Run(delegate { BuildPlan(source, destination, cancellation.Token); }, cancellation.Token); Log("扫描完成：" + plan.Count + " 个文件，" + FormatBytes(plan.Sum(p => p.Size)) + "。待复制 " + FormatBytes(copyTotal) + "。");
                SetStep(2); workerStatus = copyTotal == 0 ? "无需复制，文件已匹配。" : "Robocopy 正在复制…"; await CopyWithRobocopy(source, destination, cancellation.Token);
                if (verifyBox.Checked) { SetStep(3); workerStatus = "正在计算哈希…"; await VerifyAll(cancellation.Token); } else { workerStatus = "已跳过哈希校验。"; }
                SetStep(4); workerStatus = "任务完成。"; exportButton.Enabled = true; Log("完成。通过 " + hashPassed + "，异常 " + hashFailed + "。");
            }
            catch (OperationCanceledException) { workerStatus = "任务已停止。"; Log("任务已停止。"); }
            catch (Exception ex) { workerStatus = "任务失败：" + ex.Message; Log(ex.ToString()); MessageBox.Show(ex.Message, "任务失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { if (activeProcess != null) { activeProcess.Dispose(); activeProcess = null; } if (cancellation != null) { cancellation.Dispose(); cancellation = null; } SetRunState(false); }
        }

        void PrepareRun()
        {
            plan.Clear(); records.Clear(); while (!logQueue.IsEmpty) { string ignored; logQueue.TryDequeue(out ignored); } while (!failureQueue.IsEmpty) { HashRecord ignored; failureQueue.TryDequeue(out ignored); } log.Clear(); failures.Rows.Clear(); exportButton.Enabled = false; copyDone = copyTotal = hashDone = hashTotal = scanFiles = scanBytes = 0; hashPassed = hashFailed = 0; SetRunState(true); SetStep(0); workerStatus = "准备扫描…";
        }

        void BuildPlan(string source, string destination, CancellationToken token)
        {
            Directory.CreateDirectory(destination); foreach (string dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) { token.ThrowIfCancellationRequested(); Directory.CreateDirectory(Path.Combine(destination, Relative(source, dir))); }
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested(); var info = new FileInfo(file); string relative = Relative(source, file), target = Path.Combine(destination, relative); var item = new FilePlan { Source = file, Target = target, RelativePath = relative, Size = info.Length, NeedsCopy = true };
                if (File.Exists(target)) { var existing = new FileInfo(target); item.ExistingSize = existing.Length; item.ExistingWriteTime = existing.LastWriteTimeUtc; item.NeedsCopy = existing.Length != info.Length || Math.Abs((existing.LastWriteTimeUtc - info.LastWriteTimeUtc).TotalSeconds) > 2; }
                plan.Add(item); Interlocked.Increment(ref scanFiles); Interlocked.Add(ref scanBytes, item.Size);
            }
            Interlocked.Exchange(ref copyTotal, plan.Where(p => p.NeedsCopy).Sum(p => p.Size));
        }

        async Task CopyWithRobocopy(string source, string destination, CancellationToken token)
        {
            if (copyTotal == 0) return; var monitorStop = new CancellationTokenSource(); Task monitor = Task.Run(delegate { MonitorCopy(token, monitorStop.Token); });
            Exception runError = null;
            try
            {
                string args = "\"" + source + "\" \"" + destination + "\" /E /Z /FFT /R:2 /W:2 /COPY:DAT /DCOPY:DAT /TEE /NP /NDL"; Log("$ robocopy " + args); int code = await Task.Run(delegate { return RunRobocopy(args, token); }, token); if (code >= 8) runError = new InvalidOperationException("Robocopy 返回错误代码 " + code + "。");
            }
            catch (Exception ex) { runError = ex; }
            monitorStop.Cancel(); try { await monitor; } catch (OperationCanceledException) { } monitorStop.Dispose();
            if (runError != null) throw runError; Interlocked.Exchange(ref copyDone, copyTotal);
        }

        int RunRobocopy(string args, CancellationToken token)
        {
            using (var process = new Process { StartInfo = new ProcessStartInfo("robocopy.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.Default } })
            {
                activeProcess = process; process.Start(); string line; while ((line = process.StandardOutput.ReadLine()) != null) { if (!string.IsNullOrWhiteSpace(line)) Log(line); token.ThrowIfCancellationRequested(); } process.WaitForExit(); activeProcess = null; token.ThrowIfCancellationRequested(); return process.ExitCode;
            }
        }

        void MonitorCopy(CancellationToken workflowToken, CancellationToken monitorToken)
        {
            while (!monitorToken.IsCancellationRequested)
            {
                workflowToken.ThrowIfCancellationRequested(); long confirmed = 0;
                foreach (var item in plan.Where(p => p.NeedsCopy))
                {
                    try
                    {
                        if (!File.Exists(item.Target)) continue; var target = new FileInfo(item.Target); bool matches = target.Length == item.Size && Math.Abs((target.LastWriteTimeUtc - File.GetLastWriteTimeUtc(item.Source)).TotalSeconds) <= 2;
                        if (matches) confirmed += item.Size;
                        else if (item.ExistingSize != target.Length || item.ExistingWriteTime != target.LastWriteTimeUtc) confirmed += Math.Min(item.Size, target.Length);
                    }
                    catch { }
                }
                Interlocked.Exchange(ref copyDone, Math.Min(copyTotal, confirmed)); Thread.Sleep(350);
            }
        }

        async Task VerifyAll(CancellationToken token)
        {
            long targetBytes = 0; foreach (var item in plan) { token.ThrowIfCancellationRequested(); if (File.Exists(item.Target)) targetBytes += new FileInfo(item.Target).Length; } Interlocked.Exchange(ref hashTotal, plan.Sum(p => p.Size) + targetBytes); Interlocked.Exchange(ref hashDone, 0);
            int blockSize = (int)chunkBox.Value * 1024 * 1024; string algorithm = algorithmBox.Text;
            await Task.Run(delegate
            {
                foreach (var item in plan)
                {
                    token.ThrowIfCancellationRequested(); var record = new HashRecord { RelativePath = item.RelativePath, Result = "通过", Detail = "哈希一致" };
                    try
                    {
                        record.SourceHash = HashFile(item.Source, algorithm, blockSize, token); if (!File.Exists(item.Target)) { record.Result = "缺失"; record.Detail = "目标文件不存在"; } else { record.TargetHash = HashFile(item.Target, algorithm, blockSize, token); if (record.SourceHash != record.TargetHash) { record.Result = "失败"; record.Detail = "哈希不一致"; } }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { record.Result = "错误"; record.Detail = ex.Message; }
                    records.Add(record); if (record.Result == "通过") Interlocked.Increment(ref hashPassed); else { Interlocked.Increment(ref hashFailed); failureQueue.Enqueue(record); }
                }
            }, token);
        }

        string HashFile(string path, string algorithm, int blockSize, CancellationToken token)
        {
            HashAlgorithm hash; if (algorithm == "MD5") hash = MD5.Create(); else if (algorithm == "SHA-1") hash = SHA1.Create(); else if (algorithm == "SHA-512") hash = SHA512.Create(); else hash = SHA256.Create();
            using (hash) using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, blockSize, FileOptions.SequentialScan)) { byte[] buffer = new byte[blockSize]; int read; while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); hash.TransformBlock(buffer, 0, read, buffer, 0); Interlocked.Add(ref hashDone, read); } hash.TransformFinalBlock(new byte[0], 0, 0); return BitConverter.ToString(hash.Hash).Replace("-", ""); }
        }

        void PaintState()
        {
            long cTotal = Interlocked.Read(ref copyTotal), cDone = Interlocked.Read(ref copyDone), hTotal = Interlocked.Read(ref hashTotal), hDone = Interlocked.Read(ref hashDone), files = Interlocked.Read(ref scanFiles), bytes = Interlocked.Read(ref scanBytes); SetBar(copyBar, cDone, cTotal); SetBar(hashBar, hDone, hTotal);
            copyLabel.Text = cTotal > 0 ? FormatBytes(Math.Min(cDone, cTotal)) + " / " + FormatBytes(cTotal) + "  ·  " + Percent(cDone, cTotal) : (files > 0 ? "扫描到 " + files + " 个文件；待复制大小计算中" : "等待扫描");
            hashLabel.Text = hTotal > 0 ? FormatBytes(Math.Min(hDone, hTotal)) + " / " + FormatBytes(hTotal) + "  ·  " + Percent(hDone, hTotal) : (verifyBox.Checked ? "等待复制完成" : "已跳过");
            countersLabel.Text = workerStatus + "   通过 " + hashPassed + " · 异常 " + hashFailed;
            bool followLog = IsLogAtBottom(); bool appendedLog = false;
            for (int i = 0; i < 80; i++) { string line; if (!logQueue.TryDequeue(out line)) break; log.AppendText(line + Environment.NewLine); appendedLog = true; }
            if (appendedLog && followLog) { log.SelectionStart = log.TextLength; log.ScrollToCaret(); }
            for (int i = 0; i < 40; i++) { HashRecord record; if (!failureQueue.TryDequeue(out record)) break; int index = failures.Rows.Add(record.ToRow()); failures.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(255, 132, 132); }
        }

        bool IsLogAtBottom()
        {
            if (log.TextLength == 0) return true;
            int x = Math.Max(0, log.ClientSize.Width - 4), y = Math.Max(0, log.ClientSize.Height - 4);
            return log.GetCharIndexFromPosition(new Point(x, y)) >= log.TextLength - 4;
        }

        void ToggleDetails(Button button)
        {
            bool show = !detailsTabs.Visible; detailsTabs.Visible = show;
            mainLayout.RowStyles[3].SizeType = show ? SizeType.Percent : SizeType.Absolute;
            mainLayout.RowStyles[3].Height = show ? 100 : 0;
            button.Text = show ? "收起详细信息 ▴" : "显示详细信息 ▾";
            if (show && Height < 700) Height = 760;
            if (!show && Height > 560) Height = 530;
        }

        void SetBar(ProgressBar bar, long done, long total) { if (total <= 0) { bar.Value = 0; return; } bar.Minimum = 0; bar.Maximum = 10000; bar.Value = (int)Math.Max(0, Math.Min(10000, done * 10000 / total)); }
        void SetStep(int step) { activeStep = step; for (int i = 0; i < stepLabels.Length; i++) { stepLabels[i].BackColor = i == step ? Accent : Card; stepLabels[i].ForeColor = i == step ? Color.White : (i < step ? Color.FromArgb(125, 224, 192) : Muted); } string[] labels = { "第 1 步：选择复制路径", "第 2 步：扫描复制清单", "第 3 步：Robocopy 复制", "第 4 步：哈希校验", "第 5 步：完成与报告" }; phaseLabel.Text = labels[step]; }
        void SetRunState(bool running) { startButton.Enabled = !running; stopButton.Enabled = running; sourceBox.Enabled = !running; destinationBox.Enabled = !running; verifyBox.Enabled = !running; algorithmBox.Enabled = !running; chunkBox.Enabled = !running; }
        void StopWorkflow() { if (cancellation != null) cancellation.Cancel(); var process = activeProcess; if (process != null) try { process.Kill(); } catch { } }
        void Log(string text) { logQueue.Enqueue(text); }
        void ExportReport()
        {
            if (records.Count == 0) { MessageBox.Show("尚无哈希校验结果可以导出。", "提示"); return; } using (var dialog = new SaveFileDialog { Filter = "CSV 文件|*.csv", FileName = "robocopy_hash_report.csv" }) if (dialog.ShowDialog() == DialogResult.OK) using (var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true))) { writer.WriteLine("相对路径,结果,源哈希,目标哈希,说明"); foreach (var row in records) writer.WriteLine(string.Join(",", row.ToRow().Select(v => "\"" + (v ?? "").Replace("\"", "\"\"") + "\""))); MessageBox.Show("完整报告已导出。", "完成"); }
        }
        static string Relative(string root, string full) { return full.Substring(root.TrimEnd('\\').Length).TrimStart('\\'); }
        static string Percent(long value, long total) { return (total == 0 ? 0 : Math.Min(100, Math.Max(0, value * 100.0 / total))).ToString("0.0", CultureInfo.InvariantCulture) + "%"; }
        static string FormatBytes(long bytes) { string[] units = { "B", "KB", "MB", "GB", "TB" }; double value = bytes; int unit = 0; while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; } return value.ToString(unit == 0 ? "0" : "0.##", CultureInfo.InvariantCulture) + " " + units[unit]; }
    }

    internal static class Program { [STAThread] static void Main() { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainForm()); } }
}
