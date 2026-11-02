using System;
using System.Collections.Generic;
using System.Linq;
using NotCounters.Util;

namespace NotCounters.Editor
{
    internal sealed class EnvironmentOption
    {
        internal EnvironmentOption(EnvironmentInfoSO info)
        {
            Info = info;
            SerializedName = info.serializedName;
            DisplayName = string.IsNullOrWhiteSpace(info.environmentName) ? info.serializedName : info.environmentName;
            Type = info.environmentType;
        }

        public string SerializedName { get; }

        public string DisplayName { get; internal set; }

        public EnvironmentType Type { get; }

        public bool Is360 => Type == EnvironmentType.Circle;

        internal EnvironmentInfoSO Info { get; }

        public override string ToString() => DisplayName;
    }

    internal sealed class EnvironmentCatalog
    {
        public const string FallbackSerializedName = "DefaultEnvironment";

        private readonly EnvironmentsListModel _environments;
        private IReadOnlyList<EnvironmentOption>? _options;
        private Dictionary<string, EnvironmentOption>? _bySerializedName;

        public EnvironmentCatalog(EnvironmentsListModel environments)
        {
            _environments = environments;
        }

        public IReadOnlyList<EnvironmentOption> Environments
        {
            get
            {
                EnsureBuilt();
                return _options!;
            }
        }

        public EnvironmentOption? Default => Find(FallbackSerializedName) ?? (Environments.Count > 0 ? Environments[0] : null);

        public EnvironmentOption? Find(string? serializedName)
        {
            if (string.IsNullOrEmpty(serializedName))
            {
                return null;
            }

            EnsureBuilt();
            return _bySerializedName!.TryGetValue(serializedName!, out var option) ? option : null;
        }

        public EnvironmentOption? FindOrDefault(string? serializedName) => Find(serializedName) ?? Default;

        private void EnsureBuilt()
        {
            if (_options != null)
            {
                return;
            }

            var options = Safe.Invoke(
                "EnvironmentCatalog.Build",
                () =>
                {
                    var list = _environments.environmentInfos
                        .Where(IsPreviewable)
                        .Select(info => new EnvironmentOption(info))
                        .ToList();
                    DisambiguateDisplayNames(list);
                    return list.OrderBy(option => option.DisplayName, StringComparer.InvariantCultureIgnoreCase).ToList();
                },
                new List<EnvironmentOption>());

            var map = new Dictionary<string, EnvironmentOption>(StringComparer.Ordinal);
            foreach (var option in options)
            {
                if (!string.IsNullOrEmpty(option.SerializedName) && !map.ContainsKey(option.SerializedName))
                {
                    map.Add(option.SerializedName, option);
                }
            }

            _bySerializedName = map;
            _options = options;
        }

        private static void DisambiguateDisplayNames(List<EnvironmentOption> options)
        {
            foreach (var group in options.GroupBy(option => option.DisplayName, StringComparer.InvariantCultureIgnoreCase))
            {
                if (group.Count() < 2)
                {
                    continue;
                }

                foreach (var option in group)
                {
                    var suffix = option.SerializedName.EndsWith("Environment", StringComparison.Ordinal)
                        ? option.SerializedName.Substring(0, option.SerializedName.Length - "Environment".Length)
                        : option.SerializedName;
                    option.DisplayName = $"{option.DisplayName} ({suffix})";
                }
            }
        }

        private static bool IsPreviewable(EnvironmentInfoSO? info) =>
            info != null
            && !info.isInDevelopment
            && (info.environmentType == EnvironmentType.Normal || info.environmentType == EnvironmentType.Circle);
    }
}
