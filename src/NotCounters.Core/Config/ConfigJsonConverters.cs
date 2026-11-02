using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonFormatting = Newtonsoft.Json.Formatting;

namespace NotCounters.Core.Config
{
    public sealed class Vec3JsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(Vec3) || objectType == typeof(Vec3?);

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is not Vec3 vector)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartArray();

            JsonFormatting previous = writer.Formatting;
            writer.Formatting = JsonFormatting.None;
            try
            {
                writer.WriteValue(vector.X);
                writer.WriteValue(vector.Y);
                writer.WriteValue(vector.Z);
                writer.WriteEndArray();
            }
            finally
            {
                writer.Formatting = previous;
            }
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            string path = reader.Path;
            JToken token = JToken.Load(reader);
            if (token.Type == JTokenType.Null && objectType == typeof(Vec3?))
            {
                return null;
            }

            if (token is JArray array && array.Count == 3
                && TryReadNumber(array[0], out float x) && TryReadNumber(array[1], out float y) && TryReadNumber(array[2], out float z))
            {
                return new Vec3(x, y, z);
            }

            throw new JsonSerializationException(
                $"Invalid vector at '{path}': expected an array of three numbers [x, y, z], found {ConfigJson.Describe(token)}.");
        }

        private static bool TryReadNumber(JToken token, out float value)
        {
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                value = token.Value<float>();
                return true;
            }

            value = 0f;
            return false;
        }
    }

    public sealed class ColorValueJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(ColorValue) || objectType == typeof(ColorValue?);

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is ColorValue color)
            {
                writer.WriteValue(color.ToHex());
            }
            else
            {
                writer.WriteNull();
            }
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            string path = reader.Path;
            JToken token = JToken.Load(reader);
            if (token.Type == JTokenType.Null && objectType == typeof(ColorValue?))
            {
                return null;
            }

            if (token.Type == JTokenType.String && ColorValue.TryParseHex(token.Value<string>(), out ColorValue color))
            {
                return color;
            }

            throw new JsonSerializationException(
                $"Invalid colour at '{path}': expected \"#RRGGBBAA\", \"#RRGGBB\" or \"#RGB\", found {ConfigJson.Describe(token)}.");
        }
    }

    internal static class ConfigJson
    {
        private const int MaxQuotedLength = 40;

        public static string Describe(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Array:
                    return "an array of " + ((JArray)token).Count;
                case JTokenType.Object:
                    return "an object";
                case JTokenType.Null:
                    return "null";
                case JTokenType.String:
                    string text = token.Value<string>() ?? string.Empty;
                    return "\"" + (text.Length > MaxQuotedLength ? text.Substring(0, MaxQuotedLength) + "..." : text) + "\"";
                default:
                    return token.ToString(JsonFormatting.None);
            }
        }
    }
}
