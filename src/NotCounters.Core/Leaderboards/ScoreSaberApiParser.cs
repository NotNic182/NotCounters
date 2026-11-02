using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using NotCounters.Core.Pp;
using Newtonsoft.Json.Linq;

namespace NotCounters.Core.Leaderboards
{
    public static class ScoreSaberApiParser
    {
        public static bool TryParseLeaderboardV2(string? json, [NotNullWhen(true)] out ScoreSaberLeaderboard? leaderboard, [NotNullWhen(false)] out string? error)
        {
            leaderboard = null;
            if (!JsonRead.TryParse(json, out JToken? root, out error)) return false;
            if (!(root is JObject))
            {
                error = "ScoreSaber leaderboard response is not an object.";
                return false;
            }

            long? id = JsonRead.Long(JsonRead.Prop(root, "id"));
            if (!id.HasValue)
            {
                error = ErrorBodyMessage(root) ?? "ScoreSaber leaderboard response has no id.";
                return false;
            }

            JToken? realm = JsonRead.Prop(root, "realm");
            JToken? diff = JsonRead.Prop(root, "difficulty");
            bool valuesOk = TryReadModifierValues(JsonRead.Prop(realm, "modifierValues"), out IReadOnlyDictionary<string, double> values);

            leaderboard = new ScoreSaberLeaderboard
            {
                Id = id.Value,
                Status = ParseStatus(JsonRead.Prop(realm, "leaderboardStatus")),
                Stars = JsonRead.Double(JsonRead.Prop(realm, "stars")) ?? 0,
                MaxScore = JsonRead.Long(JsonRead.Prop(root, "maxScore")) ?? 0,
                PositiveModifiers = JsonRead.Bool(JsonRead.Prop(realm, "positiveModifiers")) ?? false,
                ModifierValues = valuesOk ? values : null,
                ModifierValuesAreDefaults = !valuesOk,
                RealmId = JsonRead.Int(JsonRead.Prop(realm, "realmId")) ?? 1,
                Hash = JsonRead.String(JsonRead.Prop(JsonRead.Prop(root, "map"), "hash")),
                Difficulty = JsonRead.Int(JsonRead.Prop(diff, "difficulty")) ?? 0,
                GameMode = JsonRead.String(JsonRead.Prop(diff, "gameMode")),
            };
            return true;
        }

        public static bool TryParseInfoV1(string? json, [NotNullWhen(true)] out ScoreSaberLeaderboard? leaderboard, [NotNullWhen(false)] out string? error)
        {
            leaderboard = null;
            if (!JsonRead.TryParse(json, out JToken? root, out error)) return false;
            if (!(root is JObject))
            {
                error = "ScoreSaber v1 info response is not an object.";
                return false;
            }

            long? id = JsonRead.Long(JsonRead.Prop(root, "id"));
            if (!id.HasValue)
            {
                error = ErrorBodyMessage(root) ?? "ScoreSaber v1 info response has no id.";
                return false;
            }

            bool? ranked = JsonRead.Bool(JsonRead.Prop(root, "ranked"));
            bool? qualified = JsonRead.Bool(JsonRead.Prop(root, "qualified"));
            bool? loved = JsonRead.Bool(JsonRead.Prop(root, "loved"));
            ScoreSaberLeaderboardStatus status;
            if (ranked == true) status = ScoreSaberLeaderboardStatus.Ranked;
            else if (qualified == true) status = ScoreSaberLeaderboardStatus.Qualified;
            else if (loved == true) status = ScoreSaberLeaderboardStatus.Loved;
            else if (ranked.HasValue || qualified.HasValue || loved.HasValue) status = ScoreSaberLeaderboardStatus.Unranked;
            else status = ScoreSaberLeaderboardStatus.Unknown;

            JToken? diff = JsonRead.Prop(root, "difficulty");
            leaderboard = new ScoreSaberLeaderboard
            {
                Id = id.Value,
                Status = status,
                Stars = JsonRead.Double(JsonRead.Prop(root, "stars")) ?? 0,
                MaxScore = JsonRead.Long(JsonRead.Prop(root, "maxScore")) ?? 0,
                PositiveModifiers = JsonRead.Bool(JsonRead.Prop(root, "positiveModifiers")) ?? false,
                ModifierValues = null,
                ModifierValuesAreDefaults = true,
                V1MaxPp = JsonRead.Double(JsonRead.Prop(root, "maxPP")),
                Hash = JsonRead.String(JsonRead.Prop(root, "songHash")),
                Difficulty = JsonRead.Int(JsonRead.Prop(diff, "difficulty")) ?? 0,
                GameMode = JsonRead.String(JsonRead.Prop(diff, "gameMode")),
            };
            return true;
        }

