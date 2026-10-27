// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

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
