using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RobocopyGui.Core.Tests
{
    public class RobocopyRunnerTests
    {
        [Fact]
        public void BuildArguments_MatchesShippedSwitches()
        {
            Assert.Equal("\"E:\\src\" \"D:\\dst\" /E /Z /FFT /R:2 /W:2 /COPY:DAT /DCOPY:DAT /TEE /NP /NDL", RobocopyRunner.BuildArguments(@"E:\src", @"D:\dst"));
        }

        [Fact]
        public async Task CopyAsync_CopiesTreeAndMarksCopyComplete()
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                src.WriteFile(@"photos\a.jpg", new string('a', 4000)); src.WriteFile("b.txt", "bee");
                var progress = new WorkflowProgress(); var plan = CopyPlanner.Build(src.Root, dst.Root, progress, CancellationToken.None); var log = new ConcurrentQueue<string>();
                await new RobocopyRunner().CopyAsync(src.Root, dst.Root, plan, progress, log.Enqueue, CancellationToken.None);
                Assert.Equal(new string('a', 4000), File.ReadAllText(dst.Combine(@"photos\a.jpg"))); Assert.Equal("bee", File.ReadAllText(dst.Combine("b.txt")));
                Assert.Equal(progress.CopyTotal, progress.CopyDone);
                Assert.StartsWith("$ robocopy \"" + src.Root + "\"", log.First());
            }
        }

        [Fact]
        public void Run_DecodesNonAsciiFileNamesInOutput()
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                string name = OemRepresentableName(); src.WriteFile(name, "x"); var log = new ConcurrentQueue<string>();
                int code = new RobocopyRunner().Run(RobocopyRunner.BuildArguments(src.Root, dst.Root), log.Enqueue, CancellationToken.None);
                Assert.True(code < 8, "robocopy exit code " + code);
                Assert.Contains(log, l => l.Contains(name));
            }
        }

        [DllImport("kernel32.dll")] static extern int GetOEMCP();

        static string OemRepresentableName()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); var oem = Encoding.GetEncoding(GetOEMCP());
            return new[] { "中文 文件.txt", "Café ñ.txt", "Привет.txt", "Ελληνικά.txt" }.First(n => oem.GetString(oem.GetBytes(n)) == n);
        }

        [Fact]
        public async Task CopyAsync_DoesNothingWhenNothingNeedsCopy()
        {
            var progress = new WorkflowProgress(); var log = new ConcurrentQueue<string>();
            await new RobocopyRunner().CopyAsync(@"C:\does-not-matter", @"C:\nowhere", new FilePlan[0], progress, log.Enqueue, CancellationToken.None);
            Assert.Empty(log); Assert.Equal(0, progress.CopyDone);
        }

        [Fact]
        public async Task CopyAsync_FailsOnFatalExitCode()
        {
            using (var dst = new TempDir())
            {
                var progress = new WorkflowProgress(); progress.SetCopyTotal(1);
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new RobocopyRunner().CopyAsync(dst.Combine("missing-source"), dst.Combine("out"), new FilePlan[0], progress, delegate { }, CancellationToken.None));
                Assert.StartsWith("Robocopy 返回错误代码 ", error.Message); Assert.Equal(0, progress.CopyDone);
            }
        }

        [Fact]
        public async Task CopyAsync_StopsWhenCancelled()
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                src.WriteFile("a.txt", "a"); var progress = new WorkflowProgress(); var plan = CopyPlanner.Build(src.Root, dst.Root, progress, CancellationToken.None);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RobocopyRunner().CopyAsync(src.Root, dst.Root, plan, progress, delegate { }, new CancellationToken(true)));
            }
        }
    }
}
