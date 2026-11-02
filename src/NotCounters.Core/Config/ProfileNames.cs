using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NotCounters.Core.Config
{
    public static class ProfileNames
    {
        public const string DefaultBaseName = "Profile";

        public static string Clean(string? name)
        {
            if (name == null)
            {
                return string.Empty;
            }

            string cleaned = CleanOnce(name);
            for (string again = CleanOnce(cleaned); !string.Equals(again, cleaned, StringComparison.Ordinal); again = CleanOnce(cleaned))
            {
                cleaned = again;
            }

            return cleaned;
        }

        private static string CleanOnce(string name)
        {
            var builder = new StringBuilder(name.Length);
            var spacePending = false;
            int units;
            for (int i = 0; i < name.Length; i += units)
            {
                units = char.IsSurrogatePair(name, i) ? 2 : 1;
                if (CharUnicodeInfo.GetUnicodeCategory(name, i) == UnicodeCategory.Format)
                {
                    continue;
                }

                char c = name[i];
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                {
                    spacePending = builder.Length > 0;
                    continue;
                }

                if (spacePending)
                {
                    builder.Append(' ');
                    spacePending = false;
                }

                builder.Append(name, i, units);
            }

            return builder.ToString();
        }

        public static string Unique(IEnumerable<HudProfile> profiles, string? baseName, string suffix = "", HudProfile? except = null)
        {
            string name = Clean(baseName);
            if (name.Length == 0)
            {
                name = DefaultBaseName;
            }

            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (HudProfile profile in profiles)
            {
                if (profile != null && !ReferenceEquals(profile, except))
                {
                    string other = Clean(profile.Name);
                    if (other.Length > 0)
                    {
                        taken.Add(other);
                    }
                }
            }

            return FirstFree(name + suffix, taken);
        }

        public static string? ForRename(IEnumerable<HudProfile> profiles, HudProfile profile, string? typed)
        {
            if (Clean(typed).Length == 0)
            {
                return null;
            }

            string name = Unique(profiles, typed, except: profile);
            return string.Equals(name, profile.Name, StringComparison.Ordinal) ? null : name;
        }

        internal static void Repair(List<HudProfile> profiles, List<string> warnings)
        {
            for (int i = 0; i < profiles.Count; i++)
            {
                HudProfile profile = profiles[i];
                string name = Clean(profile.Name);
                if (name.Length > 0 && !string.Equals(name, profile.Name, StringComparison.Ordinal))
                {
                    warnings.Add($"profiles[{i}].name: cleaned to \"{name}\" (one line, single spaces, no invisible characters).");
                }

                profile.Name = name;
            }

            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (HudProfile profile in profiles)
            {
                if (profile.Name.Length > 0)
                {
                    taken.Add(profile.Name);
                }
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < profiles.Count; i++)
            {
                HudProfile profile = profiles[i];
                if (profile.Name.Length > 0 && seen.Add(profile.Name))
                {
                    continue;
                }

                string path = $"profiles[{i}].name";
                if (profile.Name.Length == 0)
                {
                    int number = i + 1;
                    while (taken.Contains(Numbered(DefaultBaseName, number)))
                    {
                        number++;
                    }

                    profile.Name = Numbered(DefaultBaseName, number);
                    warnings.Add($"{path}: empty; renamed to \"{profile.Name}\".");
                }
                else
                {
                    string previous = profile.Name;
                    profile.Name = FirstFree(previous, taken);
                    warnings.Add($"{path}: \"{previous}\" is the name of an earlier profile; renamed to \"{profile.Name}\".");
                }

                taken.Add(profile.Name);
                seen.Add(profile.Name);
            }
        }

        private static string FirstFree(string name, HashSet<string> taken)
        {
            if (!taken.Contains(name))
            {
                return name;
            }

            for (int number = 2; ; number++)
            {
                string candidate = Numbered(name, number);
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        private static string Numbered(string name, int number) => name + " " + number.ToString(CultureInfo.InvariantCulture);
    }
}
