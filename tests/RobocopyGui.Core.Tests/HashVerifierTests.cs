using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RobocopyGui.Core.Tests
{
    public class HashVerifierTests
    {
        [Theory]
        [InlineData("SHA-256", "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
        [InlineData("SHA-512", "DDAF35A193617ABACC417349AE20413112E6FA4E89A97EA20A9EEEE64B55D39A2192992A274FC1A836BA3C23A3FEEBBD454D4423643CE80E2A9AC94FA54CA49F")]
        [InlineData("SHA-1", "A9993E364706816ABA3E25717850C26C9CD0D89D")]
        [InlineData("MD5", "900150983CD24FB0D6963F7D28E17F72")]
        [InlineData("unknown", "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
        public void HashFile_MatchesKnownDigestsAndCountsBytes(string algorithm, string expected)
        {
            using (var dir = new TempDir())
            {
                string path = dir.WriteFile("abc.txt", "abc"); var progress = new WorkflowProgress();
                Assert.Equal(expected, HashVerifier.HashFile(path, algorithm, 2, progress, CancellationToken.None));
                Assert.Equal(3, progress.HashDone);
            }
        }

        [Fact]
        public void Algorithms_KeepUiOrder() { Assert.Equal(new[] { "SHA-256", "SHA-512", "SHA-1", "MD5" }, HashVerifier.Algorithms); }

        [Fact]
        public async Task VerifyAllAsync_ReportsPassFailMissingAndError()
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                var plan = new List<FilePlan>
                {
                    new FilePlan { Source = src.WriteFile("ok.txt", "same"), Target = dst.WriteFile("ok.txt", "same"), RelativePath = "ok.txt", Size = 4 },
                    new FilePlan { Source = src.WriteFile("bad.txt", "left"), Target = dst.WriteFile("bad.txt", "right"), RelativePath = "bad.txt", Size = 4 },
                    new FilePlan { Source = src.WriteFile("gone.txt", "data"), Target = dst.Combine("gone.txt"), RelativePath = "gone.txt", Size = 4 },
                    new FilePlan { Source = src.Combine("deleted.txt"), Target = dst.WriteFile("deleted.txt", "zz"), RelativePath = "deleted.txt", Size = 0 },
                };
                var progress = new WorkflowProgress(); var failures = new List<HashRecord>();
                var records = await HashVerifier.VerifyAllAsync(plan, "SHA-256", 4096, progress, failures.Add, CancellationToken.None);
                Assert.Equal(new[] { "通过", "失败", "缺失", "错误" }, records.Select(r => r.Result).ToArray());
                Assert.Equal(new[] { "哈希一致", "哈希不一致", "目标文件不存在" }, records.Take(3).Select(r => r.Detail).ToArray());
                Assert.Null(records[2].TargetHash); Assert.False(string.IsNullOrEmpty(records[3].Detail));
                Assert.Equal(1, progress.HashPassed); Assert.Equal(3, progress.HashFailed);
                Assert.Equal(new[] { "bad.txt", "gone.txt", "deleted.txt" }, failures.Select(r => r.RelativePath).ToArray());
                Assert.Equal(12 + 4 + 5 + 2, progress.HashTotal);
            }
        }

        [Fact]
        public async Task VerifyAllAsync_StopsWhenCancelled()
        {
            using (var dir = new TempDir())
            {
                var plan = new List<FilePlan> { new FilePlan { Source = dir.WriteFile("a.txt", "a"), Target = dir.Combine("b.txt"), RelativePath = "a.txt", Size = 1 } };
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HashVerifier.VerifyAllAsync(plan, "SHA-256", 4096, new WorkflowProgress(), delegate { }, new CancellationToken(true)));
            }
        }
    }
}
