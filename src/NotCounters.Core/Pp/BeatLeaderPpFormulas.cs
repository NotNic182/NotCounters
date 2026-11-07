using System;
using System.Collections.Generic;
using C = NotCounters.Core.Pp.BeatLeaderPpConstants;

namespace NotCounters.Core.Pp
{
    public static class BeatLeaderPpFormulas
    {
        public static double LerpDescending(IReadOnlyList<CurvePoint>? points, double x)
        {
            if (points == null || points.Count == 0) return 0;
            int n = points.Count;
            if (n == 1) return points[0].Y;

            int lo = 0, hi = n;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (points[mid].X <= x) hi = mid;
                else lo = mid + 1;
            }

            int i = lo;
            if (i == 0) i = 1;
            if (i >= n) i = n - 1;
            CurvePoint upper = points[i - 1];
            CurvePoint lower = points[i];
            double dx = lower.X - upper.X;
            if (dx == 0) return upper.Y;
            return upper.Y + (x - upper.X) / dx * (lower.Y - upper.Y);
        }

        public static double NoteFactor(double notes)
        {
            if (!(notes > 0) || notes >= C.LowNoteStart) return 1;
            double f = 1 - (1 - C.LowNoteFloor) * Math.Log(C.LowNoteStart / notes) / Math.Log(C.LowNoteStart / C.LowNoteReference);
            return PpNumerics.Clamp(f, C.LowNoteFloor, 1);
        }

        public static double SpanFactor(double noteSpanSeconds)
        {
            if (!(noteSpanSeconds > 0) || noteSpanSeconds >= C.ShortMapStart) return 1;
            double f = 1 - C.ShortMapNerf * Math.Log(C.ShortMapStart / noteSpanSeconds) / Math.Log(C.ShortMapStart / C.ShortMapReference);
            return PpNumerics.Clamp(f, C.ShortMapFloor, 1);
        }

        public static double LowStarMultiplier(double stars)
        {
            if (double.IsNaN(stars) || stars >= C.LowStarBonusEnd) return 1;
            return 1 + C.LowStarBonus * Math.Pow(C.LowStarBonusEnd - Math.Max(stars, 0), C.LowStarBonusPower);
        }

        public static double CurvePower(double stars)
        {
            if (double.IsNaN(stars)) return C.CurvePowerMid;
            if (stars <= C.CurvePowerLowEnd) return C.CurvePowerLow;
            if (stars < C.CurvePowerMidStart)
            {
                return C.CurvePowerLow + (C.CurvePowerMid - C.CurvePowerLow) * (stars - C.CurvePowerLowEnd) / (C.CurvePowerMidStart - C.CurvePowerLowEnd);
            }

            if (stars <= C.CurvePowerMidEnd) return C.CurvePowerMid;
            if (stars < C.CurvePowerHighStart)
            {
                return C.CurvePowerMid + (C.CurvePowerHigh - C.CurvePowerMid) * (stars - C.CurvePowerMidEnd) / (C.CurvePowerHighStart - C.CurvePowerMidEnd);
            }

            return C.CurvePowerHigh;
        }

        public static double PassPp(double passRating)
        {
            double p = passRating;
            if (p > C.PassTopKnee) p = C.PassTopKnee + (p - C.PassTopKnee) * C.PassTopSlope;
            if (p < 0) return 0;
            double r = C.PassScale * Math.Exp(C.PassInner * Math.Pow(p, 1 / C.PassExponent)) - C.PassOffset;
            return PpNumerics.IsFinite(r) && r >= 0 ? r : 0;
        }

        public static double PassAccMultiplier(double accuracy, double predictedAcc)
        {
            if (!(predictedAcc > 0)) return 1;
            double gap = accuracy - (predictedAcc - C.PassAccErrorOffset * (1 - predictedAcc));
            return PpNumerics.Clamp(Math.Exp(C.PassAccExponent * gap), C.PassAccMinMultiplier, C.PassAccMaxMultiplier);
        }

        public static double AccPp(double curveValue, double accRating)
        {
            if (!(accRating > 0)) return 0;
            return PpNumerics.FiniteOr(curveValue * C.AccWeight * accRating, 0);
        }

        public static double MidStarMultiplier(double stars, double accuracy, double predictedAcc)
        {
            if (!(predictedAcc > 0)) return 1;
            double from = predictedAcc - C.MidStarAccBelow;
            if (!(accuracy > from)) return 1;
            double bump = MidStarBump(stars);
            if (bump == 0) return 1;
            return 1 + C.MidStarBonus * bump * Ease((accuracy - from) / (C.MidStarAccBelow + C.MidStarAccAbove));
        }

