using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RobocopyGui.Core
{
    public static class HashReport
    {
        public const string Header = "相对路径,结果,源哈希,目标哈希,说明";

        public static string ToCsvLine(HashRecord row) { return string.Join(",", row.ToRow().Select(v => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"")); }

        public static void Write(string path, IEnumerable<HashRecord> records)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true))) { writer.WriteLine(Header); foreach (var row in records) writer.WriteLine(ToCsvLine(row)); }
        }
    }
}
