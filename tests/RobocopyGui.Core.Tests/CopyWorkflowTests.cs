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
    public class CopyWorkflowTests
    {
        sealed class Recorder
        {
            public readonly List<int> Steps = new List<int>();
            public readonly List<string> Statuses = new List<string>();
            public readonly ConcurrentQueue<string> Logs = new ConcurrentQueue<string>();
            public readonly ConcurrentQueue<HashRecord> Failures = new ConcurrentQueue<HashRecord>();
            public CopyWorkflow Attach(CopyWorkflow workflow) { workflow.StepChanged = Steps.Add; workflow.StatusChanged = Statuses.Add; workflow.Log = Logs.Enqueue; workflow.Failure = Failures.Enqueue; return workflow; }
        }

        [Fact]
        public void ValidatePaths_MatchesOriginalMessages()
        {
            using (var src = new TempDir())
            {
                Assert.Equal("源文件夹不存在或不可访问。", CopyWorkflow.ValidatePaths(src.Combine("missing"), @"D:\out"));
                Assert.Equal("源文件夹和目标文件夹不能相同。", CopyWorkflow.ValidatePaths(src.Root, src.Root.ToUpperInvariant() + "\\"));
                Assert.Null(CopyWorkflow.ValidatePaths(src.Root, src.Combine("out")));
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void ValidatePaths_AsksForDestinationWhenEmpty(string destination)
        {
            using (var src = new TempDir()) Assert.Equal("请选择目标文件夹。", CopyWorkflow.ValidatePaths(src.Root, destination));
        }

        [Fact]
        public void ValidatePaths_RejectsMalformedDestination()
        {
            using (var src = new TempDir()) Assert.Equal("目标文件夹路径无效。", CopyWorkflow.ValidatePaths(src.Root, "D:\\out\0bad"));
        }

        [Fact]
        public void ValidatePaths_ChecksSourceBeforeDestination()
        {
            using (var src = new TempDir()) Assert.Equal("源文件夹不存在或不可访问。", CopyWorkflow.ValidatePaths(src.Combine("missing"), ""));
        }

        [Fact]
        public async Task RunAsync_ScansCopiesVerifiesAndReportsInOrder()
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                src.WriteFile(@"2025\a.txt", "alpha"); src.WriteFile("b.txt", "beta");
                var rec = new Recorder(); var workflow = rec.Attach(new CopyWorkflow());
                await workflow.RunAsync(src.Root, dst.Root, true, "SHA-256", 4 * 1024 * 1024, CancellationToken.None);
                Assert.Equal(new[] { 1, 2, 3, 4 }, rec.Steps);
                Assert.Equal(new[] { "正在扫描源文件夹…", "Robocopy 正在复制…", "正在计算哈希…", "任务完成。" }, rec.Statuses);
                Assert.Equal("扫描完成：2 个文件，9 B。待复制 9 B。", rec.Logs.First());
                Assert.Contains(rec.Logs, l => l.StartsWith("$ robocopy "));
                Assert.Equal("完成。通过 2，异常 0。", rec.Logs.Last());
                Assert.Equal(2, workflow.Records.Count); Assert.All(workflow.Records, r => Assert.Equal("通过", r.Result)); Assert.Empty(rec.Failures);
                Assert.Equal("alpha", File.ReadAllText(dst.Combine(@"2025\a.txt")));
            }
        }

        [Fact]
        public async Task RunAsync_SkipsHashingWhenDisabled()
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                src.WriteFile("a.txt", "alpha");
                var rec = new Recorder(); var workflow = rec.Attach(new CopyWorkflow());
                await workflow.RunAsync(src.Root, dst.Root, false, "SHA-256", 4 * 1024 * 1024, CancellationToken.None);
                Assert.Equal(new[] { 1, 2, 4 }, rec.Steps);
                Assert.Equal(new[] { "正在扫描源文件夹…", "Robocopy 正在复制…", "已跳过哈希校验。", "任务完成。" }, rec.Statuses);
                Assert.Empty(workflow.Records); Assert.Equal("完成。通过 0，异常 0。", rec.Logs.Last());
            }
        }

        [Fact]
        public async Task RunAsync_SecondRunSkipsRobocopyWhenTargetsMatch()
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                src.WriteFile("a.txt", "alpha");
                var workflow = new CopyWorkflow();
                await workflow.RunAsync(src.Root, dst.Root, true, "SHA-256", 4 * 1024 * 1024, CancellationToken.None);
                var rec = new Recorder(); rec.Attach(workflow); workflow.Reset();
                Assert.Empty(workflow.Records); Assert.Equal(0, workflow.Progress.ScanFiles); Assert.False(workflow.Progress.ScanCompleted);
                await workflow.RunAsync(src.Root, dst.Root, true, "SHA-256", 4 * 1024 * 1024, CancellationToken.None);
                Assert.Equal("无需复制，文件已匹配。", rec.Statuses[1]);
                Assert.Equal("无需复制，文件已匹配", ProgressText.Copy(workflow.Progress));
                Assert.DoesNotContain(rec.Logs, l => l.StartsWith("$ robocopy "));
                Assert.Equal("完成。通过 1，异常 0。", rec.Logs.Last());
            }
        }

        [Fact]
        public async Task RunAsync_StopsWhenCancelled()
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                src.WriteFile("a.txt", "alpha");
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CopyWorkflow().RunAsync(src.Root, dst.Root, true, "SHA-256", 4096, new CancellationToken(true)));
            }
        }
    }
}
