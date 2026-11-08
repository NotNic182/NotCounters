using System;
using Newtonsoft.Json.Linq;
using NotCounters.Core.Leaderboards;

namespace NotCounters.Core.Stats
{
    public sealed class SavedAccuracyLine
    {
        public const int CurrentVersion = 1;

        public SavedAccuracyLine(AccuracyLine line, int score)
        {
            Line = line ?? throw new ArgumentNullException(nameof(line));
            Score = Math.Max(0, score);
        }

        public AccuracyLine Line { get; }

        public int Score { get; }

        public bool Matches(PersonalBestInfo? personalBest) =>
            personalBest != null && personalBest.Available
            && (Line.EndsAt(personalBest.Accuracy) || (Score > 0 && Score == personalBest.Score));

        public bool IsBetterThan(SavedAccuracyLine? other) => other == null || Line.Final > other.Line.Final;

        public string ToJson()
        {
            var values = new JArray();
            foreach (double value in Line.Values)
            {
                values.Add(Math.Round(value, 6));
            }

            var root = new JObject
            {
                ["version"] = CurrentVersion,
                ["score"] = Score,
                ["accuracy"] = values,
            };
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static SavedAccuracyLine? FromJson(string? json)
        {
            if (!JsonRead.TryParse(json, out JToken? root, out _) || JsonRead.Int(JsonRead.Prop(root, "version")) != CurrentVersion)
            {
                return null;
            }

            AccuracyLine? line = AccuracyLineParser.FromArray(JsonRead.Prop(root, "accuracy"));
            return line == null ? null : new SavedAccuracyLine(line, JsonRead.Int(JsonRead.Prop(root, "score")) ?? 0);
        }
    }
}
