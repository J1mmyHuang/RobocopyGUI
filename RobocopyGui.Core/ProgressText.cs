// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

using System;

namespace RobocopyGui.Core
{
    public static class ProgressText
    {
        public static string Copy(WorkflowProgress progress)
        {
            long total = progress.CopyTotal, done = progress.CopyDone, files = progress.ScanFiles;
            if (total > 0) return Format.Bytes(Math.Min(done, total)) + " / " + Format.Bytes(total) + "  ·  " + Format.Percent(done, total);
            if (progress.ScanCompleted) return "无需复制，文件已匹配";
            return files > 0 ? "扫描到 " + files + " 个文件；待复制大小计算中" : "等待扫描";
        }

        public static string Hash(WorkflowProgress progress, bool verify)
        {
            long total = progress.HashTotal, done = progress.HashDone;
            return total > 0 ? Format.Bytes(Math.Min(done, total)) + " / " + Format.Bytes(total) + "  ·  " + Format.Percent(done, total) : (verify ? "等待复制完成" : "已跳过");
        }
    }
}
