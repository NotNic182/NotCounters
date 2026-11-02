using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NotCounters.Core.Leaderboards
{
    internal static class JsonRead
    {
        private const int MaxDepth = 64;

        public static bool TryParse(string? json, [NotNullWhen(true)] out JToken? token, [NotNullWhen(false)] out string? error)
        {
            token = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Empty response body.";
                return false;
            }

            try
            {
                using (var reader = new JsonTextReader(new StringReader(json!)))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.FloatParseHandling = FloatParseHandling.Double;
                    reader.MaxDepth = MaxDepth;
                    token = JToken.ReadFrom(reader);
                    while (reader.Read())
                    {
                        if (reader.TokenType != JsonToken.Comment)
                        {
                            token = null;
                            error = "Unexpected content after the JSON document.";
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                token = null;
                error = "Malformed JSON: " + ex.Message;
                return false;
            }
        }

        public static JToken? Prop(JToken? obj, string name)
        {
            if (!(obj is JObject o)) return null;
            JToken? t = o.GetValue(name, StringComparison.Ordinal) ?? o.GetValue(name, StringComparison.OrdinalIgnoreCase);
            return t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined ? null : t;
        }

        public static bool Has(JToken? obj, string name) =>
            obj is JObject o && o.GetValue(name, StringComparison.OrdinalIgnoreCase) != null;

        public static double? Double(JToken? t)
        {
            if (t == null) return null;
            try
            {
                switch (t.Type)
                {
                    case JTokenType.Float:
                    case JTokenType.Integer:
                        double d = Convert.ToDouble(((JValue)t).Value, CultureInfo.InvariantCulture);
                        return IsFinite(d) ? d : (double?)null;
                    case JTokenType.String:
                        string s = ((string?)t ?? string.Empty).Trim();
                        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && IsFinite(parsed))
                        {
                            return parsed;
                        }

                        return null;
                    default:
                        return null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private const double TwoPow63 = 9223372036854775808.0;

        public static long? Long(JToken? t)
        {
            double? d = Double(t);
            if (!d.HasValue) return null;
            double v = Math.Round(d.Value);
            if (Math.Abs(v - d.Value) > 1e-9 || v >= TwoPow63 || v < -TwoPow63) return null;
            return (long)v;
        }

        public static bool TryParseIntegral(string? s, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (!double.TryParse(s!.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || !IsFinite(d)) return false;
            if (d != Math.Floor(d) || d > int.MaxValue || d < int.MinValue) return false;
            value = (int)d;
            return true;
        }

        public static int? Int(JToken? t)
        {
            long? l = Long(t);
            if (!l.HasValue || l.Value > int.MaxValue || l.Value < int.MinValue) return null;
            return (int)l.Value;
        }

        public static string? String(JToken? t)
        {
            if (t == null) return null;
            switch (t.Type)
            {
                case JTokenType.String:
                    return (string?)t;
                case JTokenType.Integer:
                case JTokenType.Float:
                case JTokenType.Boolean:
                    return Convert.ToString(((JValue)t).Value, CultureInfo.InvariantCulture);
                default:
                    return null;
            }
        }

        public static bool? Bool(JToken? t)
        {
            if (t == null) return null;
            switch (t.Type)
            {
                case JTokenType.Boolean:
                    return (bool)t;
                case JTokenType.Integer:
                case JTokenType.Float:
                    double? d = Double(t);
                    return d.HasValue ? d.Value != 0 : (bool?)null;
                case JTokenType.String:
                    string s = ((string?)t ?? string.Empty).Trim();
                    if (bool.TryParse(s, out bool b)) return b;
                    if (s == "1") return true;
                    if (s == "0") return false;
                    return null;
                default:
                    return null;
            }
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
