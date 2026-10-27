// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

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
        // Explorer-like: the chosen folder itself goes into the destination. false = Robocopy's own "copy the contents" semantics.
        public bool IncludeSourceFolder = true;

        // Move without hashing hands deletion to robocopy /MOVE (/MOV for a single file); move with hashing deletes sources only after every file verified.
        public bool UsesRobocopyMove { get { return Mode == TransferMode.Move && !Verify; } }

        public TransferPaths ResolvePaths() { return TransferPaths.Resolve(Source, Destination, IncludeSourceFolder); }
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

        // Checks a folder-or-file source against the place it would actually end up, so nothing is copied into itself or onto itself.
        public static string Validate(RunOptions o)
        {
            string source = o.Source ?? "", destination = o.Destination ?? "";
            if (!Directory.Exists(source) && !File.Exists(source)) return "源文件夹或文件不存在，或无法访问。";
            if (string.IsNullOrWhiteSpace(destination)) return "请选择目标文件夹。";
            TransferPaths paths; try { paths = o.ResolvePaths(); } catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return "目标文件夹路径无效。"; }
            if (File.Exists(paths.Destination)) return "目标必须是文件夹，不能是文件。";
            if (paths.Kind == SourceKind.File) return TransferPaths.Same(paths.SourceFolder, paths.Destination) ? "源文件已经位于该目标文件夹中。" : null;
            if (o.Mode == TransferMode.Move && paths.IsDriveRoot) return "不能剪切整个磁盘，请改用复制。";
            if (TransferPaths.Same(paths.SourceFolder, paths.Destination)) return "源文件夹和目标文件夹不能相同。";
            if (TransferPaths.Inside(paths.Destination, paths.SourceFolder)) return "目标文件夹不能位于源文件夹内。";
            if (TransferPaths.Same(paths.Result, paths.SourceFolder)) return "源文件夹已经位于该目标文件夹中。";
            return null;
        }

        public void Reset() { plan = new List<FilePlan>(); records = new List<HashRecord>(); progress.Reset(); SourceDeletionSkipped = false; RemainingSourceFiles = 0; }

        // Used by the classic WinForms UI: keeps Robocopy's "copy the folder's contents" semantics.
        public Task RunAsync(string source, string destination, bool verify, string algorithm, int blockSize, CancellationToken token)
        {
            return RunAsync(new RunOptions { Source = source, Destination = destination, Verify = verify, Algorithm = algorithm, BlockSize = blockSize, IncludeSourceFolder = false }, token);
        }

        public async Task RunAsync(RunOptions o, CancellationToken token)
        {
            var paths = o.ResolvePaths();
            StepChanged(1); StageChanged(WorkflowStage.Scanning); StatusChanged("正在扫描源文件夹…"); plan = await Task.Run(delegate { return CopyPlanner.Build(paths, progress, token); }, token); Log("扫描完成：" + plan.Count + " 个文件，" + Format.Bytes(plan.Sum(p => p.Size)) + "。待复制 " + Format.Bytes(progress.CopyTotal) + "。");
            StepChanged(2); StageChanged(WorkflowStage.Copying); StatusChanged(progress.CopyTotal == 0 ? "无需复制，文件已匹配。" : o.UsesRobocopyMove ? "Robocopy 正在移动…" : "Robocopy 正在复制…"); await robocopy.CopyAsync(paths, plan, progress, Log, token, o.UsesRobocopyMove);
            if (o.Verify) { StepChanged(3); StageChanged(WorkflowStage.Verifying); StatusChanged("正在计算哈希…"); records = await HashVerifier.VerifyAllAsync(plan, o.Algorithm, o.BlockSize, progress, Failure, token); } else { StatusChanged("已跳过哈希校验。"); }
            if (o.Mode == TransferMode.Move && o.Verify)
            {
                if (progress.HashFailed > 0) { SourceDeletionSkipped = true; Log("存在 " + progress.HashFailed + " 个校验异常，未删除任何源文件。"); }
                else { StageChanged(WorkflowStage.DeletingSource); StatusChanged("正在删除源文件…"); await Task.Run(delegate { SourceCleaner.DeleteVerified(paths.SourceFolder, plan, records, progress, Log, token, paths.Kind == SourceKind.Folder); }, token); }
            }
            if (o.UsesRobocopyMove) { RemainingSourceFiles = SourceCleaner.RemainingSources(plan); if (RemainingSourceFiles > 0) Log(RemainingSourceFiles + " 个文件在目标中已存在且相同，Robocopy " + RobocopyRunner.MoveSwitch(paths.Kind) + " 未移动它们，源文件仍保留。"); }
            StepChanged(4); StageChanged(WorkflowStage.Completed); StatusChanged("任务完成。"); Log("完成。通过 " + progress.HashPassed + "，异常 " + progress.HashFailed + "。");
        }

        public void KillRobocopy() { robocopy.Kill(); }
        public void ReleaseRobocopy() { robocopy.Release(); }
    }
}