        public static double MidStarBump(double stars)
        {
            if (!(stars > C.MidStarStart && stars < C.MidStarEnd)) return 0;
            if (stars < C.MidStarFull) return Ease((stars - C.MidStarStart) / (C.MidStarFull - C.MidStarStart));
            if (stars > C.MidStarFullEnd) return Ease((C.MidStarEnd - stars) / (C.MidStarEnd - C.MidStarFullEnd));
            return 1;
        }

        public static double TechPp(double techRating)
        {
            if (!(techRating > 0)) return 0;
            double t = techRating;
            if (t > C.TechTopKnee) t = C.TechTopKnee + (t - C.TechTopKnee) * C.TechTopSlope;
            return PpNumerics.FiniteOr(C.TechWeight * t, 0);
        }

        public static double RawStars(double pp96) => pp96 / C.StarDivisor;

        public static double StarsFromPp(double pp96)
        {
            if (double.IsNaN(pp96)) return 0;
            double s = RawStars(pp96);
            if (!(pp96 > 0) || s >= C.LowStarBonusEnd || !PpNumerics.IsFinite(s)) return PpNumerics.FiniteOr(s, 0);
            double lo = s, hi = C.LowStarBonusEnd;
            for (int guard = 0; hi - lo > C.StarBisectionTolerance && guard < 200; guard++)
            {
                double mid = (lo + hi) / 2;
                if (mid < RawStars(pp96 * LowStarMultiplier(mid))) lo = mid;
                else hi = mid;
            }

            return (lo + hi) / 2;
        }

        public static double Stars(double accRating, double passRating, double techRating, BeatLeaderAccCurve? curve, double predictedAcc)
        {
            double c96 = curve?.ValueAt(C.StarAccuracy) ?? 0;
            double pp96 = PassPp(passRating) * PassAccMultiplier(C.StarAccuracy, predictedAcc) + AccPp(c96, accRating) + TechPp(techRating);
            return StarsFromPp(pp96);
        }

        public static double ModifierMultiplier(IReadOnlyDictionary<string, double>? modifierValues, string? modifiers, bool speedRated)
        {
            string upper = (modifiers ?? string.Empty).ToUpperInvariant();
            double mp = 1;
            if (modifierValues == null || upper.Length == 0) return mp;
            foreach (string code in C.ModifierOrder)
            {
                if (speedRated && C.SpeedTimescales.ContainsKey(code)) continue;
                if (upper.IndexOf(code, StringComparison.Ordinal) >= 0)
                {
                    mp += TryGetModifierValue(modifierValues, code);
                }
            }

            return mp;
        }

        public static double LegacyPassPp(double passRating)
        {
            double r = C.LegacyPassScale * Math.Exp(Math.Pow(passRating, 1 / C.LegacyPassExponent)) - C.LegacyPassOffset;
            return PpNumerics.IsFinite(r) && r >= 0 ? r : 0;
        }

        public static double LegacyAccPp(double accuracy, double accRating)
        {
            if (!(accRating > 0)) return 0;
            double r = LerpDescending(C.LegacyTable, accuracy) * accRating * C.LegacyAccWeight;
            return PpNumerics.IsFinite(r) && r > 0 ? r : 0;
        }

        public static double LegacyTechPp(double accuracy, double techRating)
        {
            if (!(techRating > 0)) return 0;
            return PpNumerics.FiniteOr(Math.Exp(C.LegacyTechAccExponent * accuracy) * C.LegacyTechWeight * techRating, 0);
        }

        public static double LegacyInflate(double sum)
        {
            double r = C.LegacyInflationBase * Math.Pow(sum, C.LegacyInflationPower) / Math.Pow(C.LegacyInflationBase, C.LegacyInflationPower);
            return PpNumerics.FiniteOr(r, 0);
        }

        private static double Ease(double t)
        {
            double c = PpNumerics.Clamp(t, 0, 1);
            return c * c * (3 - 2 * c);
        }

        internal static double TryGetModifierValue(IReadOnlyDictionary<string, double> values, string code)
        {
            if (values.TryGetValue(code.ToLowerInvariant(), out double v) || values.TryGetValue(code, out v))
            {
                return PpNumerics.FiniteOr(v, 0);
            }

            return 0;
        }
    }
}
