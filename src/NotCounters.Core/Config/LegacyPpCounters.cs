using System;
using System.Collections.Generic;
using NotCounters.Core.Counters;
using Newtonsoft.Json.Linq;

namespace NotCounters.Core.Config
{
    public static class LegacyCounterTypeIds
    {
        public const string BeatLeaderPp = "beatLeaderPp";
        public const string ScoreSaberPp = "scoreSaberPp";

        internal static bool IsLegacy(string? typeId) => typeId == BeatLeaderPp || typeId == ScoreSaberPp;
    }

    internal static class LegacyPpCounters
    {
        internal const float PairMaxDistanceMetres = 1f;

        internal const string BeatLeaderName = "BeatLeader pp";

        internal const string ScoreSaberName = "ScoreSaber pp";

        private static readonly string[] EitherOptionKeys =
        {
            CounterOptionKeys.ShowMaxPp,
            CounterOptionKeys.ShowFcPp,
            CounterOptionKeys.ShowStars,
        };

        internal static void Convert(List<CounterSettings>? counters, string layoutPath, List<string> warnings)
        {
            if (counters == null)
            {
                return;
            }

            var beatLeader = new List<Entry>();
            var scoreSaber = new List<Entry>();
            bool hasPpCounter = false;
            for (int i = 0; i < counters.Count; i++)
            {
                CounterSettings counter = counters[i];
                if (counter == null)
                {
                    continue;
                }

                string path = $"{layoutPath}.counters[{i}]";
                switch (counter.Type)
                {
                    case LegacyCounterTypeIds.BeatLeaderPp:
                        beatLeader.Add(new Entry(counter, path));
                        break;
                    case LegacyCounterTypeIds.ScoreSaberPp:
                        scoreSaber.Add(new Entry(counter, path));
                        break;
                    case CounterTypeIds.Pp:
                        hasPpCounter = true;
                        break;
                }
            }

            if (beatLeader.Count == 0 && scoreSaber.Count == 0)
            {
                return;
            }

            foreach (Entry entry in beatLeader)
            {
                ConfigValidator.EnsureStyleAndOptions(entry.Counter, entry.Path, warnings);
            }

            foreach (Entry entry in scoreSaber)
            {
                ConfigValidator.EnsureStyleAndOptions(entry.Counter, entry.Path, warnings);
            }

            bool slotFree = !hasPpCounter;
            bool onlyOne = beatLeader.Count + scoreSaber.Count == 1;
            int pairs = Math.Min(beatLeader.Count, scoreSaber.Count);
            for (int k = 0; k < pairs; k++)
            {
                string? blocker = MergeBlocker(beatLeader[k].Counter, scoreSaber[k].Counter);
                if (blocker == null)
                {
                    Merge(counters, beatLeader[k], scoreSaber[k], ref slotFree, warnings);
                }
                else
                {
                    ConvertAlone(beatLeader[k], beatLeaderSide: true, blocker, mayMove: false, ref slotFree, warnings);
                    ConvertAlone(scoreSaber[k], beatLeaderSide: false, blocker, mayMove: false, ref slotFree, warnings);
                }
            }

            for (int k = pairs; k < beatLeader.Count; k++)
            {
                ConvertAlone(beatLeader[k], beatLeaderSide: true, "there is no ScoreSaber pp counter to pair it with", onlyOne, ref slotFree, warnings);
            }

            for (int k = pairs; k < scoreSaber.Count; k++)
            {
                ConvertAlone(scoreSaber[k], beatLeaderSide: false, "there is no BeatLeader pp counter to pair it with", onlyOne, ref slotFree, warnings);
            }
        }

        private static string? MergeBlocker(CounterSettings beatLeader, CounterSettings scoreSaber)
        {
            if (beatLeader.Enabled != scoreSaber.Enabled)
            {
                return "only one of the pair is shown";
            }

            if (beatLeader.Attachment != scoreSaber.Attachment)
            {
                return "the pair follows different side panels";
            }

            Vec3 offset = beatLeader.Position - scoreSaber.Position;
            double distance = Math.Sqrt(((double)offset.X * offset.X) + ((double)offset.Y * offset.Y) + ((double)offset.Z * offset.Z));
            return distance <= PairMaxDistanceMetres ? null : "the pair is more than 1 m apart";
        }

