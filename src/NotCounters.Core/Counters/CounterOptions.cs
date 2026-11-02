using System;
using System.Collections.Generic;
using System.Globalization;
using NotCounters.Core.Config;
using Newtonsoft.Json.Linq;

namespace NotCounters.Core.Counters
{
    public static class CounterOptions
    {
        public const int MaxTextLength = 256;

        public static CounterOptionDescriptor? Find(string typeId, string key) => CounterCatalog.FindOption(typeId, key);

        public static bool GetBool(CounterSettings s, string key)
        {
            var descriptor = FindFor(s, key);
            if (TryGetToken(s, key, out var token) && TryReadBool(token, out var value))
            {
                return value;
            }

            return descriptor != null && TryReadBool(descriptor.DefaultValue, out var fallback) && fallback;
        }

        public static int GetInt(CounterSettings s, string key)
        {
            var descriptor = FindFor(s, key);
            if (TryGetToken(s, key, out var token) && TryReadInt(token, descriptor, out var value))
            {
                return value;
            }

            return descriptor != null && TryReadInt(descriptor.DefaultValue, descriptor, out var fallback) ? fallback : 0;
        }

        public static float GetFloat(CounterSettings s, string key)
        {
            var descriptor = FindFor(s, key);
            if (TryGetToken(s, key, out var token) && TryReadFloat(token, descriptor, out var value))
            {
                return value;
            }

            return descriptor != null && TryReadFloat(descriptor.DefaultValue, descriptor, out var fallback) ? fallback : 0f;
        }

        public static string GetString(CounterSettings s, string key)
        {
            var descriptor = FindFor(s, key);
            if (TryGetToken(s, key, out var token) && token.Type == JTokenType.String)
            {
                return (string?)token ?? string.Empty;
            }

            return descriptor != null && descriptor.DefaultValue.Type == JTokenType.String
                ? (string?)descriptor.DefaultValue ?? string.Empty
                : string.Empty;
        }

        public static string GetChoice(CounterSettings s, string key)
        {
            var descriptor = FindFor(s, key);
            if (TryGetToken(s, key, out var token) && TryReadChoice(token, descriptor, out var value))
            {
                return value;
            }

            return descriptor != null && TryReadChoice(descriptor.DefaultValue, descriptor, out var fallback) ? fallback : string.Empty;
        }

        public static ColorValue GetColor(CounterSettings s, string key)
        {
            var descriptor = FindFor(s, key);
            if (TryGetToken(s, key, out var token) && TryReadColor(token, out var value))
            {
                return value;
            }

            return descriptor != null && TryReadColor(descriptor.DefaultValue, out var fallback) ? fallback : ColorValue.White;
        }

        public static void Set(CounterSettings s, string key, JToken? value)
        {
            if (s == null)
            {
                return;
            }

            var descriptor = Find(s.Type, key);
            if (descriptor == null)
            {
                return;
            }

            s.Options ??= new Dictionary<string, JToken>(StringComparer.Ordinal);
            s.Options[descriptor.Key] = Canonicalize(descriptor, value);
        }

        public static void ApplyDefaults(CounterSettings s)
        {
            if (s == null)
            {
                return;
            }

            var descriptor = CounterCatalog.GetDescriptor(s.Type);
            if (descriptor == null)
            {
                return;
            }

            s.Options ??= new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var option in descriptor.Options)
            {
                if (!s.Options.TryGetValue(option.Key, out var existing) || existing == null || existing.Type == JTokenType.Null)
                {
                    s.Options[option.Key] = option.DefaultValue.DeepClone();
                }
            }
        }

        private static CounterOptionDescriptor? FindFor(CounterSettings? s, string key) => s == null ? null : Find(s.Type, key);

        private static bool TryGetToken(CounterSettings? s, string key, out JToken token)
        {
            token = null!;
            if (s?.Options == null || key == null || !s.Options.TryGetValue(key, out var found) || found == null || found.Type == JTokenType.Null)
            {
                return false;
            }

            token = found;
            return true;
        }

