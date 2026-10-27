// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

using System;
using System.IO;

namespace RobocopyGui.Core.Tests
{
    sealed class TempDir : IDisposable
    {
        public TempDir() { Root = Path.Combine(Path.GetTempPath(), "RobocopyGuiTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Root); }

        public string Root { get; }

        public string Combine(string relative) { return Path.Combine(Root, relative); }

        public string WriteFile(string relative, string content, DateTime? lastWriteUtc = null)
        {
            string full = Combine(relative); Directory.CreateDirectory(Path.GetDirectoryName(full)); File.WriteAllText(full, content);
            if (lastWriteUtc.HasValue) File.SetLastWriteTimeUtc(full, lastWriteUtc.Value);
            return full;
        }

        public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
    }
}
