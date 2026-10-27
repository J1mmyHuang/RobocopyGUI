using System;
using System.Globalization;

namespace RobocopyGui.Core
{
    public static class Format
    {
        public static string Percent(long value, long total) { return (total == 0 ? 0 : Math.Min(100, Math.Max(0, value * 100.0 / total))).ToString("0.0", CultureInfo.InvariantCulture) + "%"; }
        public static string Bytes(long bytes) { string[] units = { "B", "KB", "MB", "GB", "TB" }; double value = bytes; int unit = 0; while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; } return value.ToString(unit == 0 ? "0" : "0.##", CultureInfo.InvariantCulture) + " " + units[unit]; }
    }
}
