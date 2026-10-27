using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace RobocopyGui.Core
{
    public static class CopyPlanner
    {
        public static List<FilePlan> Build(string source, string destination, WorkflowProgress progress, CancellationToken token)
        {
            var plan = new List<FilePlan>();
            Directory.CreateDirectory(destination); foreach (string dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) { token.ThrowIfCancellationRequested(); Directory.CreateDirectory(Path.Combine(destination, Relative(source, dir))); }
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested(); var info = new FileInfo(file); string relative = Relative(source, file), target = Path.Combine(destination, relative); var item = new FilePlan { Source = file, Target = target, RelativePath = relative, Size = info.Length, NeedsCopy = true };
                if (File.Exists(target)) { var existing = new FileInfo(target); item.ExistingSize = existing.Length; item.ExistingWriteTime = existing.LastWriteTimeUtc; item.NeedsCopy = existing.Length != info.Length || Math.Abs((existing.LastWriteTimeUtc - info.LastWriteTimeUtc).TotalSeconds) > 2; }
                plan.Add(item); progress.AddScanned(item.Size);
            }
            progress.SetCopyTotal(plan.Where(p => p.NeedsCopy).Sum(p => p.Size)); progress.CompleteScan();
            return plan;
        }

        public static string Relative(string root, string full) { return full.Substring(root.TrimEnd('\\').Length).TrimStart('\\'); }
    }
}
