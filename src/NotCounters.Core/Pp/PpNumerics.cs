using System;
using System.Collections.Generic;

namespace NotCounters.Core.Pp
{
    internal static class PpNumerics
    {
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static double FiniteOr(double value, double fallback) => IsFinite(value) ? value : fallback;

        public static double FiniteOr(double? value, double fallback) =>
            value.HasValue && IsFinite(value.Value) ? value.Value : fallback;

        public static double RoundHalfUp(double value, int digits)
        {
            if (!IsFinite(value) || Math.Abs(value) > 1e14) return value;
            try
            {
                return (double)Math.Round((decimal)value, digits, MidpointRounding.AwayFromZero);
            }
            catch (OverflowException)
            {
                return value;
            }
        }

        public static double Median(List<double> values)
        {
            if (values.Count == 0) return double.NaN;
            values.Sort();
            int mid = values.Count / 2;
            return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2.0;
        }
    }
}
