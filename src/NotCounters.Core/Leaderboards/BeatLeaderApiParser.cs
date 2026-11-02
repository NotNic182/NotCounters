using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using NotCounters.Core.Pp;
using Newtonsoft.Json.Linq;

namespace NotCounters.Core.Leaderboards
{
    public static class BeatLeaderApiParser
    {
        public static bool TryParseLeaderboard(string? json, [NotNullWhen(true)] out BeatLeaderDifficulty? difficulty, [NotNullWhen(false)] out string? error)
        {
            difficulty = null;
            if (!JsonRead.TryParse(json, out JToken? root, out error)) return false;
            if (!(root is JObject))
            {
                error = "BeatLeader leaderboard response is not an object.";
                return false;
            }

            JToken? diff = JsonRead.Prop(root, "difficulty");
            if (!(diff is JObject))
            {
                error = "BeatLeader leaderboard response has no difficulty object.";
                return false;
            }

            difficulty = FromDifficultyToken(diff, JsonRead.String(JsonRead.Prop(root, "id")));
            return true;
        }

        public static bool TryParseDifficulty(string? json, [NotNullWhen(true)] out BeatLeaderDifficulty? difficulty, [NotNullWhen(false)] out string? error)
        {
            difficulty = null;
            if (!JsonRead.TryParse(json, out JToken? root, out error)) return false;
            if (!(root is JObject))
            {
                error = "BeatLeader difficulty is not an object.";
                return false;
            }

            difficulty = FromDifficultyToken(root, null);
            return true;
        }

        public static bool TryParseLeaderboardsByHash(string? json, out IReadOnlyList<BeatLeaderDifficulty> difficulties, [NotNullWhen(false)] out string? error)
        {
            var list = new List<BeatLeaderDifficulty>();
            difficulties = list.AsReadOnly();
            if (!JsonRead.TryParse(json, out JToken? root, out error)) return false;

            JToken? boards = root is JArray ? root : JsonRead.Prop(root, "leaderboards");
            if (!(boards is JArray array))
            {
                error = "BeatLeader leaderboards-by-hash response has no leaderboards array.";
                return false;
            }

            foreach (JToken item in array)
            {
                JToken? diff = JsonRead.Prop(item, "difficulty");
                if (diff is JObject)
                {
                    list.Add(FromDifficultyToken(diff, JsonRead.String(JsonRead.Prop(item, "id"))));
                }
            }

            return true;
        }

        public static bool TryParseLeaderboardsByHash(string? json, string? characteristic, string? difficultyName,
            [NotNullWhen(true)] out BeatLeaderDifficulty? difficulty, [NotNullWhen(false)] out string? error)
        {
            difficulty = null;
            if (!TryParseLeaderboardsByHash(json, out IReadOnlyList<BeatLeaderDifficulty> all, out error)) return false;
            if (TryFindDifficulty(all, characteristic, difficultyName, out difficulty)) return true;
            error = "BeatLeader has no leaderboard for " + (characteristic ?? "?") + "/" + (difficultyName ?? "?") + ".";
            return false;
        }

        public static bool TryFindDifficulty(IReadOnlyList<BeatLeaderDifficulty>? difficulties, string? characteristic,
            string? difficultyName, [NotNullWhen(true)] out BeatLeaderDifficulty? difficulty)
        {
            difficulty = null;
            if (difficulties == null || string.IsNullOrWhiteSpace(characteristic) || string.IsNullOrWhiteSpace(difficultyName)) return false;
            string wantDiff = LevelIdentity.NormalizeDifficultyName(difficultyName) ?? difficultyName!.Trim();
            string wantMode = characteristic!.Trim();
            foreach (BeatLeaderDifficulty d in difficulties)
            {
                if (d == null) continue;
                string haveDiff = LevelIdentity.NormalizeDifficultyName(d.DifficultyName) ?? d.DifficultyName ?? string.Empty;
                if (string.Equals(haveDiff, wantDiff, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(d.ModeName?.Trim(), wantMode, StringComparison.OrdinalIgnoreCase))
                {
                    difficulty = d;
                    return true;
                }
            }

            return false;
        }

        public static bool TryParseStatus(string? value, out int status)
        {
            status = (int)BeatLeaderStatus.Unknown;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string s = value!.Trim();
            if (JsonRead.TryParseIntegral(s, out int n))
            {
                if (n < 0) return false;
                status = n;
                return true;
            }

            var letters = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetter(c)) letters.Append(char.ToLowerInvariant(c));
            }

