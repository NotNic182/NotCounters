using System.Globalization;
using System.Text;

namespace NotCounters.Core.Leaderboards
{
    public static class PlayerAccountTexts
    {
        public const int MaxNameLength = 32;

        public const string Detecting = "Detecting…";

        public const string WaitingForGameAccount = "Waiting for the game’s account…";

        public const string GameAccountUnavailable = "Could not read the game’s account, enter yours below";

        public const string NoLinkedAccount = "Not found, enter yours below";

        public const string UnsupportedOverride = "Not a BeatLeader or ScoreSaber profile link";

        public const string Disabled = "Not looked up";

        public const string ModSwitchedOff = "Not looked up while \"Enable NotCounters in play\" is off";

        public static string Found(string? name, int? rank, bool isOverride)
        {
            var text = new StringBuilder(DisplayName(name));
            if (rank.HasValue && rank.Value > 0)
            {
                text.Append(" · #").Append(rank.Value.ToString(CultureInfo.InvariantCulture));
            }

            text.Append(isOverride ? " (entered)" : " (detected)");
            return text.ToString();
        }

        public static string NoMatch(string? query) =>
            "No player found for ‘" + DisplayName(query) + "’. Check the spelling or use your profile link";

        public static string Ambiguous(string? query) =>
            "Several players are called ‘" + DisplayName(query) + "’. Use your profile link or ID";

        public static string TooManyMatches(string? query) =>
            "Too many players match ‘" + DisplayName(query) + "’. Use your profile link or ID";

        public static string Unreachable(LeaderboardService service) =>
            "Could not reach " + (service == LeaderboardService.BeatLeader ? "BeatLeader" : "ScoreSaber") + ", retrying";

        public static string DisplayName(string? name)
        {
            string value = (name ?? string.Empty).Trim();
            if (value.Length > MaxNameLength)
            {
                int keep = MaxNameLength - 1;
                if (char.IsHighSurrogate(value[keep - 1])) keep--;
                value = value.Substring(0, keep).TrimEnd() + "…";
            }

            return value.Replace('<', '‹').Replace('>', '›');
        }
    }
}
