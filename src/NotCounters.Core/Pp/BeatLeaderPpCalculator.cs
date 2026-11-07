using System;
using System.Collections.Generic;
using C = NotCounters.Core.Pp.BeatLeaderPpConstants;

namespace NotCounters.Core.Pp
{
    public readonly struct BeatLeaderStarCheck
    {
        public BeatLeaderStarCheck(string ratingSet, double apiStars, double computedStars, double tolerance)
        {
            RatingSet = ratingSet;
            ApiStars = apiStars;
            ComputedStars = computedStars;
            IsDrift = !(Math.Abs(computedStars - apiStars) <= tolerance);
        }

        public string RatingSet { get; }

        public double ApiStars { get; }

        public double ComputedStars { get; }

        public double Delta => ComputedStars - ApiStars;

        public bool IsDrift { get; }
    }

    public sealed class BeatLeaderDriftReport
    {
        internal BeatLeaderDriftReport(IReadOnlyList<BeatLeaderStarCheck> checks, bool isApplicable)
        {
            Checks = checks;
            IsApplicable = isApplicable;
            double max = 0;
            foreach (BeatLeaderStarCheck c in checks)
            {
                if (c.IsDrift) HasDrift = true;
                double d = Math.Abs(c.Delta);
                if (d > max || double.IsNaN(d)) max = d;
            }

            MaxAbsDelta = max;
        }

        public IReadOnlyList<BeatLeaderStarCheck> Checks { get; }

        public bool IsApplicable { get; }

        public bool HasDrift { get; }

        public double MaxAbsDelta { get; }
    }

    public static class BeatLeaderPpCalculator
    {
        private static readonly string[] SpeedSets = { "SS", "FS", "SF" };

        public static BeatLeaderPpContext CreateContext(BeatLeaderDifficulty? difficulty, string? modifiers) =>
            BeatLeaderPpContext.Create(difficulty, modifiers);

        public static BeatLeaderPpResult Calculate(double accuracy, string? modifiers, BeatLeaderDifficulty? difficulty) =>
            BeatLeaderPpContext.Create(difficulty, modifiers).Evaluate(accuracy);

        public static double? ComputeStars(BeatLeaderDifficulty? difficulty, string? ratingSet = null)
        {
            if (difficulty == null) return null;
            try
            {
                string set = BeatLeaderPpContext.NormalizeModifiers(ratingSet);
                if (set.Length == 0)
                {
                    BeatLeaderAccCurve curve = BeatLeaderAccCurve.Build(difficulty.PredictedAcc, difficulty.SliderPercentage, difficulty.Notes, difficulty.NoteSpan);
                    return BeatLeaderPpFormulas.Stars(
                        PpNumerics.FiniteOr(difficulty.AccRating, 0),
                        PpNumerics.FiniteOr(difficulty.PassRating, 0),
                        PpNumerics.FiniteOr(difficulty.TechRating, 0),
                        curve,
                        PpNumerics.FiniteOr(difficulty.PredictedAcc, 0));
                }

                if (!difficulty.HasModifierRating(set, "AccRating")) return null;
                double? pa = difficulty.PredictedAcc, slider = difficulty.SliderPercentage, span = difficulty.NoteSpan;
                if (C.SpeedTimescales.TryGetValue(set, out double timescale))
                {
                    pa = difficulty.GetModifierRating(set, "PredictedAcc");
                    slider = difficulty.GetModifierRating(set, "SliderPercentage") ?? difficulty.SliderPercentage;
                    span = PpNumerics.FiniteOr(difficulty.NoteSpan, 0) / timescale;
                }

                BeatLeaderAccCurve speedCurve = BeatLeaderAccCurve.Build(pa, slider, difficulty.Notes, span);
                return BeatLeaderPpFormulas.Stars(
                    PpNumerics.FiniteOr(difficulty.GetModifierRating(set, "AccRating"), 0),
                    PpNumerics.FiniteOr(difficulty.GetModifierRating(set, "PassRating"), 0),
                    PpNumerics.FiniteOr(difficulty.GetModifierRating(set, "TechRating"), 0),
                    speedCurve,
                    PpNumerics.FiniteOr(pa, 0));
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static BeatLeaderDriftReport DetectDrift(BeatLeaderDifficulty? difficulty, double tolerance = C.DriftTolerance)
        {
            var checks = new List<BeatLeaderStarCheck>();
            if (difficulty == null || !difficulty.NoteSpan.HasValue) return new BeatLeaderDriftReport(checks, false);

            AddCheck(checks, string.Empty, difficulty.Stars, difficulty, tolerance);
            foreach (string set in SpeedSets)
            {
                if (difficulty.HasModifierRating(set, "AccRating"))
                {
                    AddCheck(checks, set, difficulty.GetModifierRating(set, "Stars"), difficulty, tolerance);
                }
            }

            return new BeatLeaderDriftReport(checks, checks.Count > 0);
        }

        public static double ProfileTotal(IEnumerable<double>? pps)
        {
            if (pps == null) return 0;
            var list = new List<double>();
            foreach (double pp in pps)
            {
                if (PpNumerics.IsFinite(pp) && pp > 0) list.Add(pp);
            }

            list.Sort((a, b) => b.CompareTo(a));
            double total = 0, weight = 1;
            foreach (double pp in list)
            {
                total += pp * weight;
                weight *= C.ProfileWeight;
            }

            return total;
        }

        private static void AddCheck(List<BeatLeaderStarCheck> checks, string set, double? apiStars, BeatLeaderDifficulty d, double tolerance)
        {
            if (!apiStars.HasValue || !PpNumerics.IsFinite(apiStars.Value)) return;
            double? computed = ComputeStars(d, set);
            if (!computed.HasValue) return;
            checks.Add(new BeatLeaderStarCheck(set, apiStars.Value, computed.Value, tolerance));
        }
    }
}
