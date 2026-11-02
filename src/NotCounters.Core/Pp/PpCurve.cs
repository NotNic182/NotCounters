using System;
using System.Collections.Generic;

namespace NotCounters.Core.Pp
{
    public sealed class PpCurve
    {
        private readonly CurvePoint[] _points;

        public PpCurve(IEnumerable<CurvePoint>? points)
        {
            var list = new List<CurvePoint>();
            if (points != null)
            {
                foreach (CurvePoint p in points)
                {
                    if (PpNumerics.IsFinite(p.X) && PpNumerics.IsFinite(p.Y)) list.Add(p);
                }
            }

            for (int i = 1; i < list.Count; i++)
            {
                CurvePoint item = list[i];
                int j = i - 1;
                while (j >= 0 && list[j].X < item.X)
                {
                    list[j + 1] = list[j];
                    j--;
                }

                list[j + 1] = item;
            }

            _points = list.ToArray();
            Points = Array.AsReadOnly(_points);
        }

        public IReadOnlyList<CurvePoint> Points { get; }

        public bool IsEmpty => _points.Length == 0;

        public double TopX => _points.Length > 0 ? _points[0].X : 0;

        public double Multiplier(double x)
        {
            CurvePoint[] c = _points;
            if (c.Length == 0 || double.IsNaN(x) || x <= 0) return 0;
            if (x >= c[0].X) return c[0].Y;
            for (int i = 0; i < c.Length - 1; i++)
            {
                CurvePoint hi = c[i];
                CurvePoint lo = c[i + 1];
                if (lo.X <= x && x <= hi.X)
                {
                    if (hi.X == lo.X) return hi.Y;
                    return lo.Y + (x - lo.X) / (hi.X - lo.X) * (hi.Y - lo.Y);
                }
            }

            return 0;
        }

        public bool Matches(PpCurve? other, double relativeTolerance = 1e-12)
        {
            if (other == null || other._points.Length != _points.Length) return false;
            for (int i = 0; i < _points.Length; i++)
            {
                if (!Close(_points[i].X, other._points[i].X, relativeTolerance) || !Close(_points[i].Y, other._points[i].Y, relativeTolerance))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Close(double a, double b, double tol)
        {
            if (a == b) return true;
            return Math.Abs(a - b) <= tol * Math.Max(Math.Abs(a), Math.Abs(b));
        }
    }
}
