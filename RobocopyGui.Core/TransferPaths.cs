// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

using System;
using System.IO;

namespace RobocopyGui.Core
{
    public enum SourceKind { Folder, File }

    // Turns the user's choice (a folder or a single file, plus a destination folder) into what Robocopy reads and writes.
    // Like Explorer, a folder lands in a same-named folder under the destination and a file lands directly in it.
    public sealed class TransferPaths
    {
        public SourceKind Kind { get; private set; }
        public string SourceFolder { get; private set; }   // folder Robocopy reads: the chosen folder, or the chosen file's parent
        public string FileName { get; private set; }       // single file only: Robocopy's file filter
        public string Destination { get; private set; }    // destination folder as chosen
        public string TargetFolder { get; private set; }   // folder Robocopy writes into
        public string Result { get; private set; }         // where the chosen folder or file ends up

        public bool IsDriveRoot { get { return Kind == SourceKind.Folder && Path.GetFileName(SourceFolder).Length == 0; } }

        // includeSourceFolder = false keeps Robocopy's own semantics (copy the folder's contents), used by the classic WinForms UI.
        public static TransferPaths Resolve(string source, string destination, bool includeSourceFolder = true)
        {
            string full = Trim(Path.GetFullPath(source)), target = Trim(Path.GetFullPath(destination));
            if (File.Exists(full)) return ForFile(Path.GetDirectoryName(full), Path.GetFileName(full), target);
            string name = Path.GetFileName(full), folder = includeSourceFolder && name.Length > 0 ? Path.Combine(target, name) : target;
            return new TransferPaths { Kind = SourceKind.Folder, SourceFolder = full, Destination = target, TargetFolder = folder, Result = folder };
        }

        public static TransferPaths ForFile(string sourceFolder, string fileName, string destination)
        {
            return new TransferPaths { Kind = SourceKind.File, SourceFolder = sourceFolder, FileName = fileName, Destination = destination, TargetFolder = destination, Result = Path.Combine(destination, fileName) };
        }

        internal static string Trim(string path) { return path.Length > 3 ? path.TrimEnd('\\') : path; }

        internal static bool Same(string a, string b) { return string.Equals(Trim(a), Trim(b), StringComparison.OrdinalIgnoreCase); }

        internal static bool Inside(string path, string folder) { string prefix = folder.EndsWith("\\") ? folder : folder + "\\"; return Trim(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase); }
    }
}
