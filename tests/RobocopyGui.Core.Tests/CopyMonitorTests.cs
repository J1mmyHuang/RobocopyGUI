using System;
using System.IO;
using Xunit;

namespace RobocopyGui.Core.Tests
{
    public class CopyMonitorTests
    {
        static readonly DateTime Stamp = new DateTime(2025, 7, 1, 8, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void ConfirmedBytes_CountsOnlyBytesConfirmedOnTarget()
        {
            using (var src = new TempDir()) using (var dst = new TempDir())
            {
                var done = new FilePlan { Source = src.WriteFile("done.bin", "0123456789", Stamp), Target = dst.WriteFile("done.bin", "0123456789", Stamp), Size = 10, NeedsCopy = true };
                var partial = new FilePlan { Source = src.WriteFile("partial.bin", "0123456789", Stamp), Target = dst.WriteFile("partial.bin", "0123", Stamp.AddMinutes(5)), Size = 10, NeedsCopy = true };
                var missing = new FilePlan { Source = src.WriteFile("missing.bin", "0123456789", Stamp), Target = dst.Combine("missing.bin"), Size = 10, NeedsCopy = true };
                var skipped = new FilePlan { Source = src.WriteFile("skip.bin", "0123456789", Stamp), Target = dst.WriteFile("skip.bin", "0123456789", Stamp), Size = 10, NeedsCopy = false };
                string staleTarget = dst.WriteFile("stale.bin", "old", Stamp.AddDays(-1));
                var stale = new FilePlan { Source = src.WriteFile("stale.bin", "0123456789", Stamp), Target = staleTarget, Size = 10, NeedsCopy = true, ExistingSize = 3, ExistingWriteTime = File.GetLastWriteTimeUtc(staleTarget) };
                Assert.Equal(10 + 4, CopyMonitor.ConfirmedBytes(new[] { done, partial, missing, skipped, stale }));
            }
        }
    }
}
