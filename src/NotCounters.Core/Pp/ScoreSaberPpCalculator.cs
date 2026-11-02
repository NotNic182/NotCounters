using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NotCounters.Core.Pp
{
    public enum ScoreSaberMaxPpSource
    {
        None,
        BackSolved,
        Stars,
        V1,
    }

    public sealed class ScoreSaberMaxPpResult
    {
        internal ScoreSaberMaxPpResult(double maxPp, ScoreSaberMaxPpSource source, int sampleCount,
            double? backSolvedValue, double starsMaxPp, bool sanityCheckFailed, string? note)
        {
            MaxPp = maxPp;
            Source = source;
            SampleCount = sampleCount;
            BackSolvedValue = backSolvedValue;
            StarsMaxPp = starsMaxPp;
            SanityCheckFailed = sanityCheckFailed;
            Note = note;
        }

        public double MaxPp { get; }

        public ScoreSaberMaxPpSource Source { get; }

        public int SampleCount { get; }

        public double? BackSolvedValue { get; }

        public double StarsMaxPp { get; }

        public bool SanityCheckFailed { get; }

        public bool IsApproximate => Source != ScoreSaberMaxPpSource.BackSolved;

        public string? Note { get; }
    }

    public static class ScoreSaberPpCalculator
    {
        public const double PpPerStar = 450 / 10.685333512;

        public const double MaxPpSanityTolerance = 0.002;

        public const double DecayFactor = 0.965;

        public static readonly IReadOnlyDictionary<string, double> DefaultModifierValues =
            new ReadOnlyDictionary<string, double>(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["NO"] = -0.05,
                ["NB"] = -0.10,
                ["NF"] = -0.50,
                ["SS"] = -0.30,
                ["NA"] = -0.30,
                ["FS"] = 0.08,
                ["DA"] = 0.02,
                ["GN"] = 0.04,
            });

        public static double Multiplier(IEnumerable<string>? codes, bool positiveAllowed, IReadOnlyDictionary<string, double>? values = null)
        {
            IReadOnlyDictionary<string, double> table = values ?? DefaultModifierValues;
            double m = 1;
            foreach (string code in DistinctCodes(codes))
            {
                double v = Lookup(table, code);
                if (v > 0 && !positiveAllowed) continue;
                m += v;
            }

            return m;
        }

        public static long ModifiedScore(long baseScore, IEnumerable<string>? codes, bool positiveAllowed, IReadOnlyDictionary<string, double>? values = null)
        {
            double v = Math.Floor(baseScore * Multiplier(codes, positiveAllowed, values) + 0.5);
            if (!PpNumerics.IsFinite(v)) return 0;
            if (v >= long.MaxValue) return long.MaxValue;
            if (v <= long.MinValue) return long.MinValue;
            return (long)v;
        }

        public static long ModifiedScore(long baseScore, IEnumerable<string>? codes, ScoreSaberLeaderboard? leaderboard) =>
            ModifiedScore(baseScore, codes, leaderboard?.PositiveModifiers ?? false, leaderboard?.ModifierValues);

        public static double? Pp(long baseScore, IEnumerable<string>? codes, ScoreSaberPlayOutcome outcome,
            ScoreSaberLeaderboard? leaderboard, ScoreSaberCurves? curves = null, double? maxPp = null, bool includeQualified = false)
        {
            if (!TryGate(leaderboard, maxPp, includeQualified, out double resolvedMaxPp)) return null;
            if (outcome != ScoreSaberPlayOutcome.Clear) return 0;
            ScoreSaberLeaderboard lb = leaderboard!;
            double x = ModifiedScore(baseScore, codes, lb) / (double)lb.MaxScore;
            return Finite(resolvedMaxPp * CurveFor(lb, curves).Multiplier(x));
        }

        public static double? LivePp(double relativeAccuracy, IEnumerable<string>? codes, ScoreSaberLeaderboard? leaderboard,
            ScoreSaberCurves? curves = null, double? maxPp = null, bool includeQualified = false)
        {
            if (!PpNumerics.IsFinite(relativeAccuracy)) return null;
            if (!TryGate(leaderboard, maxPp, includeQualified, out double resolvedMaxPp)) return null;
            ScoreSaberLeaderboard lb = leaderboard!;
            return LivePpCore(relativeAccuracy, Multiplier(codes, lb.PositiveModifiers, lb.ModifierValues), lb, curves, resolvedMaxPp);
        }

        public static double? LivePpWithMultiplier(double relativeAccuracy, double modifierMultiplier, ScoreSaberLeaderboard? leaderboard,
            ScoreSaberCurves? curves = null, double? maxPp = null, bool includeQualified = false)
        {
            if (!PpNumerics.IsFinite(relativeAccuracy)) return null;
            if (!TryGate(leaderboard, maxPp, includeQualified, out double resolvedMaxPp)) return null;
            return LivePpCore(relativeAccuracy, modifierMultiplier, leaderboard!, curves, resolvedMaxPp);
        }

        public static double? ResolveMaxPp(ScoreSaberLeaderboard? leaderboard, double? explicitMaxPp = null)
        {
            if (explicitMaxPp.HasValue)
            {
                return PpNumerics.IsFinite(explicitMaxPp.Value) && explicitMaxPp.Value > 0 ? explicitMaxPp : null;
            }

            if (leaderboard == null) return null;
            if (leaderboard.MaxPp is double exact && PpNumerics.IsFinite(exact) && exact > 0) return exact;
            double fromStars = MaxPpFromStars(leaderboard.Stars);
            if (fromStars > 0) return fromStars;
            if (leaderboard.V1MaxPp is double v1 && PpNumerics.IsFinite(v1) && v1 > 0) return v1;
            return null;
        }

        public static double MaxPpFromStars(double stars)
        {
            if (!PpNumerics.IsFinite(stars) || stars <= 0) return 0;
            return PpNumerics.RoundHalfUp(stars * PpPerStar, 2);
        }

        public static ScoreSaberMaxPpResult BackSolveMaxPp(IEnumerable<ScoreSaberScoreSample>? scores,
            ScoreSaberLeaderboard? leaderboard, ScoreSaberCurves? curves = null)
        {
            if (leaderboard == null)
            {
                return new ScoreSaberMaxPpResult(0, ScoreSaberMaxPpSource.None, 0, null, 0, false, "No leaderboard data.");
            }

            double starsPp = MaxPpFromStars(leaderboard.Stars);
            double reference = starsPp > 0 ? starsPp : PpNumerics.FiniteOr(leaderboard.V1MaxPp, 0);

            var ks = new List<double>();
            if (scores != null && leaderboard.MaxScore > 0)
            {
                PpCurve curve = CurveFor(leaderboard, curves);
                foreach (ScoreSaberScoreSample? s in scores)
                {
                    if (s == null || !(s.Pp > 0) || !PpNumerics.IsFinite(s.Pp) || s.Outcome != ScoreSaberPlayOutcome.Clear) continue;
                    double m = curve.Multiplier(s.ModifiedScore / (double)leaderboard.MaxScore);
                    if (!(m > 0)) continue;
                    double k = s.Pp / m;
                    if (PpNumerics.IsFinite(k)) ks.Add(k);
                }
            }

            if (ks.Count == 0)
            {
                return Fallback(leaderboard, starsPp, 0, null, false, "No usable scores to back-solve maxPP; using stars.");
            }

            double median = PpNumerics.Median(ks);
            if (reference > 0 && Math.Abs(median - reference) / reference <= MaxPpSanityTolerance)
            {
                return new ScoreSaberMaxPpResult(median, ScoreSaberMaxPpSource.BackSolved, ks.Count, median, starsPp, false, null);
            }

            return Fallback(leaderboard, starsPp, ks.Count, median, true,
                "Back-solved maxPP failed the 0.2% sanity check against stars; using stars.");
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
                weight *= DecayFactor;
            }

            return total;
        }

        private static ScoreSaberMaxPpResult Fallback(ScoreSaberLeaderboard lb, double starsPp, int count, double? median, bool sanityFailed, string note)
        {
            if (starsPp > 0)
            {
                return new ScoreSaberMaxPpResult(starsPp, ScoreSaberMaxPpSource.Stars, count, median, starsPp, sanityFailed, note);
            }

            if (lb.V1MaxPp is double v1 && PpNumerics.IsFinite(v1) && v1 > 0)
            {
                return new ScoreSaberMaxPpResult(v1, ScoreSaberMaxPpSource.V1, count, median, starsPp, sanityFailed, note);
            }

            return new ScoreSaberMaxPpResult(0, ScoreSaberMaxPpSource.None, count, median, starsPp, sanityFailed,
                "No stars or maxPP available.");
        }

        private static bool TryGate(ScoreSaberLeaderboard? lb, double? maxPp, bool includeQualified, out double resolved)
        {
            resolved = 0;
            if (lb == null || lb.MaxScore <= 0) return false;
            bool statusOk = lb.Status == ScoreSaberLeaderboardStatus.Ranked
                            || (includeQualified && lb.Status == ScoreSaberLeaderboardStatus.Qualified && lb.Stars > 0);
            if (!statusOk) return false;
            double? r = ResolveMaxPp(lb, maxPp);
            if (!r.HasValue || !(r.Value > 0)) return false;
            resolved = r.Value;
            return true;
        }

        private static double? LivePpCore(double relativeAccuracy, double multiplier, ScoreSaberLeaderboard lb, ScoreSaberCurves? curves, double resolvedMaxPp)
        {
            double rel = PpNumerics.Clamp(relativeAccuracy, 0, 1);
            double x = rel * multiplier;
            return Finite(resolvedMaxPp * CurveFor(lb, curves).Multiplier(x));
        }

        private static PpCurve CurveFor(ScoreSaberLeaderboard lb, ScoreSaberCurves? curves)
        {
            ScoreSaberCurves chosen = curves != null && curves.IsUsable ? curves : ScoreSaberCurves.Snapshot;
            return chosen.For(lb.PositiveModifiers);
        }

        private static double? Finite(double v) => PpNumerics.IsFinite(v) ? v : 0;

        private static double Lookup(IReadOnlyDictionary<string, double> table, string code)
        {
            if (table.TryGetValue(code, out double v) || table.TryGetValue(code.ToLowerInvariant(), out v))
            {
                return PpNumerics.FiniteOr(v, 0);
            }

            return 0;
        }

        private static IEnumerable<string> DistinctCodes(IEnumerable<string>? codes)
        {
            if (codes == null) yield break;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string? raw in codes)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string code = raw!.Trim().ToUpperInvariant();
                if (seen.Add(code)) yield return code;
            }
        }
    }
}
