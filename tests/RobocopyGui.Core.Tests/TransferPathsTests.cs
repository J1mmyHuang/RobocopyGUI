// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

using System.IO;
using Xunit;

namespace RobocopyGui.Core.Tests
{
    public class TransferPathsTests
    {
        [Fact]
        public void Resolve_PutsFolderIntoSameNamedFolderUnderDestination()
        {
            using (var work = new TempDir())
            {
                Directory.CreateDirectory(work.Combine("照片"));
                var p = TransferPaths.Resolve(work.Combine("照片") + "\\", work.Combine("备份"));
                Assert.Equal(SourceKind.Folder, p.Kind); Assert.Equal(work.Combine("照片"), p.SourceFolder); Assert.Null(p.FileName);
                Assert.Equal(work.Combine(@"备份\照片"), p.TargetFolder); Assert.Equal(work.Combine(@"备份\照片"), p.Result);
            }
        }

        [Fact]
        public void Resolve_CopiesOnlyContentsWhenAskedOrForDriveRoot()
        {
            using (var work = new TempDir())
            {
                Directory.CreateDirectory(work.Combine("src"));
                Assert.Equal(work.Combine("dst"), TransferPaths.Resolve(work.Combine("src"), work.Combine("dst"), false).TargetFolder);
                var root = TransferPaths.Resolve(Path.GetPathRoot(work.Root), work.Combine("dst"));
                Assert.Equal(SourceKind.Folder, root.Kind); Assert.Equal(work.Combine("dst"), root.TargetFolder);
            }
        }

        [Fact]
        public void Resolve_SingleFileReadsParentFolderFilteredToThatFile()
        {
            using (var work = new TempDir())
            {
                string file = work.WriteFile(@"docs\季度 报告.txt", "x");
                var p = TransferPaths.Resolve(file, work.Combine("备份"));
                Assert.Equal(SourceKind.File, p.Kind); Assert.Equal(work.Combine("docs"), p.SourceFolder); Assert.Equal("季度 报告.txt", p.FileName);
                Assert.Equal(work.Combine("备份"), p.TargetFolder); Assert.Equal(work.Combine(@"备份\季度 报告.txt"), p.Result);
            }
        }

        [Fact]
        public void Validate_AcceptsFolderAndFileSources()
        {
            using (var work = new TempDir())
            {
                Directory.CreateDirectory(work.Combine("src")); string file = work.WriteFile(@"docs\a.txt", "a");
                Assert.Null(CopyWorkflow.Validate(new RunOptions { Source = work.Combine("src"), Destination = work.Combine("out") }));
                Assert.Null(CopyWorkflow.Validate(new RunOptions { Source = file, Destination = work.Combine("out"), Mode = TransferMode.Move }));
            }
        }

        [Fact]
        public void Validate_ExplainsEachRejectedCombination()
        {
            using (var work = new TempDir())
            {
                string src = work.Combine("src"); Directory.CreateDirectory(src); string file = work.WriteFile(@"docs\a.txt", "a"); string notFolder = work.WriteFile("note.txt", "n");
                Assert.Equal("源文件夹或文件不存在，或无法访问。", Check(work.Combine("missing"), work.Combine("out")));
                Assert.Equal("请选择目标文件夹。", Check(src, "  "));
                Assert.Equal("目标文件夹路径无效。", Check(src, "D:\\out\0bad"));
                Assert.Equal("目标必须是文件夹，不能是文件。", Check(src, notFolder));
                Assert.Equal("源文件夹和目标文件夹不能相同。", Check(src, src.ToUpperInvariant() + "\\"));
                Assert.Equal("目标文件夹不能位于源文件夹内。", Check(src, work.Combine(@"src\backup")));
                Assert.Equal("源文件夹已经位于该目标文件夹中。", Check(src, work.Root));
                Assert.Equal("源文件已经位于该目标文件夹中。", Check(file, work.Combine("docs")));
                Assert.Equal("不能剪切整个磁盘，请改用复制。", CopyWorkflow.Validate(new RunOptions { Source = Path.GetPathRoot(work.Root), Destination = work.Combine("out"), Mode = TransferMode.Move }));
            }
        }

        static string Check(string source, string destination) { return CopyWorkflow.Validate(new RunOptions { Source = source, Destination = destination }); }

        [Fact]
        public void BuildArguments_SingleFileSkipsSubfoldersAndMovesWithMov()
        {
            var file = TransferPaths.ForFile(@"D:\docs", "a b.txt", @"E:\backup");
            Assert.Equal("\"D:\\docs\" \"E:\\backup\" \"a b.txt\" /Z /FFT /R:2 /W:2 /COPY:DAT /TEE /NP /NDL", RobocopyRunner.BuildArguments(file, false));
            Assert.Equal("\"D:\\docs\" \"E:\\backup\" \"a b.txt\" /Z /FFT /R:2 /W:2 /COPY:DAT /MOV /TEE /NP /NDL", RobocopyRunner.BuildArguments(file, true));
            Assert.Equal("/MOV", RobocopyRunner.MoveSwitch(SourceKind.File)); Assert.Equal("/MOVE", RobocopyRunner.MoveSwitch(SourceKind.Folder));
        }

        [Fact]
        public void BuildArguments_DoublesTrailingBackslashOfDriveRoots()
        {
            // "E:\" would escape the closing quote and robocopy would read the rest of the line as one path.
            Assert.StartsWith("\"D:\\\\\" \"E:\\\\\" \"a.txt\" ", RobocopyRunner.BuildArguments(TransferPaths.ForFile(@"D:\", "a.txt", @"E:\"), false));
            Assert.StartsWith("\"D:\\\\\" \"E:\\backup\" /E ", RobocopyRunner.BuildArguments(@"D:\", @"E:\backup"));
        }
    }
}
