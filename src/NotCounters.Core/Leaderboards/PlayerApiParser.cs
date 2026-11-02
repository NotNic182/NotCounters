using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json.Linq;

namespace NotCounters.Core.Leaderboards
{
    public static class PlayerApiParser
    {
        public static bool TryParseBeatLeaderPlayer(string? json, [NotNullWhen(true)] out LeaderboardPlayer? player, [NotNullWhen(false)] out string? error) =>
            TryParseSingle(json, "BeatLeader player", out player, out error);

        public static bool TryParseBeatLeaderSearch(string? json, out IReadOnlyList<LeaderboardPlayer> players, [NotNullWhen(false)] out string? error)
        {
            bool parsed = TryParseBeatLeaderSearchPage(json, out PlayerSearchPage page, out error);
            players = page.Players;
            return parsed;
        }

        public static bool TryParseBeatLeaderSearchPage(string? json, out PlayerSearchPage page, [NotNullWhen(false)] out string? error) =>
            TryParseList(json, "data", "BeatLeader player search", out page, out error);

        public static bool TryParseScoreSaberPlayer(string? json, [NotNullWhen(true)] out LeaderboardPlayer? player, [NotNullWhen(false)] out string? error) =>
            TryParseSingle(json, "ScoreSaber player", out player, out error);

        public static bool TryParseScoreSaberSearch(string? json, out IReadOnlyList<LeaderboardPlayer> players, [NotNullWhen(false)] out string? error)
        {
            bool parsed = TryParseScoreSaberSearchPage(json, out PlayerSearchPage page, out error);
            players = page.Players;
            return parsed;
        }

        public static bool TryParseScoreSaberSearchPage(string? json, out PlayerSearchPage page, [NotNullWhen(false)] out string? error) =>
            TryParseList(json, "players", "ScoreSaber player search", out page, out error);

        private static bool TryParseSingle(string? json, string what, [NotNullWhen(true)] out LeaderboardPlayer? player, [NotNullWhen(false)] out string? error)
        {
            player = null;
            if (!JsonRead.TryParse(json, out JToken? root, out error)) return false;
            if (!(root is JObject))
            {
                error = what + " response is not an object.";
                return false;
            }

            player = ReadPlayer(root);
            if (player == null)
            {
                error = what + " response has no player id.";
                return false;
            }

            return true;
        }

        private static bool TryParseList(string? json, string arrayProperty, string what, out PlayerSearchPage page, [NotNullWhen(false)] out string? error)
        {
            var list = new List<LeaderboardPlayer>();
            page = new PlayerSearchPage(list.AsReadOnly(), 0, null, null);
            if (!JsonRead.TryParse(json, out JToken? root, out error)) return false;

            JToken? items = root is JArray ? root : JsonRead.Prop(root, arrayProperty);
            if (!(items is JArray array))
            {
                error = what + " response has no " + arrayProperty + " array.";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JToken item in array)
            {
                LeaderboardPlayer? player = ReadPlayer(item);
                if (player != null && seen.Add(player.Id)) list.Add(player);
            }

            JToken? metadata = root is JObject ? JsonRead.Prop(root, "metadata") : null;
            page = new PlayerSearchPage(
                list.AsReadOnly(),
                array.Count,
                NonNegative(JsonRead.Int(JsonRead.Prop(metadata, "total"))),
                Positive(JsonRead.Int(JsonRead.Prop(metadata, "itemsPerPage"))));
            return true;
        }

        private static int? NonNegative(int? value) => value.HasValue && value.Value >= 0 ? value : null;

        private static int? Positive(int? value) => value.HasValue && value.Value > 0 ? value : null;

        private static LeaderboardPlayer? ReadPlayer(JToken? token)
        {
            if (!(token is JObject)) return null;
            string? id = Trimmed(JsonRead.String(JsonRead.Prop(token, "id")));
            if (id == null) return null;

            string name = Trimmed(JsonRead.String(JsonRead.Prop(token, "name"))) ?? id;
            string? alias = Trimmed(JsonRead.String(JsonRead.Prop(token, "alias")));

            int? rank = JsonRead.Int(JsonRead.Prop(token, "rank")) ?? JsonRead.Int(JsonRead.Prop(JsonRead.Prop(token, "stats"), "rank"));
            if (rank.HasValue && rank.Value <= 0) rank = null;

            return new LeaderboardPlayer(id, name, alias, rank);
        }

        private static string? Trimmed(string? value)
        {
            if (value == null) return null;
            string trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }
}
