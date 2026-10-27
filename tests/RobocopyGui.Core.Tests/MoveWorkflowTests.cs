// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RobocopyGui.Core.Tests
{
    public class MoveWorkflowTests
    {
        static readonly DateTime Stamp = new DateTime(2025, 7, 1, 8, 0, 0, DateTimeKind.Utc);

        sealed class Recorder
        {
            public readonly List<WorkflowStage> Stages = new List<WorkflowStage>();
            public readonly ConcurrentQueue<string> Logs = new ConcurrentQueue<string>();
            public CopyWorkflow Attach(CopyWorkflow w) { w.StageChanged = Stages.Add; w.Log = Logs.Enqueue; return w; }
        }

        [Fact]
        public void BuildArguments_AddsMoveOnlyWhenAsked()
        {
            Assert.Equal("\"E:\\s\" \"D:\\d\" /E /Z /FFT /R:2 /W:2 /COPY:DAT /DCOPY:DAT /MOVE /TEE /NP /NDL", RobocopyRunner.BuildArguments(@"E:\s", @"D:\d", true));
            Assert.Equal("/E /Z /FFT /R:2 /W:2 /COPY:DAT /DCOPY:DAT", RobocopyRunner.Switches(false));
            Assert.False(new RunOptions { Mode = TransferMode.Move, Verify = true }.UsesRobocopyMove);
            Assert.True(new RunOptions { Mode = TransferMode.Move, Verify = false }.UsesRobocopyMove);
            Assert.False(new RunOptions { Mode = TransferMode.Copy, Verify = false }.UsesRobocopyMove);
        }

        [Fact]
        public async Task MoveWithHash_DeletesSourceOnlyAfterEverythingVerified()
        {
            using (var work = new TempDir())
            {
                string src = work.Combine("src"), dst = work.Combine("dst");
                work.WriteFile(@"src\a.txt", "alpha"); work.WriteFile(@"src\2025\b.txt", "beta");
                var rec = new Recorder(); var w = rec.Attach(new CopyWorkflow());
                await w.RunAsync(new RunOptions { Source = src, Destination = dst, Mode = TransferMode.Move, Verify = true }, CancellationToken.None);
                Assert.Equal(new[] { WorkflowStage.Scanning, WorkflowStage.Copying, WorkflowStage.Verifying, WorkflowStage.DeletingSource, WorkflowStage.Completed }, rec.Stages);
                Assert.False(Directory.Exists(src));
                Assert.Equal("alpha", File.ReadAllText(Path.Combine(dst, @"src\a.txt"))); Assert.Equal("beta", File.ReadAllText(Path.Combine(dst, @"src\2025\b.txt")));
                Assert.Equal(2, w.Progress.DeleteDone); Assert.False(w.SourceDeletionSkipped);
                Assert.DoesNotContain(rec.Logs, l => l.Contains("/MOVE"));
            }
        }

        [Fact]
        public async Task MoveWithHash_DeletesNothingWhenAnyFileFailsVerification()
        {
            using (var work = new TempDir())
            {
                string src = work.Combine("src"), dst = work.Combine("dst");
                work.WriteFile(@"src\good.txt", "good", Stamp); work.WriteFile(@"src\bad.txt", "AAAA", Stamp); work.WriteFile(@"dst\src\bad.txt", "BBBB", Stamp);
                var rec = new Recorder(); var w = rec.Attach(new CopyWorkflow());
                await w.RunAsync(new RunOptions { Source = src, Destination = dst, Mode = TransferMode.Move, Verify = true }, CancellationToken.None);
                Assert.True(w.SourceDeletionSkipped); Assert.DoesNotContain(WorkflowStage.DeletingSource, rec.Stages);
                Assert.True(File.Exists(Path.Combine(src, "good.txt"))); Assert.True(File.Exists(Path.Combine(src, "bad.txt")));
                Assert.Contains(rec.Logs, l => l == "存在 1 个校验异常，未删除任何源文件。");
            }
        }

        [Fact]
        public async Task MoveWithHash_DeletesNothingWhenCancelled()
        {
            using (var work = new TempDir())
            {
                string src = work.Combine("src"); work.WriteFile(@"src\a.txt", "alpha");
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CopyWorkflow().RunAsync(new RunOptions { Source = src, Destination = work.Combine("dst"), Mode = TransferMode.Move, Verify = true }, new CancellationToken(true)));
                Assert.True(File.Exists(Path.Combine(src, "a.txt")));
            }
        }

        [Fact]
        public async Task MoveWithoutHash_UsesRobocopyMove()
        {
            using (var work = new TempDir())
            {
                string src = work.Combine("src"), dst = work.Combine("dst");
                work.WriteFile(@"src\a.txt", "alpha"); work.WriteFile(@"src\sub\b.txt", "beta");
                var rec = new Recorder(); var w = rec.Attach(new CopyWorkflow());
                await w.RunAsync(new RunOptions { Source = src, Destination = dst, Mode = TransferMode.Move, Verify = false }, CancellationToken.None);
                Assert.Equal(new[] { WorkflowStage.Scanning, WorkflowStage.Copying, WorkflowStage.Completed }, rec.Stages);
                Assert.Contains(rec.Logs, l => l.StartsWith("$ robocopy ") && l.Contains(" /MOVE "));
                Assert.False(Directory.Exists(src)); Assert.Equal("beta", File.ReadAllText(Path.Combine(dst, @"src\sub\b.txt")));
                Assert.Equal(0, w.RemainingSourceFiles); Assert.Empty(w.Records);
            }
        }

        [Fact]
        public async Task MoveWithoutHash_ReportsFilesRobocopySkippedAsIdentical()
        {
            using (var work = new TempDir())
            {
                string src = work.Combine("src"), dst = work.Combine("dst");
                work.WriteFile(@"src\same.txt", "same", Stamp); work.WriteFile(@"dst\src\same.txt", "same", Stamp); work.WriteFile(@"src\new.txt", "new");
                var rec = new Recorder(); var w = rec.Attach(new CopyWorkflow());
                await w.RunAsync(new RunOptions { Source = src, Destination = dst, Mode = TransferMode.Move, Verify = false }, CancellationToken.None);
                Assert.False(File.Exists(Path.Combine(src, "new.txt"))); Assert.True(File.Exists(Path.Combine(src, "same.txt")));
                Assert.Equal(1, w.RemainingSourceFiles);
                Assert.Contains(rec.Logs, l => l == "1 个文件在目标中已存在且相同，Robocopy /MOVE 未移动它们，源文件仍保留。");
            }
        }

        [Fact]
        public async Task CopyMode_NeverDeletesSource()
        {
            using (var work = new TempDir())
            {
                string src = work.Combine("src"); work.WriteFile(@"src\a.txt", "alpha");
                var rec = new Recorder(); var w = rec.Attach(new CopyWorkflow());
                await w.RunAsync(new RunOptions { Source = src, Destination = work.Combine("dst"), Mode = TransferMode.Copy, Verify = true }, CancellationToken.None);
                Assert.True(File.Exists(Path.Combine(src, "a.txt"))); Assert.DoesNotContain(WorkflowStage.DeletingSource, rec.Stages); Assert.Equal(0, w.Progress.DeleteTotal);
            }
        }
    }
}
