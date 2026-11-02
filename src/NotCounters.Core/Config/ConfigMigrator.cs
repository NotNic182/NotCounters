using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using JsonFormatting = Newtonsoft.Json.Formatting;

namespace NotCounters.Core.Config
{
    internal static class ConfigMigrator
    {
        internal const string SchemaVersionProperty = "schemaVersion";

        private static readonly Action<JObject, ICollection<string>>[] Steps =
        {
            UpgradeUnversionedToV1,
            UpgradeV1ToV2,
            UpgradeV2ToV3,
            UpgradeV3ToV4,
        };

        private const string GlobalProperty = "global";
        private const string NoFailCountsAsZeroProperty = "beatLeaderNoFailCountsAsZero";

        public static long ReadSchemaVersion(JObject root, ICollection<string> warnings)
        {
            JToken? token = root[SchemaVersionProperty];
            if (token == null || token.Type == JTokenType.Null)
            {
                return 0;
            }

            if (token.Type == JTokenType.Integer || (token.Type == JTokenType.Float && IsWholeNumber(token.Value<double>())))
            {
                double value = token.Value<double>();
                if (value >= 0)
                {
                    return value >= long.MaxValue ? long.MaxValue : (long)value;
                }
            }

            warnings.Add($"Unreadable {SchemaVersionProperty} {token.ToString(JsonFormatting.None)}; treated as 0 (oldest format).");
            return 0;
        }

        public static void Migrate(JObject root, long fromVersion, ICollection<string> warnings)
        {
            for (long version = Math.Max(0, fromVersion); version < HudConfig.CurrentSchemaVersion; version++)
            {
                Steps[version](root, warnings);
                root[SchemaVersionProperty] = version + 1;
            }
        }

        private static void UpgradeUnversionedToV1(JObject root, ICollection<string> warnings)
        {
            warnings.Add("Upgraded an unversioned (pre-release) config to schema version 1.");
        }

        private static void UpgradeV1ToV2(JObject root, ICollection<string> warnings)
        {
            if (root[GlobalProperty] is JObject global
                && global[NoFailCountsAsZeroProperty] is JValue { Type: JTokenType.Boolean } value
                && (bool)value)
            {
                global[NoFailCountsAsZeroProperty] = false;
                warnings.Add($"Upgraded the config to schema version 2: {GlobalProperty}.{NoFailCountsAsZeroProperty} was reset to false (BeatLeader pp with No Fail now drops only after an actual fail).");
            }
        }

        private static void UpgradeV2ToV3(JObject root, ICollection<string> warnings)
        {
            warnings.Add("Upgraded the config to schema version 3: the BeatLeader and ScoreSaber pp counters are now one PP counter (each conversion is listed separately).");
        }

        private static void UpgradeV3ToV4(JObject root, ICollection<string> warnings)
        {
            if (root["profiles"] is JArray profiles)
            {
                foreach (JToken profile in profiles)
                {
                    if (profile is not JObject profileObject)
                    {
                        continue;
                    }

                    WriteMissingAttachments(profileObject["layout"]);
                    if (profileObject["environmentOverrides"] is JObject overrides)
                    {
                        foreach (JProperty entry in overrides.Properties())
                        {
                            WriteMissingAttachments(entry.Value);
                        }
                    }
                }
            }

            warnings.Add("Upgraded the config to schema version 4: counters follow the environment's HUD, and the game's own text look and size are the defaults (each change is listed separately).");
        }

        private static void WriteMissingAttachments(JToken? layout)
        {
            if (layout is not JObject layoutObject || layoutObject["counters"] is not JArray counters)
            {
                return;
            }

            foreach (JToken counter in counters)
            {
                if (counter is JObject counterObject && counterObject["attachment"] == null)
                {
                    counterObject["attachment"] = nameof(CounterAttachment.None);
                }
            }
        }

        private static bool IsWholeNumber(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && Math.Floor(value) == value;
    }
}
