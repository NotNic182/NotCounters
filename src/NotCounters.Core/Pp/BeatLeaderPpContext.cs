using System;
using System.Linq;
using C = NotCounters.Core.Pp.BeatLeaderPpConstants;

namespace NotCounters.Core.Pp
{
    public sealed class BeatLeaderPpContext
    {
        private readonly double _passPpConstant;

        private BeatLeaderPpContext(BeatLeaderDifficulty? difficulty, string modifiers, string? error)
        {
            Difficulty = difficulty;
            Modifiers = modifiers;
            Error = error;
            RatingSet = string.Empty;
            ModifierMultiplier = 1;
            DisplayModifierMultiplier = 1;
            CurvePower = 1;
            LowStarMultiplier = 1;
        }

        private BeatLeaderPpContext(BeatLeaderDifficulty difficulty, string modifiers, Precomputed p)
        {
            Difficulty = difficulty;
            Modifiers = modifiers;
            IsNoFail = p.IsNoFail;
            IsRhythmGameStandard = p.IsRhythmGameStandard;
            IsLegacy = p.IsLegacy;
            RatingSet = p.RatingSet;
            AccRating = p.Acc;
            PassRating = p.Pass;
            TechRating = p.Tech;
            ModifierMultiplier = p.Mp;
            DisplayModifierMultiplier = p.DisplayMp;
            Curve = p.Curve;
            Stars = p.Stars;
            ApiStars = p.ApiStars;
            CurvePower = p.CurvePower;
            LowStarMultiplier = p.Lsm;
            Curve96 = p.C96;
            HasStarDrift = p.Drift;
            _passPpConstant = p.PassPp;
        }

        public BeatLeaderDifficulty? Difficulty { get; }

        public string Modifiers { get; }

        public bool IsValid => Error == null;

        public string? Error { get; }

        public bool IsNoFail { get; }

        public bool IsRhythmGameStandard { get; }

        public bool IsLegacy { get; }

        public string RatingSet { get; }

        public double AccRating { get; }

        public double PassRating { get; }

        public double TechRating { get; }

        public double ModifierMultiplier { get; }

        public double DisplayModifierMultiplier { get; }

        public BeatLeaderAccCurve? Curve { get; }

        public double Stars { get; }

        public double? ApiStars { get; }

        public bool HasStarDrift { get; }

        public double CurvePower { get; }

        public double LowStarMultiplier { get; }

        public double Curve96 { get; }

        public bool IsApproximate => IsLegacy || HasStarDrift;

        public double DisplayStars => (ApiStars ?? Stars) * DisplayModifierMultiplier;

        public static BeatLeaderPpContext Create(BeatLeaderDifficulty? difficulty, string? modifiers)
        {
            string mods = NormalizeModifiers(modifiers);
            if (difficulty == null) return new BeatLeaderPpContext(null, mods, "No BeatLeader difficulty data.");
            try
            {
                return new BeatLeaderPpContext(difficulty, mods, Precompute(difficulty, mods));
            }
            catch (Exception ex)
            {
                return new BeatLeaderPpContext(null, mods, "BeatLeader precompute failed: " + ex.Message);
            }
        }

        public BeatLeaderPpResult Evaluate(double accuracy)
        {
            if (!IsValid) return BeatLeaderPpResult.Zero(0, false, false, BeatLeaderZeroReason.NoData);
            bool approx = IsApproximate;
            if (double.IsNaN(accuracy) || accuracy <= 0 || accuracy > 1)
            {
                return BeatLeaderPpResult.Zero(Stars, approx, IsLegacy, BeatLeaderZeroReason.InvalidAccuracy);
            }

            if (IsRhythmGameStandard)
            {
                double rgs = PpNumerics.FiniteOr(accuracy * PpNumerics.FiniteOr(Difficulty?.PassRating, 0) * C.RhythmGameStandardFactor, 0);
                if (rgs < 0) rgs = 0;
                return new BeatLeaderPpResult(rgs, rgs, 0, 0, Stars, approx, IsLegacy, BeatLeaderZeroReason.None);
            }

            if (IsNoFail) return BeatLeaderPpResult.Zero(Stars, approx, IsLegacy, BeatLeaderZeroReason.NoFail);

            try
            {
                return IsLegacy ? EvaluateLegacy(accuracy) : EvaluateV4(accuracy);
            }
            catch (Exception)
            {
                return BeatLeaderPpResult.Zero(Stars, approx, IsLegacy, BeatLeaderZeroReason.NonFinite);
            }
        }

        public BeatLeaderPpResult EvaluateLive(double relativeAccuracy)
        {
            if (double.IsNaN(relativeAccuracy) || double.IsInfinity(relativeAccuracy))
            {
                return Evaluate(double.NaN);
            }

            return Evaluate(PpNumerics.Clamp(relativeAccuracy, 0, 1));
        }

        private BeatLeaderPpResult EvaluateV4(double accuracy)
        {
            double cv = Curve?.ValueAt(accuracy) ?? 0;
            if (CurvePower != 1 && cv > 0 && Curve96 > 0)
            {
                cv = Curve96 * Math.Pow(cv / Curve96, CurvePower);
            }

            double p = _passPpConstant;
            double a = BeatLeaderPpFormulas.AccPp(cv, AccRating * ModifierMultiplier);
            double t = BeatLeaderPpFormulas.TechPp(accuracy, TechRating * ModifierMultiplier);
            return Combine(p, a, t, C.PpMultiplier * (p + a + t) * LowStarMultiplier);
        }

