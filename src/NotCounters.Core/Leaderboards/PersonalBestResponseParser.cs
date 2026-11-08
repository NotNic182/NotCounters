using System;
using System.Diagnostics.CodeAnalysis;
using NotCounters.Core.Stats;
using Newtonsoft.Json.Linq;

namespace NotCounters.Core.Leaderboards
{
    public enum PersonalBestParseStatus
    {
        Found,
        NoScore,
        Invalid,
    }

    public static class PersonalBestResponseParser
    {
        public static PersonalBestParseStatus Parse(PbSource source, string? json, long? maxScoreHint,
            out PersonalBestInfo info, out string? error)
        {
            info = new PersonalBestInfo { Available = false, Source = source };
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                {
                    return PersonalBestParseStatus.NoScore;
                }

                if (!JsonRead.TryParse(json, out JToken? root, out error))
                {
                    return PersonalBestParseStatus.Invalid;
                }

                if (!TryFindScore(root, out JObject? score, out JToken? container))
                {
                    return PersonalBestParseStatus.NoScore;
                }

                long? unmodified = JsonRead.Long(JsonRead.Prop(score, "unmodifiedScore")) ?? JsonRead.Long(JsonRead.Prop(score, "baseScore"));
                long? modified = JsonRead.Long(JsonRead.Prop(score, "modifiedScore"));
                double? reportedAccuracy = JsonRead.Double(JsonRead.Prop(score, "accuracy"));
                if (!unmodified.HasValue && !modified.HasValue && !reportedAccuracy.HasValue)
                {
                    return PersonalBestParseStatus.NoScore;
                }

                double? accuracy = NormalizeAccuracy(reportedAccuracy);
                if (!accuracy.HasValue)
                {
                    long? maxScore = FindMaxScore(score, container) ?? maxScoreHint;
                    long? numerator = unmodified ?? modified;
                    if (maxScore.HasValue && maxScore.Value > 0 && numerator.HasValue)
                    {
                        accuracy = numerator.Value / (double)maxScore.Value;
                    }
                }

                if (!accuracy.HasValue)
                {
                    error = "The response has no accuracy and no max score.";
                    return PersonalBestParseStatus.Invalid;
                }

                long shownScore = modified ?? unmodified ?? 0;
                info = new PersonalBestInfo
                {
                    Available = true,
                    Source = source,
                    Score = (int)Math.Max(0, Math.Min(int.MaxValue, shownScore)),
                    Accuracy = Clamp01(accuracy.Value),
                    MaxCombo = Math.Max(0, JsonRead.Int(JsonRead.Prop(score, "maxCombo")) ?? 0),
                    FullCombo = JsonRead.Bool(JsonRead.Prop(score, "fullCombo")) ?? false,
                    ScoreId = source == PbSource.BeatLeader ? JsonRead.Long(JsonRead.Prop(score, "id")) : null,
                };
                return PersonalBestParseStatus.Found;
            }
            catch (Exception ex)
            {
                info = new PersonalBestInfo { Available = false, Source = source };
                error = ex.GetType().Name + ": " + ex.Message;
                return PersonalBestParseStatus.Invalid;
            }
        }

        private static bool TryFindScore(JToken root, [NotNullWhen(true)] out JObject? score, out JToken? container)
        {
            score = null;
            container = null;
            JToken? candidate = root;

            if (candidate is JObject wrapper && (JsonRead.Prop(wrapper, "data") ?? JsonRead.Prop(wrapper, "scores")) is JArray list)
            {
                candidate = list;
            }

            if (candidate is JArray array)
            {
                candidate = null;
                foreach (JToken item in array)
                {
                    if (item is JObject first)
                    {
                        candidate = first;
                        break;
                    }
                }
            }

            if (!(candidate is JObject found))
            {
                return false;
            }

            if (JsonRead.Prop(found, "score") is JObject inner)
            {
                container = found;
                score = inner;
            }
            else
            {
                score = found;
            }

            return true;
        }

        private static long? FindMaxScore(JObject score, JToken? container)
        {
            JToken? scoreLeaderboard = JsonRead.Prop(score, "leaderboard");
            return JsonRead.Long(JsonRead.Prop(score, "maxScore"))
                   ?? JsonRead.Long(JsonRead.Prop(JsonRead.Prop(container, "leaderboard"), "maxScore"))
                   ?? JsonRead.Long(JsonRead.Prop(scoreLeaderboard, "maxScore"))
                   ?? JsonRead.Long(JsonRead.Prop(JsonRead.Prop(scoreLeaderboard, "difficulty"), "maxScore"));
        }

        private static double? NormalizeAccuracy(double? value)
        {
            if (!value.HasValue || value.Value < 0)
            {
                return null;
            }

            return value.Value > 1.5 ? value.Value / 100.0 : value.Value;
        }

        private static double Clamp01(double value) => value < 0 ? 0 : (value > 1 ? 1 : value);
    }
}