        private static void Merge(List<CounterSettings> counters, Entry beatLeaderEntry, Entry scoreSaberEntry, ref bool slotFree, List<string> warnings)
        {
            CounterSettings beatLeader = beatLeaderEntry.Counter;
            CounterSettings scoreSaber = scoreSaberEntry.Counter;
            bool moveToDefault = slotFree
                && DefaultConfigFactory.IsAtLegacyPpSlot(beatLeader)
                && DefaultConfigFactory.IsAtLegacyPpSlot(scoreSaber);
            bool resize = moveToDefault && DefaultConfigFactory.HasSecondReleaseLook(beatLeader.Style, TextAlign.Right);

            foreach (string key in EitherOptionKeys)
            {
                if (!CounterOptions.GetBool(beatLeader, key) && CounterOptions.GetBool(scoreSaber, key))
                {
                    beatLeader.Options[key] = new JValue(true);
                }
            }

            beatLeader.Type = CounterTypeIds.Pp;
            beatLeader.Options[CounterOptionKeys.Leaderboards] = new JValue(CounterChoices.PpBoth);
            counters.Remove(scoreSaber);

            string? label = beatLeader.Style.LabelText;
            if (moveToDefault)
            {
                MoveToDefaultSlot(beatLeader, resize);
                slotFree = false;
            }
            else
            {
                beatLeader.Position = Midpoint(beatLeader.Position, scoreSaber.Position);
            }

            warnings.Add($"{beatLeaderEntry.Path} and {scoreSaberEntry.Path}: the BeatLeader and ScoreSaber pp counters were merged into one PP counter that shows both leaderboards, {(moveToDefault ? "at the PP counter's default place" : "halfway between where the two were")}.");
            if (!string.IsNullOrWhiteSpace(label))
            {
                beatLeader.Style.LabelText = null;
                warnings.Add($"{beatLeaderEntry.Path}.style.labelText: \"{label}\" removed from the merged PP counter, whose lines are named by the leaderboard logos.");
            }
        }

        private static void ConvertAlone(Entry entry, bool beatLeaderSide, string reason, bool mayMove, ref bool slotFree, List<string> warnings)
        {
            CounterSettings counter = entry.Counter;
            bool moveToDefault = mayMove && slotFree && DefaultConfigFactory.IsAtLegacyPpSlot(counter);
            bool resize = moveToDefault
                && DefaultConfigFactory.HasSecondReleaseLook(counter.Style, beatLeaderSide ? TextAlign.Right : TextAlign.Left);
            string leaderboard = beatLeaderSide ? "BeatLeader" : "ScoreSaber";

            counter.Type = CounterTypeIds.Pp;
            counter.Options[CounterOptionKeys.Leaderboards] = new JValue(beatLeaderSide ? CounterChoices.PpBeatLeader : CounterChoices.PpScoreSaber);
            if (string.IsNullOrWhiteSpace(counter.Name))
            {
                counter.Name = beatLeaderSide ? BeatLeaderName : ScoreSaberName;
            }

            if (moveToDefault)
            {
                MoveToDefaultSlot(counter, resize);
                slotFree = false;
            }

            warnings.Add($"{entry.Path}: the {leaderboard} pp counter became a PP counter that shows only {leaderboard} ({reason}){(moveToDefault ? ", at the PP counter's default place" : string.Empty)}.");
        }

        private static void MoveToDefaultSlot(CounterSettings counter, bool resize)
        {
            if (resize)
            {
                counter.Style.FontSize = TextStyle.DefaultFontSize;
            }

            DefaultConfigFactory.PlaceInDefaultSlot(counter);
        }

        private static Vec3 Midpoint(Vec3 a, Vec3 b) => new Vec3(Halfway(a.X, b.X), Halfway(a.Y, b.Y), Halfway(a.Z, b.Z));

        private static float Halfway(float a, float b) => (float)Math.Round(((double)a + b) / 2d, 4);

        private readonly struct Entry
        {
            public Entry(CounterSettings counter, string path)
            {
                Counter = counter;
                Path = path;
            }

            public CounterSettings Counter { get; }

            public string Path { get; }
        }
    }
}