        public static bool TryParseCurves(string? json, [NotNullWhen(true)] out ScoreSaberCurves? curves, [NotNullWhen(false)] out string? error)
        {
            curves = null;
            if (!JsonRead.TryParse(json, out JToken? root, out error)) return false;
            List<CurvePoint>? normal = ReadCurve(JsonRead.Prop(root, "curve"));
            List<CurvePoint>? positive = ReadCurve(JsonRead.Prop(root, "positiveModifierCurve"));
            if (normal == null || normal.Count == 0 || positive == null || positive.Count == 0)
            {
                error = "ScoreSaber pp-curve response lacks a usable curve or positiveModifierCurve.";
                return false;
            }

            curves = new ScoreSaberCurves(new PpCurve(normal), new PpCurve(positive));
            return true;
        }

        public static bool TryParseScores(string? json, out IReadOnlyList<ScoreSaberScoreSample> scores, [NotNullWhen(false)] out string? error)
        {
            var list = new List<ScoreSaberScoreSample>();
            scores = list.AsReadOnly();
            if (!JsonRead.TryParse(json, out JToken? root, out error)) return false;
            JToken? data = root is JArray ? root : JsonRead.Prop(root, "data");
            if (!(data is JArray array))
            {
                error = ErrorBodyMessage(root) ?? "ScoreSaber scores response has no data array.";
                return false;
            }

            foreach (JToken item in array)
            {
                if (!(item is JObject)) continue;
                long? modified = JsonRead.Long(JsonRead.Prop(item, "modifiedScore"));
                double? pp = JsonRead.Double(JsonRead.Prop(item, "pp"));
                if (!modified.HasValue || !pp.HasValue) continue;
                list.Add(new ScoreSaberScoreSample
                {
                    Id = JsonRead.Long(JsonRead.Prop(item, "id")) ?? 0,
                    UnmodifiedScore = JsonRead.Long(JsonRead.Prop(item, "unmodifiedScore")) ?? JsonRead.Long(JsonRead.Prop(item, "baseScore")),
                    ModifiedScore = modified.Value,
                    Pp = pp.Value,
                    Mods = ReadMods(JsonRead.Prop(item, "mods") ?? JsonRead.Prop(item, "modifiers")),
                    Outcome = ParseOutcome(JsonRead.Prop(item, "playOutcome")),
                });
            }

            return true;
        }

        public static bool TryParseModifierValues(string? json, out IReadOnlyDictionary<string, double> values)
        {
            if (!JsonRead.TryParse(json, out JToken? token, out _))
            {
                values = new Dictionary<string, double>();
                return false;
            }

            return TryReadModifierValues(token, out values);
        }

        public static ScoreSaberLeaderboardStatus ParseStatus(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return ScoreSaberLeaderboardStatus.Unknown;
            string s = value!.Trim();
            if (JsonRead.TryParseIntegral(s, out int n))
            {
                return n >= 0 && n <= 3 ? (ScoreSaberLeaderboardStatus)n : ScoreSaberLeaderboardStatus.Unknown;
            }

            switch (Letters(s))
            {
                case "unranked": return ScoreSaberLeaderboardStatus.Unranked;
                case "ranked": return ScoreSaberLeaderboardStatus.Ranked;
                case "qualified": return ScoreSaberLeaderboardStatus.Qualified;
                case "loved": return ScoreSaberLeaderboardStatus.Loved;
                default: return ScoreSaberLeaderboardStatus.Unknown;
            }
        }

        public static ScoreSaberPlayOutcome ParseOutcome(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return ScoreSaberPlayOutcome.Unknown;
            string s = value!.Trim();
            if (JsonRead.TryParseIntegral(s, out int n))
            {
                return n >= 0 && n <= 3 ? (ScoreSaberPlayOutcome)n : ScoreSaberPlayOutcome.Unknown;
            }

            switch (Letters(s))
            {
                case "clear": return ScoreSaberPlayOutcome.Clear;
                case "fail": return ScoreSaberPlayOutcome.Fail;
                case "quit": return ScoreSaberPlayOutcome.Quit;
                case "restart": return ScoreSaberPlayOutcome.Restart;
                default: return ScoreSaberPlayOutcome.Unknown;
            }
        }

