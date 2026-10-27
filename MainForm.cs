using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RobocopyGui.Core;

namespace RobocopyGui
{
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
        readonly CopyWorkflow workflow = new CopyWorkflow();
        readonly System.Windows.Forms.Timer paintTimer = new System.Windows.Forms.Timer { Interval = 120 };
        CancellationTokenSource cancellation;
        volatile string workerStatus = "准备就绪";
        int activeStep;

        public MainForm()
        {
            Text = "Robocopy 复制与完整性校验"; StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(900, 490); Size = new Size(980, 530); BackColor = Canvas; ForeColor = Color.White; Font = new Font(UiFont, 9);
            algorithmBox.Items.AddRange(HashVerifier.Algorithms); algorithmBox.SelectedIndex = 0;
            workflow.StepChanged = SetStep; workflow.StatusChanged = delegate (string status) { workerStatus = status; }; workflow.Log = Log; workflow.Failure = failureQueue.Enqueue;
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
            string pathError = CopyWorkflow.ValidatePaths(source, destination); if (pathError != null) { MessageBox.Show(pathError, "路径错误", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            PrepareRun(); cancellation = new CancellationTokenSource();
            try
            {
                await workflow.RunAsync(source, destination, verifyBox.Checked, algorithmBox.Text, (int)chunkBox.Value * 1024 * 1024, cancellation.Token); exportButton.Enabled = true;
            }
            catch (OperationCanceledException) { workerStatus = "任务已停止。"; Log("任务已停止。"); }
            catch (Exception ex) { workerStatus = "任务失败：" + ex.Message; Log(ex.ToString()); MessageBox.Show(ex.Message, "任务失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { workflow.ReleaseRobocopy(); if (cancellation != null) { cancellation.Dispose(); cancellation = null; } SetRunState(false); }
        }

        void PrepareRun()
        {
            workflow.Reset(); while (!logQueue.IsEmpty) { string ignored; logQueue.TryDequeue(out ignored); } while (!failureQueue.IsEmpty) { HashRecord ignored; failureQueue.TryDequeue(out ignored); } log.Clear(); failures.Rows.Clear(); exportButton.Enabled = false; SetRunState(true); SetStep(0); workerStatus = "准备扫描…";
        }

        void PaintState()
        {
            var progress = workflow.Progress; long cTotal = progress.CopyTotal, cDone = progress.CopyDone, hTotal = progress.HashTotal, hDone = progress.HashDone, files = progress.ScanFiles; SetBar(copyBar, cDone, cTotal); SetBar(hashBar, hDone, hTotal);
            copyLabel.Text = cTotal > 0 ? Format.Bytes(Math.Min(cDone, cTotal)) + " / " + Format.Bytes(cTotal) + "  ·  " + Format.Percent(cDone, cTotal) : (files > 0 ? "扫描到 " + files + " 个文件；待复制大小计算中" : "等待扫描");
            hashLabel.Text = hTotal > 0 ? Format.Bytes(Math.Min(hDone, hTotal)) + " / " + Format.Bytes(hTotal) + "  ·  " + Format.Percent(hDone, hTotal) : (verifyBox.Checked ? "等待复制完成" : "已跳过");
            countersLabel.Text = workerStatus + "   通过 " + progress.HashPassed + " · 异常 " + progress.HashFailed;
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
        void StopWorkflow() { if (cancellation != null) cancellation.Cancel(); workflow.KillRobocopy(); }
        void Log(string text) { logQueue.Enqueue(text); }
        void ExportReport()
        {
            if (workflow.Records.Count == 0) { MessageBox.Show("尚无哈希校验结果可以导出。", "提示"); return; } using (var dialog = new SaveFileDialog { Filter = "CSV 文件|*.csv", FileName = "robocopy_hash_report.csv" }) if (dialog.ShowDialog() == DialogResult.OK) { HashReport.Write(dialog.FileName, workflow.Records); MessageBox.Show("完整报告已导出。", "完成"); }
        }
    }
}
