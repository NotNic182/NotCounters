using System;
using System.Collections.Generic;
using C = NotCounters.Core.Pp.BeatLeaderPpConstants;

namespace NotCounters.Core.Pp
{
    public sealed class BeatLeaderAccCurve
    {
        private static readonly BeatLeaderCurveShape StandardShape = new BeatLeaderCurveShape(
            C.ShapeA, C.ShapeC, C.ShapeK, C.ShapeW, C.ShapeK2, C.ShapeP2);

        private readonly CurvePoint[] _points;

        private BeatLeaderAccCurve(CurvePoint[] points, bool isShaped, double tilt, double nerf)
        {
            _points = points;
            IsShaped = isShaped;
            Tilt = tilt;
            NerfFactor = nerf;
            Points = Array.AsReadOnly(points);
        }

        public IReadOnlyList<CurvePoint> Points { get; }

        public bool IsShaped { get; }

        public double Tilt { get; }

        public double NerfFactor { get; }

        public bool IsPlainBaseTable => !IsShaped && Tilt == 0;

        public double ValueAt(double accuracy) => BeatLeaderPpFormulas.LerpDescending(Points, accuracy);

        public static BeatLeaderAccCurve Build(double? predictedAcc, double? sliderPercentage, int notes, double? noteSpanSeconds)
        {
            double pa = PpNumerics.FiniteOr(predictedAcc, 0);
            double slider = PpNumerics.FiniteOr(sliderPercentage, 0);
            double span = PpNumerics.FiniteOr(noteSpanSeconds, 0);

            double nerf = BeatLeaderPpFormulas.NoteFactor(notes) * BeatLeaderPpFormulas.SpanFactor(span);
            bool inRange = pa > C.PerMapMinPredictedAcc && pa < C.PerMapMaxPredictedAcc;

            BeatLeaderCurveShape? shape = null;
            if (inRange)
            {
                CurveParameterRow r = ParametersAt(pa);
                bool isDefaultRow = r.A >= C.DefaultRowAThreshold && r.K == C.ShapeK && r.W == C.ShapeW
                                    && r.K2 == C.ShapeK2 && r.P2 == C.ShapeP2;
                if (!isDefaultRow)
                {
                    shape = new BeatLeaderCurveShape(PpNumerics.Clamp(r.A, C.ShapeAMin, C.ShapeAMax), r.C, r.K, r.W, r.K2, r.P2);
                }
            }

            double tilt = inRange ? C.SliderTilt * slider : 0;

            if (shape == null && tilt == 0)
            {
                var plain = new CurvePoint[C.BaseTable.Count];
                for (int i = 0; i < plain.Length; i++)
                {
                    plain[i] = new CurvePoint(C.BaseTable[i].X, C.BaseTable[i].Y * nerf);
                }

                return new BeatLeaderAccCurve(plain, false, 0, nerf);
            }

            List<double> grid = BuildGrid(shape?.A ?? 1.0);
            var points = new CurvePoint[grid.Count];
            for (int i = 0; i < grid.Count; i++)
            {
                double x = grid[i];
                double v;
                if (shape != null)
                {
                    double sb = BeatLeaderSmoothBase.ValueAt(x);
                    double reference = StandardShape.ValueAt(x);
                    v = reference > 0 ? Math.Min(sb, sb * shape.ValueAt(x) / reference) : sb;
                }
                else
                {
                    v = BeatLeaderPpFormulas.LerpDescending(C.BaseTable, x);
                }

                if (tilt != 0)
                {
                    v *= Math.Exp(tilt * C.SliderTiltScale * PpNumerics.Clamp(x - pa, -C.SliderTiltRange, C.SliderTiltRange));
                }

                points[i] = new CurvePoint(x, PpNumerics.FiniteOr(v * nerf, 0));
            }

            return new BeatLeaderAccCurve(points, shape != null, tilt, nerf);
        }

        public static CurveParameterRow ParametersAt(double predictedAcc)
        {
            IReadOnlyList<CurveParameterRow> rows = C.ParameterRows;
            CurveParameterRow r;
            if (!(predictedAcc > rows[0].PredictedAcc))
            {
                r = rows[0];
            }
            else if (predictedAcc > rows[rows.Count - 1].PredictedAcc)
            {
                r = rows[rows.Count - 1];
            }
            else
            {
                int k = 1;
                while (k < rows.Count - 1 && !(predictedAcc <= rows[k].PredictedAcc)) k++;
                CurveParameterRow a = rows[k - 1], b = rows[k];
                double t = (predictedAcc - a.PredictedAcc) / (b.PredictedAcc - a.PredictedAcc);
                r = new CurveParameterRow(
                    predictedAcc,
                    a.A + t * (b.A - a.A),
                    a.C + t * (b.C - a.C),
                    a.K + t * (b.K - a.K),
                    a.W + t * (b.W - a.W),
                    a.K2 + t * (b.K2 - a.K2),
                    a.P2 + t * (b.P2 - a.P2));
            }

            return new CurveParameterRow(r.PredictedAcc, r.A, Math.Min(r.C, C.ShapeCMax), r.K, r.W, r.K2, r.P2);
        }

