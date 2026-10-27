using System.Threading;

namespace RobocopyGui.Core
{
    public sealed class WorkflowProgress
    {
        long copyDone, copyTotal, hashDone, hashTotal, scanFiles, scanBytes;
        int hashPassed, hashFailed;

        public long CopyDone { get { return Interlocked.Read(ref copyDone); } }
        public long CopyTotal { get { return Interlocked.Read(ref copyTotal); } }
        public long HashDone { get { return Interlocked.Read(ref hashDone); } }
        public long HashTotal { get { return Interlocked.Read(ref hashTotal); } }
        public long ScanFiles { get { return Interlocked.Read(ref scanFiles); } }
        public long ScanBytes { get { return Interlocked.Read(ref scanBytes); } }
        public int HashPassed { get { return Volatile.Read(ref hashPassed); } }
        public int HashFailed { get { return Volatile.Read(ref hashFailed); } }

        public void Reset() { copyDone = copyTotal = hashDone = hashTotal = scanFiles = scanBytes = 0; hashPassed = hashFailed = 0; }
        public void AddScanned(long bytes) { Interlocked.Increment(ref scanFiles); Interlocked.Add(ref scanBytes, bytes); }
        public void SetCopyTotal(long bytes) { Interlocked.Exchange(ref copyTotal, bytes); }
        public void SetCopyDone(long bytes) { Interlocked.Exchange(ref copyDone, bytes); }
        public void BeginHashing(long totalBytes) { Interlocked.Exchange(ref hashTotal, totalBytes); Interlocked.Exchange(ref hashDone, 0); }
        public void AddHashed(long bytes) { Interlocked.Add(ref hashDone, bytes); }
        public void CountPassed() { Interlocked.Increment(ref hashPassed); }
        public void CountFailed() { Interlocked.Increment(ref hashFailed); }
    }
}
