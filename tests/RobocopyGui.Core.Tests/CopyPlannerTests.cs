// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace RobocopyGui.Core.Tests
{
    public class CopyPlannerTests
    {
        static readonly DateTime Stamp = new DateTime(2025, 7, 1, 8, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Build_CreatesDestinationTreeAndPlansNewFiles()
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                src.WriteFile(@"a\b.txt", "abc"); src.WriteFile("c.txt", "hello"); Directory.CreateDirectory(src.Combine("empty"));
                string destination = dst.Combine("out"); var progress = new WorkflowProgress();
                var plan = CopyPlanner.Build(src.Root, destination, progress, CancellationToken.None);
                Assert.True(Directory.Exists(Path.Combine(destination, "a"))); Assert.True(Directory.Exists(Path.Combine(destination, "empty")));
                Assert.Equal(new[] { @"a\b.txt", "c.txt" }, plan.Select(p => p.RelativePath).OrderBy(p => p).ToArray());
                Assert.All(plan, p => { Assert.True(p.NeedsCopy); Assert.Equal(Path.Combine(destination, p.RelativePath), p.Target); });
                Assert.Equal(2, progress.ScanFiles); Assert.Equal(8, progress.ScanBytes); Assert.Equal(8, progress.CopyTotal); Assert.True(progress.ScanCompleted);
            }
        }

        [Theory]
        [InlineData("hello", 0, false)]
        [InlineData("hello", 1, false)]
        [InlineData("hello", 3, true)]
        [InlineData("hello!", 0, true)]
        public void Build_ComparesSizeAndTwoSecondTimestampWindow(string targetContent, int secondsOffset, bool needsCopy)
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                src.WriteFile("file.txt", "hello", Stamp); dst.WriteFile("file.txt", targetContent, Stamp.AddSeconds(secondsOffset));
                var progress = new WorkflowProgress();
                var item = CopyPlanner.Build(src.Root, dst.Root, progress, CancellationToken.None).Single();
                Assert.Equal(needsCopy, item.NeedsCopy); Assert.Equal(targetContent.Length, item.ExistingSize); Assert.Equal(Stamp.AddSeconds(secondsOffset), item.ExistingWriteTime);
                Assert.Equal(needsCopy ? 5 : 0, progress.CopyTotal);
            }
        }

        [Fact]
        public void Build_StopsWhenCancelled()
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                src.WriteFile(@"sub\file.txt", "x");
                var cancelled = new CancellationToken(true); var progress = new WorkflowProgress();
                Assert.Throws<OperationCanceledException>(() => CopyPlanner.Build(src.Root, dst.Root, progress, cancelled));
                Assert.False(progress.ScanCompleted);
            }
        }

        [Theory]
        [InlineData(@"C:\src", @"C:\src\a\b.txt", @"a\b.txt")]
        [InlineData(@"C:\src\", @"C:\src\a\b.txt", @"a\b.txt")]
        [InlineData(@"C:\src", @"C:\src\x", "x")]
        public void Relative_StripsRootAndSeparators(string root, string full, string expected) { Assert.Equal(expected, CopyPlanner.Relative(root, full)); }
    }
}
