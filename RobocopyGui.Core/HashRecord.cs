// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

namespace RobocopyGui.Core
{
    public sealed class HashRecord
    {
        public string RelativePath, Result, SourceHash, TargetHash, Detail;
        public string[] ToRow() { return new[] { RelativePath, Result, SourceHash, TargetHash, Detail }; }
    }
}
