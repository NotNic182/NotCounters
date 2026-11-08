using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Leaderboards
{
    public static class AccuracyLineParser
    {
        public static AccuracyLine? FromBeatLeaderStatistic(string? json)
        {
            if (!JsonRead.TryParse(json, out JToken? root, out _))
            {
                return null;
            }

            return FromArray(JsonRead.Prop(JsonRead.Prop(root, "scoreGraphTracker"), "graph"));
        }

        public static AccuracyLine? FromArray(JToken? token)
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

            return AccuracyLine.From(values);
        }
    }
}