        private static ScoreSaberLeaderboardStatus ParseStatus(JToken? token) => ParseStatus(JsonRead.String(token));

        private static ScoreSaberPlayOutcome ParseOutcome(JToken? token) => ParseOutcome(JsonRead.String(token));

        internal static bool TryReadModifierValues(JToken? token, out IReadOnlyDictionary<string, double> values) =>
            TryReadModifierValues(token, 0, out values);

        private static bool TryReadModifierValues(JToken? token, int depth, out IReadOnlyDictionary<string, double> values)
        {
            var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            values = map;
            if (token == null || depth > 2) return false;

            switch (token.Type)
            {
                case JTokenType.String:
                    return JsonRead.TryParse((string?)token, out JToken? inner, out _)
                           && TryReadModifierValues(inner, depth + 1, out values);
                case JTokenType.Object:
                    foreach (JProperty p in ((JObject)token).Properties())
                    {
                        double? v = JsonRead.Double(p.Value);
                        if (v.HasValue && p.Name.Trim().Length > 0) map[p.Name.Trim().ToUpperInvariant()] = v.Value;
                    }

                    break;
                case JTokenType.Array:
                    foreach (JToken entry in (JArray)token)
                    {
                        string? code = null;
                        double? v = null;
                        if (entry is JArray pair && pair.Count >= 2)
                        {
                            code = JsonRead.String(pair[0]);
                            v = JsonRead.Double(pair[1]);
                        }
                        else if (entry is JObject o)
                        {
                            code = JsonRead.String(JsonRead.Prop(o, "name") ?? JsonRead.Prop(o, "code") ?? JsonRead.Prop(o, "modifier"));
                            v = JsonRead.Double(JsonRead.Prop(o, "value"));
                        }

                        if (!string.IsNullOrWhiteSpace(code) && v.HasValue) map[code!.Trim().ToUpperInvariant()] = v.Value;
                    }

                    break;
                default:
                    return false;
            }

            return map.Count > 0;
        }

        private static List<CurvePoint>? ReadCurve(JToken? token)
        {
            if (token != null && token.Type == JTokenType.String
                && JsonRead.TryParse((string?)token, out JToken? inner, out _))
            {
                token = inner;
            }

            if (!(token is JArray array)) return null;
            var points = new List<CurvePoint>(array.Count);
            foreach (JToken entry in array)
            {
                double? x = null, y = null;
                if (entry is JArray pair && pair.Count >= 2)
                {
                    x = JsonRead.Double(pair[0]);
                    y = JsonRead.Double(pair[1]);
                }
                else if (entry is JObject o)
                {
                    x = JsonRead.Double(JsonRead.Prop(o, "x"));
                    y = JsonRead.Double(JsonRead.Prop(o, "y"));
                }

                if (x.HasValue && y.HasValue) points.Add(new CurvePoint(x.Value, y.Value));
            }

            return points;
        }

        private static IReadOnlyList<string> ReadMods(JToken? token)
        {
            var mods = new List<string>();
            if (token is JArray array)
            {
                foreach (JToken t in array)
                {
                    string? s = JsonRead.String(t);
                    if (!string.IsNullOrWhiteSpace(s)) mods.Add(s!.Trim().ToUpperInvariant());
                }
            }
            else
            {
                string? s = JsonRead.String(token);
                if (!string.IsNullOrWhiteSpace(s))
                {
                    foreach (string part in s!.Split(','))
                    {
                        if (part.Trim().Length > 0) mods.Add(part.Trim().ToUpperInvariant());
                    }
                }
            }

            return mods.AsReadOnly();
        }

        private static string? ErrorBodyMessage(JToken? root)
        {
            string? message = JsonRead.String(JsonRead.Prop(root, "message")) ?? JsonRead.String(JsonRead.Prop(root, "errorMessage"));
            if (message == null) return null;
            string? code = JsonRead.String(JsonRead.Prop(root, "statusCode"));
            return code != null ? "ScoreSaber error " + code + ": " + message : "ScoreSaber error: " + message;
        }

        private static string Letters(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetter(c)) sb.Append(char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }
    }
}