        private static List<double> BuildGrid(double shapeA)
        {
            var set = new HashSet<double>();
            foreach (CurvePoint p in C.BaseTable) set.Add(Round(p.X));
            for (int i = 0; i < C.GridCoarseCount; i++) set.Add(Round(C.GridCoarseStart + C.GridCoarseStep * i));
            for (int i = 0; i < C.GridFineCount; i++) set.Add(Round(C.GridFineStart + C.GridFineStep * i));
            set.Add(Round(shapeA));

            var grid = new List<double>(set.Count);
            foreach (double x in set)
            {
                if (x >= 0 && x <= 1) grid.Add(x);
            }

            grid.Sort((a, b) => b.CompareTo(a));
            return grid;
        }

        private static double Round(double x) => Math.Round(x, C.GridRoundingDigits, MidpointRounding.AwayFromZero);
    }

    internal sealed class BeatLeaderCurveShape
    {
        private readonly double _c, _k, _w, _k2, _p2, _y0;
        private readonly double _m, _r, _expKMinus1, _expK2Minus1;

        public BeatLeaderCurveShape(double a, double c, double k, double w, double k2, double p2, double y0 = C.ShapeY0)
        {
            A = a;
            _c = c;
            _k = k;
            _w = w;
            _k2 = k2;
            _p2 = p2;
            _y0 = y0;
            double expK = Math.Exp(k);
            _expKMinus1 = expK - 1;
            _expK2Minus1 = Math.Exp(k2) - 1;
            _m = (1 - y0) / C.ShapeMidSpan * ((1 - w) + w * k * expK / _expKMinus1);
            _r = C.ShapeTopGain - C.ShapeTopSpan * _m;
        }

        public double A { get; }

        public double ValueAt(double x)
        {
            if (x <= C.ShapeLowX) return _y0 / C.ShapeLowX * x;
            if (x <= C.ShapeKneeX)
            {
                double u = (x - C.ShapeLowX) / C.ShapeMidSpan;
                return _y0 + (1 - _y0) * ((1 - _w) * u + _w * (Math.Exp(_k * u) - 1) / _expKMinus1);
            }

            if (x <= A) return F2(x);

            double uA = (A - C.ShapeKneeX) / C.ShapeTopSpan;
            double pw = uA > 0 ? Math.Pow(uA, _p2) : 0;
            double pw1 = uA > 0 ? Math.Pow(uA, _p2 - 1) : 0;
            double s = _m + _r * Math.Exp(_k2 * pw) / _expK2Minus1 * (_k2 * _p2 / C.ShapeTopSpan) * pw1;
            double s1 = s * (1 - _c);
            double v = F2(A) + s1 * (x - A);
            if (Math.Abs(A - 1) >= C.ShapeAEpsilon)
            {
                v += (s - s1) * (1 - A) * (1 - Math.Exp(-(x - A) / (1 - A)));
            }

            return v;
        }

        private double F2(double x)
        {
            double u = (x - C.ShapeKneeX) / C.ShapeTopSpan;
            return 1 + _m * (x - C.ShapeKneeX) + _r * (Math.Exp(_k2 * Math.Pow(u, _p2)) - 1) / _expK2Minus1;
        }
    }

    internal static class BeatLeaderSmoothBase
    {
        private static readonly double[] Xs;
        private static readonly double[] Ys;
        private static readonly double[] H;
        private static readonly double[] M;

        static BeatLeaderSmoothBase()
        {
            IReadOnlyList<CurvePoint> table = C.BaseTable;
            int n = table.Count;
            Xs = new double[n];
            Ys = new double[n];
            for (int i = 0; i < n; i++)
            {
                Xs[i] = table[n - 1 - i].X;
                Ys[i] = table[n - 1 - i].Y;
            }

            H = new double[n - 1];
            var d = new double[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                H[i] = Xs[i + 1] - Xs[i];
                d[i] = (Ys[i + 1] - Ys[i]) / H[i];
            }

            M = new double[n];
            M[0] = d[0];
            M[n - 1] = d[n - 2];
            for (int i = 1; i < n - 1; i++)
            {
                if (d[i - 1] * d[i] <= 0)
                {
                    M[i] = 0;
                }
                else
                {
                    double w1 = 2 * H[i] + H[i - 1];
                    double w2 = H[i] + 2 * H[i - 1];
                    M[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i]);
                }
            }
        }

        public static double ValueAt(double a)
        {
            int n = Xs.Length;
            if (double.IsNaN(a)) return 0;
            if (a <= Xs[0]) return Ys[0];
            if (a >= Xs[n - 1]) return Ys[n - 1];
            int i = 0;
            while (Xs[i + 1] < a) i++;
            double h = H[i];
            double t = (a - Xs[i]) / h;
            double t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * Ys[i]
                   + (t3 - 2 * t2 + t) * h * M[i]
                   + (-2 * t3 + 3 * t2) * Ys[i + 1]
                   + (t3 - t2) * h * M[i + 1];
        }
    }
}
