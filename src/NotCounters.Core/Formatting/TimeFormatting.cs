using System;
using System.Globalization;

namespace NotCounters.Core.Formatting
{
    public static class TimeFormatting
    {
        public const string NoTime = "--:--";

        private const double MaxSeconds = 1e9;

        public static string MinutesSeconds(double seconds)
        {
            if (!NumberFormatting.IsFinite(seconds) || Math.Abs(seconds) > MaxSeconds)
            {
                return NoTime;
            }

            var totalSeconds = (long)Math.Floor(Math.Abs(seconds));
            var minutes = totalSeconds / 60;
            var remainder = totalSeconds % 60;
            var text = minutes.ToString(CultureInfo.InvariantCulture) + ":" + remainder.ToString("00", CultureInfo.InvariantCulture);
            return seconds < 0 && totalSeconds > 0 ? "-" + text : text;
        }

        public static string Clock(DateTime t, bool use24h, bool seconds)
        {
            string format;
            if (use24h)
            {
                format = seconds ? "HH:mm:ss" : "HH:mm";
            }
            else
            {
                format = seconds ? "h:mm:ss tt" : "h:mm tt";
            }

            return t.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
