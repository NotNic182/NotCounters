using System;

namespace NotCounters.Core.Leaderboards
{
    internal static class ApiUrl
    {
        public const int MaxSegmentLength = 256;

        public static string NormalizeBase(string? baseUrl, string fallback)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return fallback;
            string trimmed = baseUrl!.Trim().TrimEnd('/');
            return Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
                   && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? trimmed
                : fallback;
        }

        public static string? Segment(string? value)
        {
            if (string.IsNullOrEmpty(value) || value!.Length > MaxSegmentLength) return null;
            try
            {
                return Uri.EscapeDataString(value);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string? Join(params string?[] parts)
        {
            foreach (string? p in parts)
            {
                if (p == null) return null;
            }

            return string.Concat(parts);
        }
    }
}
