using System;

namespace NotCounters.Core.Leaderboards
{
    public enum PlayerOverrideKind
    {
        Automatic,
        Query,
        Unsupported,
    }

    public sealed class PlayerOverride
    {
        private const int MaxTextLength = 2048;

        private static readonly string[] BeatLeaderHosts = { "beatleader.com", "beatleader.xyz" };

        private PlayerOverride(PlayerOverrideKind kind, string query, bool fromLink)
        {
            Kind = kind;
            Query = query;
            FromLink = fromLink;
            IsNumeric = kind == PlayerOverrideKind.Query && IsAllDigits(query);
        }

        public static PlayerOverride Automatic { get; } = new PlayerOverride(PlayerOverrideKind.Automatic, string.Empty, false);

        public PlayerOverrideKind Kind { get; }

        public string Query { get; }

        public bool IsNumeric { get; }

        public bool FromLink { get; }

        public static PlayerOverride Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Automatic;
            if (text!.Length > MaxTextLength) return Unsupported();

            string value = StripQuotes(text.Trim());
            if (value.Length == 0) return Automatic;

            bool fromLink = false;
            if (LooksLikeLink(value))
            {
                string? fromUrl = ValueFromProfileLink(value);
                if (fromUrl == null) return Unsupported();
                value = fromUrl;
                fromLink = true;
            }

            if (value.Length == 0 || value.Length > ApiUrl.MaxSegmentLength) return Unsupported();
            return new PlayerOverride(PlayerOverrideKind.Query, value, fromLink);
        }

        public override string ToString() => Kind == PlayerOverrideKind.Query ? Query : Kind.ToString();

        private static PlayerOverride Unsupported() => new PlayerOverride(PlayerOverrideKind.Unsupported, string.Empty, false);

        private static string StripQuotes(string value)
        {
            if (value.Length >= 2)
            {
                char first = value[0];
                char last = value[value.Length - 1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\'') || (first == '<' && last == '>'))
                {
                    return value.Substring(1, value.Length - 2).Trim();
                }
            }

            return value;
        }

        private static bool LooksLikeLink(string value)
        {
            if (value.IndexOf("://", StringComparison.Ordinal) > 0
                || value.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            int slash = value.IndexOf('/');
            if (slash <= 0) return false;
            string host = value.Substring(0, slash);
            int dot = host.LastIndexOf('.');
            if (dot <= 0 || dot >= host.Length - 2) return false;
            foreach (char c in host)
            {
                bool hostChar = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '-';
                if (!hostChar) return false;
            }

            return true;
        }

        private static string? ValueFromProfileLink(string link)
        {
            try
            {
                string absolute = link.IndexOf("://", StringComparison.Ordinal) >= 0 ? link : "https://" + link;
                if (!Uri.TryCreate(absolute, UriKind.Absolute, out Uri? uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    return null;
                }

                string host = uri.Host.ToLowerInvariant();
                if (host.StartsWith("www.", StringComparison.Ordinal)) host = host.Substring(4);
                string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length < 2) return null;

                string first = segments[0];
                string second = Uri.UnescapeDataString(segments[1]).Trim();
                bool profilePath = string.Equals(first, "u", StringComparison.OrdinalIgnoreCase);
                if (profilePath && (Array.IndexOf(BeatLeaderHosts, host) >= 0 || host == "scoresaber.com"))
                {
                    return second;
                }

                if (host == "steamcommunity.com" && string.Equals(first, "profiles", StringComparison.OrdinalIgnoreCase) && IsAllDigits(second))
                {
                    return second;
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool IsAllDigits(string value)
        {
            if (value.Length == 0) return false;
            foreach (char c in value)
            {
                if (c < '0' || c > '9') return false;
            }

            return true;
        }
    }
}
