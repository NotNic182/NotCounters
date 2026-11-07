using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NotCounters.Core.Pp
{
    public readonly struct CurveParameterRow
    {
        public CurveParameterRow(double predictedAcc, double a, double c, double k, double w, double k2, double p2)
        {
            PredictedAcc = predictedAcc;
            A = a;
            C = c;
            K = k;
            W = w;
            K2 = k2;
            P2 = p2;
        }

        public double PredictedAcc { get; }

        public double A { get; }

        public double C { get; }

        public double K { get; }

        public double W { get; }

        public double K2 { get; }

        public double P2 { get; }
    }

    public static class BeatLeaderPpConstants
    {
        public const string SnapshotDate = "2026-11-05";

        public const double PpMultiplier = 1.0128;

        public const double PassScale = 12.3;
        public const double PassInner = 0.85966;
        public const double PassExponent = 2.62;
        public const double PassOffset = 30;
        public const double PassTopKnee = 13;
        public const double PassTopSlope = 0.5;

        public const double PassAccExponent = 6;
        public const double PassAccMaxMultiplier = 3;
        public const double PassAccMinMultiplier = 0.8;
        public const double PassAccErrorOffset = 1;

        public const double AccWeight = 34.519;

        public const double TechWeight = 5.7887;
        public const double TechTopKnee = 6.2754;
        public const double TechTopSlope = 0.7;

        public const double CurvePowerLow = 1.08;
        public const double CurvePowerMid = 1.0;
        public const double CurvePowerHigh = 0.9;
        public const double CurvePowerLowEnd = 3.478;
        public const double CurvePowerMidStart = 5.3718;
        public const double CurvePowerMidEnd = 7.1529;
        public const double CurvePowerHighStart = 9.6894;

        public const double StarDivisor = 52;
        public const double StarAccuracy = 0.96;
        public const double StarBisectionTolerance = 1e-6;

        public const double LowStarBonus = 0.007593;
        public const double LowStarBonusEnd = 8.8584;
        public const double LowStarBonusPower = 1.98;

        public const double MidStarBonus = 0.05;
        public const double MidStarStart = 4.2508;
        public const double MidStarFull = 6.3762;
        public const double MidStarFullEnd = 8.0;
        public const double MidStarEnd = 10.5;
        public const double MidStarAccBelow = 0.03;
        public const double MidStarAccAbove = 0;

        public const double ShapeY0 = 0.256;
        public const double ShapeK = 4.86;
        public const double ShapeW = 0.6;
        public const double ShapeK2 = 2.88;
        public const double ShapeP2 = 2.46;
        public const double ShapeC = 1.0;
        public const double ShapeA = 1.0;
        public const double ShapeLowX = 0.6;
        public const double ShapeKneeX = 0.95;
        public const double ShapeMidSpan = 0.35;
        public const double ShapeTopSpan = 0.05;
        public const double ShapeTopGain = 6.424;
        public const double ShapeAEpsilon = 1e-9;

        public const double PerMapMinPredictedAcc = 0.6;
        public const double PerMapMaxPredictedAcc = 0.9995;
        public const double DefaultRowAThreshold = 0.9995;
        public const double ShapeAMin = 0.951;
        public const double ShapeAMax = 1.0;
        public const double ShapeCMax = 1.0;
        public const double TopFactorStep = 1e-5;

        public const double SliderTilt = 0.2;
        public const double SliderTiltRange = 0.015;
        public const double SliderTiltScale = 100;

        public const double LowNoteStart = 115;
        public const double LowNoteReference = 20;
        public const double LowNoteFloor = 0.8031;
        public const double ShortMapStart = 75;
        public const double ShortMapReference = 12;
        public const double ShortMapNerf = 0.10;
        public const double ShortMapFloor = 0.90;

        public const double SlowerSongTimescale = 0.85;
        public const double FasterSongTimescale = 1.2;
        public const double SuperFastSongTimescale = 1.5;

        public const double GridCoarseStart = 0.6;
        public const double GridCoarseStep = 0.005;
        public const int GridCoarseCount = 71;
        public const double GridFineStart = 0.95;
        public const double GridFineStep = 0.0005;
        public const int GridFineCount = 101;
        public const int GridRoundingDigits = 6;

        public const double ProfileWeight = 0.965;
        public const double RhythmGameStandardFactor = 55;
        public const string RhythmGameStandardModeName = "RhythmGameStandard";
        public const double DriftTolerance = 0.001;

        public const double LegacyPassScale = 15.2;
        public const double LegacyPassExponent = 2.62;
        public const double LegacyPassOffset = 30;
        public const double LegacyAccWeight = 34;
        public const double LegacyTechWeight = 1.08;
        public const double LegacyTechAccExponent = 1.9;
        public const double LegacyInflationBase = 650;
        public const double LegacyInflationPower = 1.3;
        public const double LegacyStarDivisor = 52;

        public static readonly IReadOnlyList<CurvePoint> BaseTable = Points(
            1.0, 7.2, 0.999, 6.241, 0.9975, 5.158, 0.995, 4.010, 0.9925, 3.241, 0.99, 2.700,
            0.9875, 2.303, 0.985, 2.007, 0.9825, 1.786, 0.98, 1.618, 0.9775, 1.490, 0.975, 1.392,
            0.9725, 1.315, 0.97, 1.256, 0.965, 1.167, 0.96, 1.094, 0.955, 1.039, 0.95, 1.000,
            0.94, 0.926, 0.93, 0.861, 0.92, 0.804, 0.91, 0.760, 0.9, 0.727, 0.875, 0.675,
            0.85, 0.627, 0.825, 0.584, 0.8, 0.549, 0.75, 0.469, 0.7, 0.400, 0.65, 0.343,
            0.6, 0.297, 0.0, 0.0);

        public static readonly IReadOnlyList<CurvePoint> LegacyTable = Points(
            1.0, 7.424, 0.999, 6.241, 0.9975, 5.158, 0.995, 4.010, 0.9925, 3.241, 0.99, 2.700,
            0.9875, 2.303, 0.985, 2.007, 0.9825, 1.786, 0.98, 1.618, 0.9775, 1.490, 0.975, 1.392,
            0.9725, 1.315, 0.97, 1.256, 0.965, 1.167, 0.96, 1.094, 0.955, 1.039, 0.95, 1.000,
            0.94, 0.931, 0.93, 0.867, 0.92, 0.813,
            0.91, 0.768, 0.9, 0.729, 0.875, 0.650, 0.85, 0.581, 0.825, 0.522, 0.8, 0.473,
            0.75, 0.404, 0.7, 0.345, 0.65, 0.296, 0.6, 0.256, 0.0, 0.0);

        public static readonly IReadOnlyList<CurveParameterRow> ParameterRows = Array.AsReadOnly(new[]
        {
            new CurveParameterRow(0.955, 1.0, -5.30, 4.86, 0.60, 2.88, 2.46),
            new CurveParameterRow(0.960, 0.99, -3.00, 4.30, 0.66, 4.80, 1.85),
            new CurveParameterRow(0.965, 0.988, -2.40, 4.50, 0.64, 4.50, 1.95),
            new CurveParameterRow(0.970, 0.986, -2.20, 4.70, 0.62, 4.20, 2.05),
            new CurveParameterRow(0.975, 0.983, 0, 4.86, 0.60, 3.80, 2.15),
            new CurveParameterRow(0.980, 0.983, 0, 4.86, 0.60, 3.35, 2.30),
            new CurveParameterRow(0.985, 0.985, -0.60, 4.86, 0.60, 2.88, 2.46),
            new CurveParameterRow(0.990, 0.99, 0, 4.86, 0.60, 2.88, 2.46),
            new CurveParameterRow(0.995, 0.99, 0, 4.86, 0.60, 2.88, 2.46),
        });

        public static readonly IReadOnlyList<string> ModifierOrder = Array.AsReadOnly(new[]
        {
            "DA", "FS", "SF", "SS", "GN", "NA", "NB", "NF", "NO", "PM", "SC", "SA", "OP", "EZ", "HD", "SMC", "OHP",
        });

        public static readonly IReadOnlyDictionary<string, double> SpeedTimescales =
            new ReadOnlyDictionary<string, double>(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["SS"] = SlowerSongTimescale,
                ["FS"] = FasterSongTimescale,
                ["SF"] = SuperFastSongTimescale,
            });

        private static IReadOnlyList<CurvePoint> Points(params double[] xy)
        {
            var points = new CurvePoint[xy.Length / 2];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = new CurvePoint(xy[2 * i], xy[2 * i + 1]);
            }

            return Array.AsReadOnly(points);
        }
    }
}
