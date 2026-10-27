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

        public static string BuildArguments(string source, string destination, bool move = false) { return "\"" + source + "\" \"" + destination + "\" " + Switches(move) + " /TEE /NP /NDL"; }

        public static string Switches(bool move) { return "/E /Z /FFT /R:2 /W:2 /COPY:DAT /DCOPY:DAT" + (move ? " /MOVE" : ""); }

        public async Task CopyAsync(string source, string destination, IReadOnlyList<FilePlan> plan, WorkflowProgress progress, Action<string> log, CancellationToken token, bool move = false)
        {
            if (progress.CopyTotal == 0) return; var monitorStop = new CancellationTokenSource(); Task monitor = Task.Run(delegate { CopyMonitor.Run(plan, progress, token, monitorStop.Token); });
            Exception runError = null;
            try
            {
                string args = BuildArguments(source, destination, move); log("$ robocopy " + args); int code = await Task.Run(delegate { return Run(args, log, token); }, token); if (code >= 8) runError = new InvalidOperationException("Robocopy 返回错误代码 " + code + "。");
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
