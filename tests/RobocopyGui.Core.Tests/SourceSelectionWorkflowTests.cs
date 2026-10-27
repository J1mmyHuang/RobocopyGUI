using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RobocopyGui.Core.Tests
{
    // Explorer-like results: a folder lands inside a same-named folder in the destination; a single file is copied or moved on its own.
    public class SourceSelectionWorkflowTests
    {
        static async Task<ConcurrentQueue<string>> Run(RunOptions options)
        {
            var logs = new ConcurrentQueue<string>(); var w = new CopyWorkflow { Log = logs.Enqueue };
            await w.RunAsync(options, CancellationToken.None); return logs;
        }

        [Fact]
        public async Task CopyFolder_CreatesSameNamedFolderInDestination()
        {
            using (var work = new TempDir())
            {
                work.WriteFile(@"照片\a.txt", "alpha"); work.WriteFile(@"照片\2026\b.txt", "beta");
                await Run(new RunOptions { Source = work.Combine("照片"), Destination = work.Combine("备份") });
                Assert.Equal("beta", File.ReadAllText(work.Combine(@"备份\照片\2026\b.txt"))); Assert.False(File.Exists(work.Combine(@"备份\a.txt")));
                Assert.True(File.Exists(work.Combine(@"照片\a.txt")));
            }
        }

        [Fact]
        public async Task LegacyRunAsync_StillCopiesContentsOnly()
        {
            using (var work = new TempDir())
            {
                work.WriteFile(@"src\a.txt", "alpha");
                await new CopyWorkflow().RunAsync(work.Combine("src"), work.Combine("dst"), true, "SHA-256", 4096, CancellationToken.None);
                Assert.True(File.Exists(work.Combine(@"dst\a.txt"))); Assert.False(Directory.Exists(work.Combine(@"dst\src")));
            }
        }

        [Fact]
        public async Task CopyFile_CopiesOnlyThatFile()
        {
            using (var work = new TempDir())
            {
                string file = work.WriteFile(@"docs\a.txt", "alpha"); work.WriteFile(@"docs\b.txt", "beta"); work.WriteFile(@"docs\sub\a.txt", "nested");
                var w = new CopyWorkflow(); await w.RunAsync(new RunOptions { Source = file, Destination = work.Combine("out") }, CancellationToken.None);
                Assert.Equal(new[] { "a.txt" }, Directory.GetFileSystemEntries(work.Combine("out")).Select(Path.GetFileName));
                Assert.Equal("alpha", File.ReadAllText(work.Combine(@"out\a.txt")));
                Assert.Single(w.Records); Assert.Equal("通过", w.Records[0].Result);
            }
        }

        [Fact]
        public async Task MoveFileWithHash_DeletesOnlyThatFile()
        {
            using (var work = new TempDir())
            {
                string file = work.WriteFile(@"docs\a.txt", "alpha"); work.WriteFile(@"docs\b.txt", "beta"); work.WriteFile(@"docs\sub\a.txt", "nested");
                await Run(new RunOptions { Source = file, Destination = work.Combine("out"), Mode = TransferMode.Move, Verify = true });
                Assert.False(File.Exists(file)); Assert.Equal("alpha", File.ReadAllText(work.Combine(@"out\a.txt")));
                Assert.True(File.Exists(work.Combine(@"docs\b.txt"))); Assert.True(File.Exists(work.Combine(@"docs\sub\a.txt")));
            }
        }

        [Fact]
        public async Task MoveFileWithHash_KeepsItsFolderEvenWhenLeftEmpty()
        {
            using (var work = new TempDir())
            {
                string file = work.WriteFile(@"solo\a.txt", "alpha");
                await Run(new RunOptions { Source = file, Destination = work.Combine("out"), Mode = TransferMode.Move, Verify = true });
                Assert.False(File.Exists(file)); Assert.True(Directory.Exists(work.Combine("solo")));
            }
        }

        [Fact]
        public async Task MoveFileWithoutHash_UsesMovAndKeepsTheFolder()
        {
            using (var work = new TempDir())
            {
                string file = work.WriteFile(@"only\a.txt", "alpha"); work.WriteFile(@"only\sub\a.txt", "nested");
                var logs = await Run(new RunOptions { Source = file, Destination = work.Combine("out"), Mode = TransferMode.Move, Verify = false });
                var command = logs.Single(l => l.StartsWith("$ robocopy ")); Assert.Contains(" /MOV ", command); Assert.DoesNotContain("/MOVE", command); Assert.DoesNotContain("/E ", command);
                Assert.False(File.Exists(file)); Assert.True(File.Exists(work.Combine(@"only\sub\a.txt"))); Assert.True(Directory.Exists(work.Combine("only")));
                Assert.False(Directory.Exists(work.Combine(@"out\sub")));
            }
        }

        [Fact]
        public async Task MoveFolderWithHash_MovesTheWholeFolder()
        {
            using (var work = new TempDir())
            {
                work.WriteFile(@"照片\a.txt", "alpha"); work.WriteFile(@"照片\2026\b.txt", "beta");
                await Run(new RunOptions { Source = work.Combine("照片"), Destination = work.Combine("备份"), Mode = TransferMode.Move, Verify = true });
                Assert.False(Directory.Exists(work.Combine("照片"))); Assert.Equal("beta", File.ReadAllText(work.Combine(@"备份\照片\2026\b.txt")));
            }
        }
    }
}
