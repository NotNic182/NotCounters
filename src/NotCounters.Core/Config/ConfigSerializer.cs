using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using JsonFormatting = Newtonsoft.Json.Formatting;

namespace NotCounters.Core.Config
{
    public static class ConfigSerializer
    {
        private static readonly IContractResolver Resolver = new DefaultContractResolver
        {
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false, OverrideSpecifiedNames = true },
        };

        private static readonly JsonSerializer Writer = JsonSerializer.Create(CreateWriteSettings());

        private static readonly JsonSerializer CloneReader = JsonSerializer.Create(CreateReadSettings());

        private static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
        {
            CommentHandling = CommentHandling.Ignore,
            LineInfoHandling = LineInfoHandling.Load,
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Replace,
        };

        public static JsonSerializerSettings Settings => CreateWriteSettings();

        public static string Serialize(HudConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            var builder = new StringBuilder(4096);
            using (var stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture))
            using (var jsonWriter = new JsonTextWriter(stringWriter) { Formatting = JsonFormatting.Indented, Indentation = 2 })
            {
                Writer.Serialize(jsonWriter, config);
            }

            return builder.ToString();
        }

        public static HudConfig Deserialize(string json) => Deserialize(json, null);

        public static HudConfig Deserialize(string json, ICollection<string>? warnings) => ToConfig(ParseObject(json), warnings);

        public static HudConfig DeepClone(HudConfig config) => Clone(config);

        public static HudLayout DeepClone(HudLayout layout) => Clone(layout);

        public static CounterSettings DeepClone(CounterSettings counter) => Clone(counter);

        internal static JObject ParseObject(string? json)
        {
            if (json == null || json.Trim().Length == 0)
            {
                throw new ConfigFormatException("The config text is empty.");
            }

            try
            {
                using (var reader = new JsonTextReader(new StringReader(json))
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Double,
                    Culture = CultureInfo.InvariantCulture,
                })
                {
                    JToken root = JToken.ReadFrom(reader, LoadSettings);

                    while (reader.Read())
                    {
                        if (reader.TokenType != JsonToken.Comment)
                        {
                            throw new ConfigFormatException("Unexpected content after the root object.");
                        }
                    }

                    return root as JObject
                           ?? throw new ConfigFormatException($"The root of the config must be a JSON object, found {ConfigJson.Describe(root)}.");
                }
            }
            catch (JsonException ex)
            {
                throw new ConfigFormatException("The config is not valid JSON: " + ex.Message, ex);
            }
        }

        internal static HudConfig ToConfig(JObject root, ICollection<string>? warnings)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            JsonSerializer serializer = JsonSerializer.Create(CreateReadSettings());
            serializer.Error += (_, args) =>
            {
                warnings?.Add("Ignored an invalid value and kept the default: " + args.ErrorContext.Error.Message);
                args.ErrorContext.Handled = true;
            };

            using (JsonReader reader = root.CreateReader())
            {
                return serializer.Deserialize<HudConfig>(reader)
                       ?? throw new ConfigFormatException("The config object could not be read.");
            }
        }

        private static T Clone<T>(T source)
            where T : class
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            JToken tree = JToken.FromObject(source, Writer);
            return tree.ToObject<T>(CloneReader)
                   ?? throw new InvalidOperationException($"Cloning {typeof(T).Name} produced no value.");
        }

        private static JsonSerializerSettings CreateWriteSettings() => new JsonSerializerSettings
        {
            ContractResolver = Resolver,
            Converters = CreateConverters(),
            Formatting = JsonFormatting.Indented,
            Culture = CultureInfo.InvariantCulture,
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
            FloatFormatHandling = FloatFormatHandling.DefaultValue,
            TypeNameHandling = TypeNameHandling.None,
        };

        private static JsonSerializerSettings CreateReadSettings()
        {
            JsonSerializerSettings settings = CreateWriteSettings();

            settings.NullValueHandling = NullValueHandling.Ignore;
            settings.ObjectCreationHandling = ObjectCreationHandling.Auto;
            return settings;
        }

        private static IList<JsonConverter> CreateConverters() => new List<JsonConverter>
        {
            new StringEnumConverter { AllowIntegerValues = true },
            new Vec3JsonConverter(),
            new ColorValueJsonConverter(),
        };
    }
}
