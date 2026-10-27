// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace RobocopyGui.Core
{
    public static class CopyPlanner
    {
        public static List<FilePlan> Build(TransferPaths paths, WorkflowProgress progress, CancellationToken token)
        {
            if (paths.Kind == SourceKind.Folder) return Build(paths.SourceFolder, paths.TargetFolder, progress, token);
            token.ThrowIfCancellationRequested(); Directory.CreateDirectory(paths.TargetFolder);
            var plan = new List<FilePlan> { Plan(Path.Combine(paths.SourceFolder, paths.FileName), paths.Result, paths.FileName) }; progress.AddScanned(plan[0].Size);
            return Finish(plan, progress);
        }

        public static List<FilePlan> Build(string source, string destination, WorkflowProgress progress, CancellationToken token)
        {
            var plan = new List<FilePlan>();
            Directory.CreateDirectory(destination); foreach (string dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) { token.ThrowIfCancellationRequested(); Directory.CreateDirectory(Path.Combine(destination, Relative(source, dir))); }
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested(); string relative = Relative(source, file); var item = Plan(file, Path.Combine(destination, relative), relative);
                plan.Add(item); progress.AddScanned(item.Size);
            }
            return Finish(plan, progress);
        }

        static FilePlan Plan(string source, string target, string relative)
        {
            var info = new FileInfo(source); var item = new FilePlan { Source = source, Target = target, RelativePath = relative, Size = info.Length, SourceWriteTime = info.LastWriteTimeUtc, NeedsCopy = true };
            if (File.Exists(target)) { var existing = new FileInfo(target); item.ExistingSize = existing.Length; item.ExistingWriteTime = existing.LastWriteTimeUtc; item.NeedsCopy = existing.Length != info.Length || Math.Abs((existing.LastWriteTimeUtc - info.LastWriteTimeUtc).TotalSeconds) > 2; }
            return item;
        }

        static List<FilePlan> Finish(List<FilePlan> plan, WorkflowProgress progress) { progress.SetCopyTotal(plan.Where(p => p.NeedsCopy).Sum(p => p.Size)); progress.CompleteScan(); return plan; }

        public static string Relative(string root, string full) { return full.Substring(root.TrimEnd('\\').Length).TrimStart('\\'); }
    }
}
