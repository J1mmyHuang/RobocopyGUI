using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RobocopyGui.Core
{
    public enum TransferMode { Copy, Move }

    public enum WorkflowStage { Scanning, Copying, Verifying, DeletingSource, Completed }

    public sealed class RunOptions
    {
        public string Source, Destination, Algorithm = "SHA-256";
        public bool Verify = true;
        public int BlockSize = 4 * 1024 * 1024;
        public TransferMode Mode = TransferMode.Copy;

        // Move without hashing hands deletion to robocopy /MOVE; move with hashing deletes sources only after every file verified.
        public bool UsesRobocopyMove { get { return Mode == TransferMode.Move && !Verify; } }
    }

    public sealed class CopyWorkflow
    {
        readonly RobocopyRunner robocopy = new RobocopyRunner();
        readonly WorkflowProgress progress = new WorkflowProgress();
        List<FilePlan> plan = new List<FilePlan>();
        List<HashRecord> records = new List<HashRecord>();

        public CopyWorkflow() { StepChanged = delegate { }; StageChanged = delegate { }; StatusChanged = delegate { }; Log = delegate { }; Failure = delegate { }; }

        public Action<int> StepChanged { get; set; }
        public Action<WorkflowStage> StageChanged { get; set; }
        public Action<string> StatusChanged { get; set; }
        public Action<string> Log { get; set; }
        public Action<HashRecord> Failure { get; set; }
        public WorkflowProgress Progress { get { return progress; } }
        public IReadOnlyList<FilePlan> Plan { get { return plan; } }
        public IReadOnlyList<HashRecord> Records { get { return records; } }
        public bool SourceDeletionSkipped { get; private set; }
        public int RemainingSourceFiles { get; private set; }

        public static string ValidatePaths(string source, string destination)
        {
            if (!Directory.Exists(source)) return "源文件夹不存在或不可访问。";
            if (string.IsNullOrWhiteSpace(destination)) return "请选择目标文件夹。";
            string fullDestination; try { fullDestination = Path.GetFullPath(destination); } catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return "目标文件夹路径无效。"; }
            if (string.Equals(Path.GetFullPath(source).TrimEnd('\\'), fullDestination.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return "源文件夹和目标文件夹不能相同。";
            return null;
        }

        public void Reset() { plan = new List<FilePlan>(); records = new List<HashRecord>(); progress.Reset(); SourceDeletionSkipped = false; RemainingSourceFiles = 0; }

        public Task RunAsync(string source, string destination, bool verify, string algorithm, int blockSize, CancellationToken token)
        {
            return RunAsync(new RunOptions { Source = source, Destination = destination, Verify = verify, Algorithm = algorithm, BlockSize = blockSize }, token);
        }

        public async Task RunAsync(RunOptions o, CancellationToken token)
        {
            StepChanged(1); StageChanged(WorkflowStage.Scanning); StatusChanged("正在扫描源文件夹…"); plan = await Task.Run(delegate { return CopyPlanner.Build(o.Source, o.Destination, progress, token); }, token); Log("扫描完成：" + plan.Count + " 个文件，" + Format.Bytes(plan.Sum(p => p.Size)) + "。待复制 " + Format.Bytes(progress.CopyTotal) + "。");
            StepChanged(2); StageChanged(WorkflowStage.Copying); StatusChanged(progress.CopyTotal == 0 ? "无需复制，文件已匹配。" : o.UsesRobocopyMove ? "Robocopy 正在移动…" : "Robocopy 正在复制…"); await robocopy.CopyAsync(o.Source, o.Destination, plan, progress, Log, token, o.UsesRobocopyMove);
            if (o.Verify) { StepChanged(3); StageChanged(WorkflowStage.Verifying); StatusChanged("正在计算哈希…"); records = await HashVerifier.VerifyAllAsync(plan, o.Algorithm, o.BlockSize, progress, Failure, token); } else { StatusChanged("已跳过哈希校验。"); }
            if (o.Mode == TransferMode.Move && o.Verify)
            {
                if (progress.HashFailed > 0) { SourceDeletionSkipped = true; Log("存在 " + progress.HashFailed + " 个校验异常，未删除任何源文件。"); }
                else { StageChanged(WorkflowStage.DeletingSource); StatusChanged("正在删除源文件…"); await Task.Run(delegate { SourceCleaner.DeleteVerified(o.Source, plan, records, progress, Log, token); }, token); }
            }
            if (o.UsesRobocopyMove) { RemainingSourceFiles = SourceCleaner.RemainingSources(plan); if (RemainingSourceFiles > 0) Log(RemainingSourceFiles + " 个文件在目标中已存在且相同，Robocopy /MOVE 未移动它们，源文件仍保留。"); }
            StepChanged(4); StageChanged(WorkflowStage.Completed); StatusChanged("任务完成。"); Log("完成。通过 " + progress.HashPassed + "，异常 " + progress.HashFailed + "。");
        }

        public void KillRobocopy() { robocopy.Kill(); }
        public void ReleaseRobocopy() { robocopy.Release(); }
    }
}
