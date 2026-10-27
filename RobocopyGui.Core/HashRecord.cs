namespace RobocopyGui.Core
{
    public sealed class HashRecord
    {
        public string RelativePath, Result, SourceHash, TargetHash, Detail;
        public string[] ToRow() { return new[] { RelativePath, Result, SourceHash, TargetHash, Detail }; }
    }
}