        private BeatLeaderPpResult EvaluateLegacy(double accuracy)
        {
            double p = BeatLeaderPpFormulas.LegacyPassPp(PassRating * ModifierMultiplier);
            double a = BeatLeaderPpFormulas.LegacyAccPp(accuracy, AccRating * ModifierMultiplier);
            double t = BeatLeaderPpFormulas.LegacyTechPp(accuracy, TechRating * ModifierMultiplier);
            return Combine(p, a, t, BeatLeaderPpFormulas.LegacyInflate(p + a + t));
        }

        private BeatLeaderPpResult Combine(double p, double a, double t, double total)
        {
            double sum = p + a + t;
            bool finite = PpNumerics.IsFinite(total);
            if (!finite) total = 0;
            double k = sum > 0 ? total / sum : 0;
            var reason = finite ? BeatLeaderZeroReason.None : BeatLeaderZeroReason.NonFinite;
            return new BeatLeaderPpResult(total, p * k, a * k, t * k, Stars, IsApproximate, IsLegacy, reason);
        }

        internal static string NormalizeModifiers(string? modifiers)
        {
            if (string.IsNullOrEmpty(modifiers)) return string.Empty;
            var chars = new char[modifiers!.Length];
            int n = 0;
            bool pendingSeparator = false;
            foreach (char ch in modifiers)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    if (pendingSeparator && n > 0) chars[n++] = ',';
                    pendingSeparator = false;
                    chars[n++] = char.ToUpperInvariant(ch);
                }
                else
                {
                    pendingSeparator = true;
                }
            }

            return new string(chars, 0, n);
        }

        private static Precomputed Precompute(BeatLeaderDifficulty d, string mods)
        {
            var p = new Precomputed
            {
                IsNoFail = mods.IndexOf("NF", StringComparison.Ordinal) >= 0,
                IsRhythmGameStandard = string.Equals(d.ModeName?.Trim(), C.RhythmGameStandardModeName, StringComparison.OrdinalIgnoreCase),
                IsLegacy = !d.NoteSpan.HasValue,
                RatingSet = string.Empty,
                Acc = PpNumerics.FiniteOr(d.AccRating, 0),
                Pass = PpNumerics.FiniteOr(d.PassRating, 0),
                Tech = PpNumerics.FiniteOr(d.TechRating, 0),
                ApiStars = d.Stars,
            };

            bool speedRated = d.ModifiersRating != null;
            p.Mp = BeatLeaderPpFormulas.ModifierMultiplier(d.ModifierValues, mods, speedRated);
            p.DisplayMp = p.IsNoFail
                ? BeatLeaderPpFormulas.ModifierMultiplier(d.ModifierValues, WithoutCode(mods, "NF"), speedRated)
                : p.Mp;

            double? pa = d.PredictedAcc;
            double? slider = d.SliderPercentage;
            double? span = d.NoteSpan;

            if (speedRated && mods.Length > 0)
            {
                foreach (string code in mods.Split(','))
                {
                    if (code.Length == 0 || !d.HasModifierRating(code, "AccRating")) continue;
                    if (C.SpeedTimescales.TryGetValue(code, out double timescale))
                    {
                        pa = d.GetModifierRating(code, "PredictedAcc");
                        slider = d.GetModifierRating(code, "SliderPercentage");
                        span = PpNumerics.FiniteOr(d.NoteSpan, 0) / timescale;
                    }

                    p.Acc = PpNumerics.FiniteOr(d.GetModifierRating(code, "AccRating"), 0);
                    p.Pass = PpNumerics.FiniteOr(d.GetModifierRating(code, "PassRating"), 0);
                    p.Tech = PpNumerics.FiniteOr(d.GetModifierRating(code, "TechRating"), 0);
                    p.ApiStars = d.GetModifierRating(code, "Stars");
                    p.RatingSet = code;
                    break;
                }
            }

            if (p.IsLegacy)
            {
                p.Stars = LegacyStars(p.Acc, p.Pass, p.Tech);
                p.CurvePower = 1;
                p.Lsm = 1;
                return p;
            }

            p.Curve = BeatLeaderAccCurve.Build(pa, slider, d.Notes, span);
            p.Stars = BeatLeaderPpFormulas.Stars(p.Acc, p.Pass, p.Tech, p.Curve);
            p.C96 = p.Curve.ValueAt(C.StarAccuracy);
            p.CurvePower = BeatLeaderPpFormulas.CurvePower(p.Stars);
            p.Lsm = BeatLeaderPpFormulas.LowStarMultiplier(p.Stars);
            p.PassPp = BeatLeaderPpFormulas.PassPp(p.Pass * p.Mp);
            p.Drift = p.ApiStars.HasValue && PpNumerics.IsFinite(p.ApiStars.Value)
                      && Math.Abs(p.Stars - p.ApiStars.Value) > C.DriftTolerance;
            return p;
        }

        internal static double LegacyStars(double acc, double pass, double tech)
        {
            double sum96 = BeatLeaderPpFormulas.LegacyPassPp(pass)
                           + BeatLeaderPpFormulas.LegacyAccPp(C.StarAccuracy, acc)
                           + BeatLeaderPpFormulas.LegacyTechPp(C.StarAccuracy, tech);
            return BeatLeaderPpFormulas.LegacyInflate(sum96) / C.LegacyStarDivisor;
        }

        private static string WithoutCode(string mods, string code) =>
            string.Join(",", mods.Split(',').Where(part => !string.Equals(part.Trim(), code, StringComparison.Ordinal)));

        private sealed class Precomputed
        {
            public bool IsNoFail;
            public bool IsRhythmGameStandard;
            public bool IsLegacy;
            public string RatingSet = string.Empty;
            public double Acc, Pass, Tech, Mp = 1, DisplayMp = 1;
            public BeatLeaderAccCurve? Curve;
            public double Stars, CurvePower = 1, Lsm = 1, C96, PassPp;
            public double? ApiStars;
            public bool Drift;
        }
    }
}
