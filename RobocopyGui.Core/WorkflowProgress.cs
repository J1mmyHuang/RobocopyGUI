using System.Threading;

namespace RobocopyGui.Core
{
    public sealed class WorkflowProgress
    {
        long copyDone, copyTotal, hashDone, hashTotal, scanFiles, scanBytes;
        int hashPassed, hashFailed, scanCompleted, deleteTotal, deleteDone, deleteKept;

        public long CopyDone { get { return Interlocked.Read(ref copyDone); } }
        public long CopyTotal { get { return Interlocked.Read(ref copyTotal); } }
        public long HashDone { get { return Interlocked.Read(ref hashDone); } }
        public long HashTotal { get { return Interlocked.Read(ref hashTotal); } }
        public long ScanFiles { get { return Interlocked.Read(ref scanFiles); } }
        public long ScanBytes { get { return Interlocked.Read(ref scanBytes); } }
        public int HashPassed { get { return Volatile.Read(ref hashPassed); } }
        public int HashFailed { get { return Volatile.Read(ref hashFailed); } }
        public bool ScanCompleted { get { return Volatile.Read(ref scanCompleted) == 1; } }
        public int DeleteTotal { get { return Volatile.Read(ref deleteTotal); } }
        public int DeleteDone { get { return Volatile.Read(ref deleteDone); } }
        public int DeleteKept { get { return Volatile.Read(ref deleteKept); } }

        public void Reset() { copyDone = copyTotal = hashDone = hashTotal = scanFiles = scanBytes = 0; hashPassed = hashFailed = scanCompleted = deleteTotal = deleteDone = deleteKept = 0; }
        public void BeginDeleting(int files) { Interlocked.Exchange(ref deleteTotal, files); Interlocked.Exchange(ref deleteDone, 0); Interlocked.Exchange(ref deleteKept, 0); }
        public void CountDeleted() { Interlocked.Increment(ref deleteDone); }
        public void CountKept() { Interlocked.Increment(ref deleteKept); }
        public void CompleteScan() { Volatile.Write(ref scanCompleted, 1); }
        public void AddScanned(long bytes) { Interlocked.Increment(ref scanFiles); Interlocked.Add(ref scanBytes, bytes); }
        public void SetCopyTotal(long bytes) { Interlocked.Exchange(ref copyTotal, bytes); }
        public void SetCopyDone(long bytes) { Interlocked.Exchange(ref copyDone, bytes); }
        public void BeginHashing(long totalBytes) { Interlocked.Exchange(ref hashTotal, totalBytes); Interlocked.Exchange(ref hashDone, 0); }
        public void AddHashed(long bytes) { Interlocked.Add(ref hashDone, bytes); }
        public void CountPassed() { Interlocked.Increment(ref hashPassed); }
        public void CountFailed() { Interlocked.Increment(ref hashFailed); }
    }
}
