using System;

namespace NotCounters.Core.Scoring
{
    public static class RankCalculator
    {
        public static string GetRankName(int multiplied, int modified, int maxMultiplied, int maxModified)
        {
            if (maxModified <= 0) return "E";
            if (maxMultiplied == 0) return "SS";
            int denominator = Math.Max(maxMultiplied, maxModified);
            float ratio = (float)modified / denominator;
            if (multiplied == maxMultiplied && modified >= multiplied) return "SSS";
            if (ratio > 0.9f) return "SS";
            if (ratio > 0.8f) return "S";
            if (ratio > 0.65f) return "A";
            if (ratio > 0.5f) return "B";
            if (ratio > 0.35f) return "C";
            if (ratio > 0.2f) return "D";
            return "E";
        }

        public static string GetImmediateRankName(int multiplied, int modified, int maxMultiplied, int maxModified)
        {
            string rank = GetRankName(multiplied, modified, maxMultiplied, maxModified);
            return rank == "SSS" ? "SS" : rank;
        }
    }
}
