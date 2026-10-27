// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

using Xunit;

namespace RobocopyGui.Core.Tests
{
    public class FormatTests
    {
        [Theory]
        [InlineData(0L, "0 B")]
        [InlineData(1023L, "1023 B")]
        [InlineData(1024L, "1 KB")]
        [InlineData(1536L, "1.5 KB")]
        [InlineData(1048576L, "1 MB")]
        [InlineData(5368709120L, "5 GB")]
        [InlineData(1125899906842624L, "1024 TB")]
        public void Bytes_UsesBinaryUnitsAndTwoDecimals(long bytes, string expected) { Assert.Equal(expected, Format.Bytes(bytes)); }

        [Theory]
        [InlineData(0L, 0L, "0.0%")]
        [InlineData(1L, 3L, "33.3%")]
        [InlineData(684L, 1000L, "68.4%")]
        [InlineData(150L, 100L, "100.0%")]
        [InlineData(-5L, 100L, "0.0%")]
        public void Percent_IsClampedWithOneDecimal(long value, long total, string expected) { Assert.Equal(expected, Format.Percent(value, total)); }
    }
}
