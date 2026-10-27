using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace RobocopyGui.Core
{
    public static class CopyMonitor
    {
        public static long ConfirmedBytes(IEnumerable<FilePlan> plan)
        {
            long confirmed = 0;
            foreach (var item in plan.Where(p => p.NeedsCopy))
            {
                try
                {
                    if (!File.Exists(item.Target)) continue; var target = new FileInfo(item.Target); bool matches = target.Length == item.Size && Math.Abs((target.LastWriteTimeUtc - File.GetLastWriteTimeUtc(item.Source)).TotalSeconds) <= 2;
                    if (matches) confirmed += item.Size;
                    else if (item.ExistingSize != target.Length || item.ExistingWriteTime != target.LastWriteTimeUtc) confirmed += Math.Min(item.Size, target.Length);
                }
                catch { }
            }
            return confirmed;
        }

        public static void Run(IReadOnlyList<FilePlan> plan, WorkflowProgress progress, CancellationToken workflowToken, CancellationToken monitorToken)
        {
            while (!monitorToken.IsCancellationRequested)
            {
                workflowToken.ThrowIfCancellationRequested(); long confirmed = ConfirmedBytes(plan);
                progress.SetCopyDone(Math.Min(progress.CopyTotal, confirmed)); Thread.Sleep(350);
            }
        }
    }
}
