// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

using System.IO;
using System.Text;
using Xunit;

namespace RobocopyGui.Core.Tests
{
    public class HashReportTests
    {
        [Fact]
        public void ToCsvLine_QuotesEveryFieldAndEscapesQuotes()
        {
            var record = new HashRecord { RelativePath = "a\"b.txt", Result = "通过", SourceHash = "AA", TargetHash = null, Detail = "哈希一致" };
            Assert.Equal("\"a\"\"b.txt\",\"通过\",\"AA\",\"\",\"哈希一致\"", HashReport.ToCsvLine(record));
        }

        [Fact]
        public void Write_UsesUtf8BomAndHeader()
        {
            using (var dir = new TempDir())
            {
                string path = dir.Combine("report.csv");
                HashReport.Write(path, new[] { new HashRecord { RelativePath = "x.txt", Result = "失败", SourceHash = "11", TargetHash = "22", Detail = "哈希不一致" } });
                byte[] bytes = File.ReadAllBytes(path);
                Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, new[] { bytes[0], bytes[1], bytes[2] });
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                Assert.Equal(new[] { "相对路径,结果,源哈希,目标哈希,说明", "\"x.txt\",\"失败\",\"11\",\"22\",\"哈希不一致\"" }, lines);
            }
        }
    }
}
