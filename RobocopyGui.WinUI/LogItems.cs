// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

using System;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using RobocopyGui.Core;

namespace RobocopyGui.WinUI
{
    public sealed class LogLine
    {
        public string Time { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public Brush LevelBrush { get; set; }
        public Brush MessageBrush { get; set; }

        // Core logs plain strings; the level column is derived for display only.
        // Robocopy's tags are localized ("New File" / "新文件"), so match the line shape instead: tag, size, absolute path.
        static readonly Regex RobocopyFileLine = new Regex(@"^\s*\S[^\\:]*?\s[\d.]+(\s[kmgt])?\s+([A-Za-z]:\\|\\\\)");
        static readonly Regex RobocopyErrorLine = new Regex(@"(ERROR|错误)\s+\d+\s+\(0x[0-9A-Fa-f]+\)");

        public static LogLine From(string text)
        {
            bool file = RobocopyFileLine.IsMatch(text);
            string level = file ? "COPY"
                : text.StartsWith("已删除源文件") ? "DEL"
                : RobocopyErrorLine.IsMatch(text) || text.StartsWith("无法") || text.StartsWith("任务失败") || text.Contains("返回错误代码") ? "ERROR"
                : text.Contains("已保留") || text.Contains("任务已停止") || text.Contains("未删除") || text.Contains("未移动") ? "WARN"
                : "INFO";
            // Robocopy pads to ~80 columns with tabs and spaces; the log panel is narrower, so shrink each gap to two spaces.
            string message = Regex.Replace(text.Trim(), @"\t+\s*|\s{2,}", "  ");
            string brush = level == "ERROR" ? "SystemFillColorCriticalBrush" : level == "WARN" ? "SystemFillColorCautionBrush" : level == "COPY" ? "AccentTextFillColorPrimaryBrush" : level == "DEL" ? "SystemFillColorAttentionBrush" : "TextFillColorSecondaryBrush";
            return new LogLine { Time = DateTime.Now.ToString("HH:mm:ss"), Level = level, Message = message, LevelBrush = Theme.Brush(brush), MessageBrush = Theme.Brush(level == "INFO" ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush") };
        }
    }

    public sealed class ExceptionRow
    {
        public string Path { get; set; }
        public string Result { get; set; }
        public string Detail { get; set; }
        public Brush ResultBrush { get; set; }

        public static ExceptionRow From(HashRecord r) { return new ExceptionRow { Path = r.RelativePath, Result = r.Result, Detail = r.Detail, ResultBrush = Theme.Brush(r.Result == "缺失" ? "SystemFillColorCautionBrush" : "SystemFillColorCriticalBrush") }; }
    }

    static class Theme
    {
        public static Brush Brush(string key) { object value; return Application.Current.Resources.TryGetValue(key, out value) ? (Brush)value : null; }
    }
}
