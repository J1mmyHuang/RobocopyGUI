using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RobocopyGui.Core
{
    public sealed class CopyWorkflow
    {
        readonly RobocopyRunner robocopy = new RobocopyRunner();
        readonly WorkflowProgress progress = new WorkflowProgress();
        List<FilePlan> plan = new List<FilePlan>();
        List<HashRecord> records = new List<HashRecord>();

        public CopyWorkflow() { StepChanged = delegate { }; StatusChanged = delegate { }; Log = delegate { }; Failure = delegate { }; }

        public Action<int> StepChanged { get; set; }
        public Action<string> StatusChanged { get; set; }
        public Action<string> Log { get; set; }
        public Action<HashRecord> Failure { get; set; }
        public WorkflowProgress Progress { get { return progress; } }
        public IReadOnlyList<FilePlan> Plan { get { return plan; } }
        public IReadOnlyList<HashRecord> Records { get { return records; } }

        public static string ValidatePaths(string source, string destination)
        {
            if (!Directory.Exists(source)) return "源文件夹不存在或不可访问。";
            if (string.IsNullOrWhiteSpace(destination)) return "请选择目标文件夹。";
            string fullDestination; try { fullDestination = Path.GetFullPath(destination); } catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return "目标文件夹路径无效。"; }
            if (string.Equals(Path.GetFullPath(source).TrimEnd('\\'), fullDestination.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return "源文件夹和目标文件夹不能相同。";
            return null;
        }

        public void Reset() { plan = new List<FilePlan>(); records = new List<HashRecord>(); progress.Reset(); }

        public async Task RunAsync(string source, string destination, bool verify, string algorithm, int blockSize, CancellationToken token)
        {
            StepChanged(1); StatusChanged("正在扫描源文件夹…"); plan = await Task.Run(delegate { return CopyPlanner.Build(source, destination, progress, token); }, token); Log("扫描完成：" + plan.Count + " 个文件，" + Format.Bytes(plan.Sum(p => p.Size)) + "。待复制 " + Format.Bytes(progress.CopyTotal) + "。");
            StepChanged(2); StatusChanged(progress.CopyTotal == 0 ? "无需复制，文件已匹配。" : "Robocopy 正在复制…"); await robocopy.CopyAsync(source, destination, plan, progress, Log, token);
            if (verify) { StepChanged(3); StatusChanged("正在计算哈希…"); records = await HashVerifier.VerifyAllAsync(plan, algorithm, blockSize, progress, Failure, token); } else { StatusChanged("已跳过哈希校验。"); }
            StepChanged(4); StatusChanged("任务完成。"); Log("完成。通过 " + progress.HashPassed + "，异常 " + progress.HashFailed + "。");
        }

        public void KillRobocopy() { robocopy.Kill(); }
        public void ReleaseRobocopy() { robocopy.Release(); }
    }
}
