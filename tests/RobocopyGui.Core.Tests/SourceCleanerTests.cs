// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace RobocopyGui.Core.Tests
{
    public class SourceCleanerTests
    {
        static readonly DateTime Stamp = new DateTime(2025, 7, 1, 8, 0, 0, DateTimeKind.Utc);

        // Builds a source tree, mirrors it to the destination with identical size/time, and returns the plan plus "通过" records for every file.
        static (List<FilePlan> plan, List<HashRecord> records) Mirror(TempDir src, TempDir dst, params string[] files)
        {
            foreach (var f in files) { src.WriteFile(f, "content of " + f, Stamp); dst.WriteFile(f, "content of " + f, Stamp); }
            var plan = CopyPlanner.Build(src.Root, dst.Root, new WorkflowProgress(), CancellationToken.None);
            return (plan, plan.Select(p => new HashRecord { RelativePath = p.RelativePath, Result = "通过", Detail = "哈希一致" }).ToList());
        }

        [Fact]
        public void DeleteVerified_RemovesVerifiedFilesAndEmptyFoldersIncludingRoot()
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                string root = src.Combine("photos"); Directory.CreateDirectory(Path.Combine(root, "empty"));
                var sub = new TempDirView(root);
                foreach (var f in new[] { "a.txt", @"2025\b.txt", @"2025\raw\c.txt" }) { sub.Write(f); dst.WriteFile(f, "content of " + f, Stamp); }
                var progress = new WorkflowProgress(); var plan = CopyPlanner.Build(root, dst.Root, progress, CancellationToken.None);
                var records = plan.Select(p => new HashRecord { RelativePath = p.RelativePath, Result = "通过" }).ToList(); var log = new ConcurrentQueue<string>();
                SourceCleaner.DeleteVerified(root, plan, records, progress, log.Enqueue, CancellationToken.None);
                Assert.False(Directory.Exists(root));
                Assert.Equal(3, progress.DeleteTotal); Assert.Equal(3, progress.DeleteDone); Assert.Equal(0, progress.DeleteKept);
                Assert.True(File.Exists(dst.Combine(@"2025\raw\c.txt")));
                Assert.Equal("已删除 3 个源文件和 4 个空文件夹。", log.Last());
            }
        }

        [Fact]
        public void DeleteVerified_KeepsFilesThatDidNotPassVerification()
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                var (plan, records) = Mirror(src, dst, "ok.txt", @"bad\x.txt");
                records.Single(r => r.RelativePath == @"bad\x.txt").Result = "失败";
                var progress = new WorkflowProgress();
                SourceCleaner.DeleteVerified(src.Root, plan, records, progress, delegate { }, CancellationToken.None);
                Assert.False(File.Exists(src.Combine("ok.txt"))); Assert.True(File.Exists(src.Combine(@"bad\x.txt"))); Assert.True(Directory.Exists(src.Root));
                Assert.Equal(1, progress.DeleteTotal); Assert.Equal(1, progress.DeleteDone);
            }
        }

        [Fact]
        public void DeleteVerified_KeepsSourceChangedAfterScanAndSourceWhoseTargetVanished()
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                var (plan, records) = Mirror(src, dst, "changed.txt", "orphan.txt", "fine.txt");
                File.SetLastWriteTimeUtc(src.Combine("changed.txt"), Stamp.AddMinutes(1)); File.Delete(dst.Combine("orphan.txt"));
                var progress = new WorkflowProgress(); var log = new ConcurrentQueue<string>();
                SourceCleaner.DeleteVerified(src.Root, plan, records, progress, log.Enqueue, CancellationToken.None);
                Assert.True(File.Exists(src.Combine("changed.txt"))); Assert.True(File.Exists(src.Combine("orphan.txt"))); Assert.False(File.Exists(src.Combine("fine.txt")));
                Assert.Equal(1, progress.DeleteDone); Assert.Equal(2, progress.DeleteKept);
                Assert.Contains(log, l => l == "源文件在扫描后发生变化，已保留：changed.txt"); Assert.Contains(log, l => l == "目标文件已变化，源文件已保留：orphan.txt");
            }
        }

        [Fact]
        public void DeleteVerified_LeavesFilesAddedAfterScanAndTheirFolders()
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                var (plan, records) = Mirror(src, dst, @"sub\a.txt");
                src.WriteFile(@"sub\late.txt", "arrived later");
                SourceCleaner.DeleteVerified(src.Root, plan, records, new WorkflowProgress(), delegate { }, CancellationToken.None);
                Assert.False(File.Exists(src.Combine(@"sub\a.txt"))); Assert.True(File.Exists(src.Combine(@"sub\late.txt")));
            }
        }

        [Fact]
        public void DeleteVerified_ClearsReadOnlyBeforeDeleting()
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                var (plan, records) = Mirror(src, dst, "locked.txt");
                File.SetAttributes(src.Combine("locked.txt"), FileAttributes.ReadOnly); File.SetLastWriteTimeUtc(src.Combine("locked.txt"), Stamp);
                var progress = new WorkflowProgress();
                SourceCleaner.DeleteVerified(src.Root, plan, records, progress, delegate { }, CancellationToken.None);
                Assert.False(File.Exists(src.Combine("locked.txt"))); Assert.Equal(1, progress.DeleteDone);
            }
        }

        [Fact]
        public void DeleteVerified_StopsWhenCancelled()
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                var (plan, records) = Mirror(src, dst, "a.txt");
                Assert.Throws<OperationCanceledException>(() => SourceCleaner.DeleteVerified(src.Root, plan, records, new WorkflowProgress(), delegate { }, new CancellationToken(true)));
                Assert.True(File.Exists(src.Combine("a.txt")));
            }
        }

        sealed class TempDirView
        {
            readonly string root;
            public TempDirView(string root) { this.root = root; }
            public void Write(string relative) { string full = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(full)); File.WriteAllText(full, "content of " + relative); File.SetLastWriteTimeUtc(full, Stamp); }
        }
    }
}
