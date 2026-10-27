// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

using System;
using System.Threading;
using Xunit;

namespace RobocopyGui.Core.Tests
{
    public class ProgressTextTests
    {
        [Fact]
        public void Copy_WaitsBeforeScanning() { Assert.Equal("等待扫描", ProgressText.Copy(new WorkflowProgress())); }

        [Fact]
        public void Copy_ShowsScannedCountWhileScanning()
        {
            var progress = new WorkflowProgress(); progress.AddScanned(10); progress.AddScanned(20);
            Assert.Equal("扫描到 2 个文件；待复制大小计算中", ProgressText.Copy(progress));
        }

        [Fact]
        public void Copy_ShowsBytesWhileCopying()
        {
            var progress = new WorkflowProgress(); progress.SetCopyTotal(1000); progress.SetCopyDone(684);
            Assert.Equal("684 B / 1000 B  ·  68.4%", ProgressText.Copy(progress));
        }

        [Fact]
        public void Copy_SaysNothingToCopyWhenScanFindsMatchingTargets()
        {
            var stamp = new DateTime(2025, 7, 1, 8, 0, 0, DateTimeKind.Utc);
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                src.WriteFile("a.txt", "same", stamp); dst.WriteFile("a.txt", "same", stamp);
                var progress = new WorkflowProgress(); CopyPlanner.Build(src.Root, dst.Root, progress, CancellationToken.None);
                Assert.Equal("无需复制，文件已匹配", ProgressText.Copy(progress));
                progress.Reset(); Assert.Equal("等待扫描", ProgressText.Copy(progress));
            }
        }

        [Theory]
        [InlineData(true, "等待复制完成")]
        [InlineData(false, "已跳过")]
        public void Hash_BeforeHashingDependsOnVerifyOption(bool verify, string expected) { Assert.Equal(expected, ProgressText.Hash(new WorkflowProgress(), verify)); }

        [Fact]
        public void Hash_ShowsBytesWhileHashing()
        {
            var progress = new WorkflowProgress(); progress.BeginHashing(2048); progress.AddHashed(1024);
            Assert.Equal("1 KB / 2 KB  ·  50.0%", ProgressText.Hash(progress, true));
        }
    }
}
