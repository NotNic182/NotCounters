using System;
using System.Globalization;

namespace NotCounters.Core.Config
{
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        public static bool operator ==(Vec3 a, Vec3 b) => a.Equals(b);

        public static bool operator !=(Vec3 a, Vec3 b) => !a.Equals(b);

        public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object? obj) => obj is Vec3 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                return (hash * 397) ^ Z.GetHashCode();
            }
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", X, Y, Z);
    }

    public readonly struct ColorValue : IEquatable<ColorValue>
    {
        public static readonly ColorValue White = new ColorValue(1f, 1f, 1f, 1f);
        public static readonly ColorValue Black = new ColorValue(0f, 0f, 0f, 1f);

        public ColorValue(float r, float g, float b, float a = 1f)
        {
            R = Clamp01(r);
            G = Clamp01(g);
            B = Clamp01(b);
            A = Clamp01(a);
        }

        public float R { get; }

        public float G { get; }

        public float B { get; }

        public float A { get; }

        public ColorValue WithAlpha(float a) => new ColorValue(R, G, B, a);

        public string ToHex() =>
            "#" + ToByte(R).ToString("X2", CultureInfo.InvariantCulture)
                + ToByte(G).ToString("X2", CultureInfo.InvariantCulture)
                + ToByte(B).ToString("X2", CultureInfo.InvariantCulture)
                + ToByte(A).ToString("X2", CultureInfo.InvariantCulture);

        public static bool TryParseHex(string? text, out ColorValue color)
        {
            color = White;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var hex = text!.Trim();
            if (hex.StartsWith("#", StringComparison.Ordinal))
            {
                hex = hex.Substring(1);
            }

            if (hex.Length == 3)
            {
                hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
            }

            if (hex.Length == 6)
            {
                hex += "FF";
            }

            if (hex.Length != 8 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
            {
                return false;
            }

            color = new ColorValue(
                ((packed >> 24) & 0xFF) / 255f,
                ((packed >> 16) & 0xFF) / 255f,
                ((packed >> 8) & 0xFF) / 255f,
                (packed & 0xFF) / 255f);
            return true;
        }

        public static bool operator ==(ColorValue a, ColorValue b) => a.Equals(b);

        public static bool operator !=(ColorValue a, ColorValue b) => !a.Equals(b);

        public bool Equals(ColorValue other) => ToHex() == other.ToHex();

        public override bool Equals(object? obj) => obj is ColorValue other && Equals(other);

        public override int GetHashCode() => ToHex().GetHashCode();

        public override string ToString() => ToHex();

        private static float Clamp01(float v) => float.IsNaN(v) ? 0f : v < 0f ? 0f : v > 1f ? 1f : v;

        private static int ToByte(float v) => (int)Math.Round(Clamp01(v) * 255f, MidpointRounding.AwayFromZero);
    }
}
