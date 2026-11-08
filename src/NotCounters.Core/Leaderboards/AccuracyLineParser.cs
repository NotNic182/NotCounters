using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Leaderboards
{
    public static class AccuracyLineParser
    {
        // BeatLeader averages the notes cut within each second, and the game counts a cut a bit later, once its swing is scored
        public const double BeatLeaderShift = 0.7;

        public static AccuracyLine? FromBeatLeaderStatistic(string? json)
        {
            if (!JsonRead.TryParse(json, out JToken? root, out _))
            {
                return null;
            }

            return FromArray(JsonRead.Prop(JsonRead.Prop(root, "scoreGraphTracker"), "graph"), BeatLeaderShift);
        }

        public static AccuracyLine? FromArray(JToken? token, double shift = 0d)
        {
            if (!(token is JArray array))
            {
                return null;
            }

            var values = new List<double>(array.Count);
            foreach (JToken item in array)
            {
                double? value = JsonRead.Double(item);
                if (!value.HasValue)
                {
                    return null;
                }

                values.Add(value.Value);
            }

            return AccuracyLine.From(values, shift);
        }
    }
}
