using System;
using System.Globalization;

namespace NotCounters.Core.Pp
{
    public readonly struct CurvePoint : IEquatable<CurvePoint>
    {
        public CurvePoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }

        public double Y { get; }

        public bool Equals(CurvePoint other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object? obj) => obj is CurvePoint other && Equals(other);

        public override int GetHashCode() => unchecked((X.GetHashCode() * 397) ^ Y.GetHashCode());

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0}, {1})", X, Y);

        public static bool operator ==(CurvePoint left, CurvePoint right) => left.Equals(right);

        public static bool operator !=(CurvePoint left, CurvePoint right) => !left.Equals(right);
    }
}