        private static JToken Canonicalize(CounterOptionDescriptor descriptor, JToken? value)
        {
            if (value != null && value.Type != JTokenType.Null)
            {
                switch (descriptor.Kind)
                {
                    case CounterOptionKind.Bool:
                        if (TryReadBool(value, out var b))
                        {
                            return new JValue(b);
                        }

                        break;
                    case CounterOptionKind.Int:
                        if (TryReadInt(value, descriptor, out var i))
                        {
                            return new JValue((long)i);
                        }

                        break;
                    case CounterOptionKind.Float:
                        if (TryReadFloat(value, descriptor, out var f))
                        {
                            return new JValue(double.Parse(f.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
                        }

                        break;
                    case CounterOptionKind.Choice:
                        if (TryReadChoice(value, descriptor, out var choice))
                        {
                            return new JValue(choice);
                        }

                        break;
                    case CounterOptionKind.Text:
                        if (value.Type == JTokenType.String)
                        {
                            var text = (string?)value ?? string.Empty;
                            return new JValue(text.Length > MaxTextLength ? text.Substring(0, MaxTextLength) : text);
                        }

                        break;
                    case CounterOptionKind.Color:
                        if (TryReadColor(value, out var color))
                        {
                            return new JValue(color.ToHex());
                        }

                        break;
                }
            }

            return descriptor.DefaultValue.DeepClone();
        }

        private static bool TryReadBool(JToken token, out bool value)
        {
            value = false;
            switch (token.Type)
            {
                case JTokenType.Boolean:
                    value = (bool)token;
                    return true;
                case JTokenType.Integer:
                case JTokenType.Float:
                    if (TryReadNumber(token, out var number))
                    {
                        value = number != 0d;
                        return true;
                    }

                    return false;
                case JTokenType.String:
                    return bool.TryParse(((string?)token ?? string.Empty).Trim(), out value);
                default:
                    return false;
            }
        }

        private static bool TryReadNumber(JToken token, out double value)
        {
            value = 0d;
            switch (token.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    try
                    {
                        value = (double)token;
                    }
                    catch (Exception ex) when (ex is OverflowException || ex is InvalidCastException || ex is ArgumentException || ex is FormatException)
                    {
                        return false;
                    }

                    break;
                case JTokenType.String:
                    if (!double.TryParse((string?)token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    {
                        return false;
                    }

                    break;
                default:
                    return false;
            }

            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool TryReadInt(JToken token, CounterOptionDescriptor? descriptor, out int value)
        {
            value = 0;
            if (!TryReadNumber(token, out var number))
            {
                return false;
            }

            number = Math.Round(number, MidpointRounding.AwayFromZero);
            number = ClampToDescriptor(number, descriptor);
            if (number > int.MaxValue)
            {
                number = int.MaxValue;
            }
            else if (number < int.MinValue)
            {
                number = int.MinValue;
            }

            value = (int)number;
            return true;
        }

        private static bool TryReadFloat(JToken token, CounterOptionDescriptor? descriptor, out float value)
        {
            value = 0f;
            if (!TryReadNumber(token, out var number))
            {
                return false;
            }

            number = ClampToDescriptor(number, descriptor);
            if (number > float.MaxValue)
            {
                number = float.MaxValue;
            }
            else if (number < -float.MaxValue)
            {
                number = -float.MaxValue;
            }

            value = (float)number;
            return true;
        }

        private static bool TryReadChoice(JToken token, CounterOptionDescriptor? descriptor, out string value)
        {
            value = string.Empty;
            if (token.Type != JTokenType.String)
            {
                return false;
            }

            var text = (string?)token ?? string.Empty;
            var choices = descriptor?.Choices;
            if (choices == null || choices.Count == 0)
            {
                value = text;
                return true;
            }

            for (var i = 0; i < choices.Count; i++)
            {
                if (string.Equals(choices[i].Key, text, StringComparison.Ordinal))
                {
                    value = choices[i].Key;
                    return true;
                }
            }

            var trimmed = text.Trim();
            for (var i = 0; i < choices.Count; i++)
            {
                if (string.Equals(choices[i].Key, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    value = choices[i].Key;
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadColor(JToken token, out ColorValue value)
        {
            value = ColorValue.White;
            return token.Type == JTokenType.String && ColorValue.TryParseHex((string?)token, out value);
        }

        private static double ClampToDescriptor(double number, CounterOptionDescriptor? descriptor)
        {
            if (descriptor == null)
            {
                return number;
            }

            if (number < descriptor.Min)
            {
                return descriptor.Min;
            }

            return number > descriptor.Max ? descriptor.Max : number;
        }
    }
}
