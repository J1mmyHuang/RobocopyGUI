// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RobocopyGui.Core
{
    public sealed class RobocopyRunner
    {
        Process activeProcess;

        static RobocopyRunner() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }

        // robocopy writes redirected output in the system OEM code page (e.g. 936 on Chinese Windows), not UTF-8.
        public static Encoding OutputEncoding { get { return Encoding.GetEncoding(GetOEMCP()); } }

        [DllImport("kernel32.dll")] static extern int GetOEMCP();

        public static string BuildArguments(string source, string destination, bool move = false) { return Quote(source) + " " + Quote(destination) + " " + Switches(move) + " /TEE /NP /NDL"; }

        public static string BuildArguments(TransferPaths paths, bool move)
        {
            if (paths.Kind == SourceKind.Folder) return BuildArguments(paths.SourceFolder, paths.TargetFolder, move);
            return Quote(paths.SourceFolder) + " " + Quote(paths.TargetFolder) + " " + Quote(paths.FileName) + " " + Switches(SourceKind.File, move) + " /TEE /NP /NDL";
        }

        public static string Switches(bool move) { return Switches(SourceKind.Folder, move); }

        // A single file: no /E (the file filter would also match same-named files in subfolders), no /DCOPY (no folder is being copied),
        // and /MOV, which unlike /MOVE never removes the source folder.
        public static string Switches(SourceKind kind, bool move)
        {
            return kind == SourceKind.File ? "/Z /FFT /R:2 /W:2 /COPY:DAT" + (move ? " /MOV" : "") : "/E /Z /FFT /R:2 /W:2 /COPY:DAT /DCOPY:DAT" + (move ? " /MOVE" : "");
        }

        public static string MoveSwitch(SourceKind kind) { return kind == SourceKind.File ? "/MOV" : "/MOVE"; }

        // A trailing backslash before the closing quote ("E:\") escapes the quote and robocopy reads the rest of the line as the path.
        static string Quote(string path) { return "\"" + (path.EndsWith("\\") ? path + "\\" : path) + "\""; }

        public Task CopyAsync(string source, string destination, IReadOnlyList<FilePlan> plan, WorkflowProgress progress, Action<string> log, CancellationToken token, bool move = false)
        {
            return CopyWithArgumentsAsync(BuildArguments(source, destination, move), plan, progress, log, token);
        }

        public Task CopyAsync(TransferPaths paths, IReadOnlyList<FilePlan> plan, WorkflowProgress progress, Action<string> log, CancellationToken token, bool move = false)
        {
            return CopyWithArgumentsAsync(BuildArguments(paths, move), plan, progress, log, token);
        }

        async Task CopyWithArgumentsAsync(string args, IReadOnlyList<FilePlan> plan, WorkflowProgress progress, Action<string> log, CancellationToken token)
        {
            if (progress.CopyTotal == 0) return; var monitorStop = new CancellationTokenSource(); Task monitor = Task.Run(delegate { CopyMonitor.Run(plan, progress, token, monitorStop.Token); });
            Exception runError = null;
            try
            {
                log("$ robocopy " + args); int code = await Task.Run(delegate { return Run(args, log, token); }, token); if (code >= 8) runError = new InvalidOperationException("Robocopy 返回错误代码 " + code + "。");
            }
            catch (Exception ex) { runError = ex; }
            monitorStop.Cancel(); try { await monitor; } catch (OperationCanceledException) { } monitorStop.Dispose();
            if (runError != null) throw runError; progress.SetCopyDone(progress.CopyTotal);
        }

        public int Run(string args, Action<string> log, CancellationToken token)
        {
            using (var process = new Process { StartInfo = new ProcessStartInfo("robocopy.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = OutputEncoding } })
            {
                activeProcess = process; process.Start(); string line; while ((line = process.StandardOutput.ReadLine()) != null) { if (!string.IsNullOrWhiteSpace(line)) log(line); token.ThrowIfCancellationRequested(); } process.WaitForExit(); activeProcess = null; token.ThrowIfCancellationRequested(); return process.ExitCode;
            }
        }

        public void Kill() { var process = activeProcess; if (process != null) try { process.Kill(); } catch { } }
        public void Release() { if (activeProcess != null) { activeProcess.Dispose(); activeProcess = null; } }
    }
}
