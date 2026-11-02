using System;
using System.Globalization;
using NotCounters.Core.Config;

namespace NotCounters.Core.Formatting
{
    public static class NumberFormatting
    {
        public const string NoValue = "--";

        public const int MaxDecimals = 6;

        private const string PercentSuffix = " %";

        private static readonly string[] FixedFormats = { "F0", "F1", "F2", "F3", "F4", "F5", "F6" };

        private static readonly string[] PercentFormats = { "P0", "P1", "P2", "P3", "P4", "P5", "P6" };

        public static string Integer(long value, NumberGrouping grouping)
        {
            var digits = value.ToString(CultureInfo.InvariantCulture);
            var separator = GroupSeparator(grouping);
            if (separator == '\0')
            {
                return digits;
            }

            var signLength = value < 0 ? 1 : 0;
            var digitCount = digits.Length - signLength;
            if (digitCount <= 3)
            {
                return digits;
            }

            var separatorCount = (digitCount - 1) / 3;
            var buffer = new char[digits.Length + separatorCount];
            var write = buffer.Length - 1;
            var groupLength = 0;
            for (var read = digits.Length - 1; read >= signLength; read--)
            {
                if (groupLength == 3)
                {
                    buffer[write--] = separator;
                    groupLength = 0;
                }

                buffer[write--] = digits[read];
                groupLength++;
            }

            if (signLength == 1)
            {
                buffer[write] = '-';
            }

            return new string(buffer);
        }

        public static string Fixed(double value, int decimals)
        {
            if (!IsFinite(value))
            {
                return NoValue;
            }

            decimals = ClampDecimals(decimals);
            return RoundForDisplay(value, decimals).ToString(FixedFormats[decimals], CultureInfo.InvariantCulture);
        }

        public static string Percent(double fraction, int decimals)
        {
            if (!IsFinite(fraction))
            {
                return NoValue;
            }

            return Fixed(fraction * 100d, decimals) + PercentSuffix;
        }

        public static string SignedPercentPoints(double points, int decimals)
        {
            if (!IsFinite(points))
            {
                return NoValue;
            }

            return Signed(points, decimals) + PercentSuffix;
        }

        public static string Percent(double fraction, int decimals, IFormatProvider? format)
        {
            if (!IsFinite(fraction))
            {
                return NoValue;
            }

            decimals = ClampDecimals(decimals);
            var rounded = Math.Round(fraction, decimals + 2, MidpointRounding.AwayFromZero);
            if (rounded == 0d)
            {
                rounded = 0d;
            }

            return rounded.ToString(PercentFormats[decimals], format ?? CultureInfo.InvariantCulture);
        }

        public static string Signed(double value, int decimals)
        {
            if (!IsFinite(value))
            {
                return NoValue;
            }

            decimals = ClampDecimals(decimals);
            var rounded = RoundForDisplay(value, decimals);
            var text = rounded.ToString(FixedFormats[decimals], CultureInfo.InvariantCulture);
            return rounded > 0d ? "+" + text : text;
        }

        public static double RoundForDisplay(double value, int decimals)
        {
            if (!IsFinite(value))
            {
                return value;
            }

            var rounded = Math.Round(value, ClampDecimals(decimals), MidpointRounding.AwayFromZero);

            return rounded == 0d ? 0d : rounded;
        }

        public static int ClampDecimals(int decimals) => decimals < 0 ? 0 : decimals > MaxDecimals ? MaxDecimals : decimals;

        internal static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static char GroupSeparator(NumberGrouping grouping)
        {
            switch (grouping)
            {
                case NumberGrouping.Space: return ' ';
                case NumberGrouping.Comma: return ',';
                case NumberGrouping.Dot: return '.';
                default: return '\0';
            }
        }
    }
}
