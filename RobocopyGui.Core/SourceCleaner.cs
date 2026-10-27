using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace RobocopyGui.Core
{
    // Deletes source files only after their hashes matched the destination, then removes source folders left empty (as robocopy /MOVE does).
    public static class SourceCleaner
    {
        public static void DeleteVerified(string sourceRoot, IReadOnlyList<FilePlan> plan, IReadOnlyList<HashRecord> records, WorkflowProgress progress, Action<string> log, CancellationToken token)
        {
            string root = Path.GetFullPath(sourceRoot); if (root.Length > 3) root = root.TrimEnd('\\'); string prefix = root.EndsWith("\\") ? root : root + "\\";
            var verified = new HashSet<string>(records.Where(r => r.Result == "通过").Select(r => r.RelativePath), StringComparer.OrdinalIgnoreCase);
            var files = plan.Where(p => verified.Contains(p.RelativePath)).ToList();
            progress.BeginDeleting(files.Count); log("开始删除源文件（只删除校验一致的文件）。");
            foreach (var item in files)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var source = new FileInfo(item.Source); var target = new FileInfo(item.Target);
                    if (!Path.GetFullPath(item.Source).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { progress.CountKept(); log("源文件不在源文件夹内，已保留：" + item.RelativePath); continue; }
                    if (!source.Exists || source.Length != item.Size || source.LastWriteTimeUtc != item.SourceWriteTime) { progress.CountKept(); log("源文件在扫描后发生变化，已保留：" + item.RelativePath); continue; }
                    if (!target.Exists || target.Length != item.Size) { progress.CountKept(); log("目标文件已变化，源文件已保留：" + item.RelativePath); continue; }
                    if ((source.Attributes & FileAttributes.ReadOnly) != 0) source.Attributes &= ~FileAttributes.ReadOnly;
                    source.Delete(); progress.CountDeleted(); log("已删除源文件 " + item.RelativePath);
                }
                catch (Exception ex) { progress.CountKept(); log("无法删除源文件 " + item.RelativePath + "：" + ex.Message); }
            }
            int folders = RemoveEmptyFolders(root, token); log("已删除 " + progress.DeleteDone + " 个源文件和 " + folders + " 个空文件夹" + (progress.DeleteKept > 0 ? "，保留 " + progress.DeleteKept + " 个源文件。" : "。"));
        }

        public static int RemainingSources(IEnumerable<FilePlan> plan) { return plan.Count(p => File.Exists(p.Source)); }

        static int RemoveEmptyFolders(string root, CancellationToken token)
        {
            int removed = 0; if (!Directory.Exists(root)) return 0;
            foreach (string dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
            {
                token.ThrowIfCancellationRequested();
                try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) { Directory.Delete(dir, false); removed++; } } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            try { if (!Directory.EnumerateFileSystemEntries(root).Any()) { Directory.Delete(root, false); removed++; } } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return removed;
        }
    }
}