            switch (letters.ToString())
            {
                case "unranked": status = 0; return true;
                case "nominated": status = 1; return true;
                case "qualified": status = 2; return true;
                case "ranked": status = 3; return true;
                case "unrankable": status = 4; return true;
                case "outdated": status = 5; return true;
                case "inevent": status = 6; return true;
                case "ost": status = 7; return true;
                default: return false;
            }
        }

        private static BeatLeaderDifficulty FromDifficultyToken(JToken diff, string? leaderboardId)
        {
            int status = (int)BeatLeaderStatus.Unknown;
            string? statusText = JsonRead.String(JsonRead.Prop(diff, "status"));
            if (TryParseStatus(statusText, out int parsed)) status = parsed;

            return new BeatLeaderDifficulty
            {
                LeaderboardId = leaderboardId ?? JsonRead.String(JsonRead.Prop(diff, "leaderboardId")),
                Status = status,
                Stars = JsonRead.Double(JsonRead.Prop(diff, "stars")),
                AccRating = JsonRead.Double(JsonRead.Prop(diff, "accRating")),
                PassRating = JsonRead.Double(JsonRead.Prop(diff, "passRating")),
                TechRating = JsonRead.Double(JsonRead.Prop(diff, "techRating")),
                PredictedAcc = JsonRead.Double(JsonRead.Prop(diff, "predictedAcc")),
                SliderPercentage = JsonRead.Double(JsonRead.Prop(diff, "sliderPercentage")),
                Notes = JsonRead.Int(JsonRead.Prop(diff, "notes")) ?? 0,
                NoteSpan = JsonRead.Double(JsonRead.Prop(diff, "noteSpan")),
                MaxScore = JsonRead.Int(JsonRead.Prop(diff, "maxScore")) ?? 0,
                ModeName = JsonRead.String(JsonRead.Prop(diff, "modeName")),
                DifficultyName = JsonRead.String(JsonRead.Prop(diff, "difficultyName")),
                Value = JsonRead.Int(JsonRead.Prop(diff, "value")),
                ModifierValues = ReadModifierValues(JsonRead.Prop(diff, "modifierValues")),
                ModifiersRating = ReadModifiersRating(JsonRead.Prop(diff, "modifiersRating")),
            };
        }

        private static IReadOnlyDictionary<string, double>? ReadModifierValues(JToken? token)
        {
            JObject? obj = AsObject(token);
            if (obj == null) return null;
            var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (JProperty p in obj.Properties())
            {
                if (string.Equals(p.Name, "modifierId", StringComparison.OrdinalIgnoreCase)) continue;
                double? v = JsonRead.Double(p.Value);
                if (v.HasValue) values[p.Name.ToLowerInvariant()] = v.Value;
            }

            return values;
        }

        private static IReadOnlyDictionary<string, double?>? ReadModifiersRating(JToken? token)
        {
            JObject? obj = AsObject(token);
            if (obj == null) return null;
            var ratings = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
            foreach (JProperty p in obj.Properties())
            {
                JTokenType type = p.Value.Type;
                if (type == JTokenType.Null || type == JTokenType.Undefined)
                {
                    ratings[p.Name] = null;
                }
                else
                {
                    double? v = JsonRead.Double(p.Value);
                    if (v.HasValue || type == JTokenType.String) ratings[p.Name] = v;
                }
            }

            return ratings;
        }

        private static JObject? AsObject(JToken? token)
        {
            if (token is JObject o) return o;
            if (token != null && token.Type == JTokenType.String
                && JsonRead.TryParse((string?)token, out JToken? inner, out _) && inner is JObject io)
            {
                return io;
            }

            return null;
        }
    }
}
