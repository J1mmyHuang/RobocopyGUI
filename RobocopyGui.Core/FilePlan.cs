using System;

namespace RobocopyGui.Core
{
    public sealed class FilePlan
    {
        public string Source, Target, RelativePath;
        public long Size, ExistingSize;
        public DateTime ExistingWriteTime, SourceWriteTime;
        public bool NeedsCopy;
    }
}
