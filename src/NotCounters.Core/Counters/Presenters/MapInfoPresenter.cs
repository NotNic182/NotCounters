using System.Text;
using NotCounters.Core.Config;
using NotCounters.Core.Formatting;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class MapInfoPresenter : CounterPresenter
    {
        private const string Ellipsis = "...";
        private static readonly char[] LineBreakingChars = { '\r', '\n', '\t' };

        public MapInfoPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.MapInfo, "Map info", CounterCategory.Info, "Map")
            {
                Description = "Song name, artist, mapper, difficulty and star ratings of the map you are playing.",
                Options = new[]
                {
                    OptionBuilder.Bool(CounterOptionKeys.ShowSong, "Song name", true, "Show the song name and sub-name."),
                    OptionBuilder.Bool(CounterOptionKeys.ShowArtist, "Artist", false, "Show the song artist."),
                    OptionBuilder.Bool(CounterOptionKeys.ShowMapper, "Mapper", true, "Show who mapped the level."),
                    OptionBuilder.Bool(CounterOptionKeys.ShowDifficulty, "Difficulty", true, "Show the difficulty (and the characteristic when it is not Standard)."),
                    OptionBuilder.Bool(CounterOptionKeys.ShowBeatLeaderStars, "BeatLeader stars", false, "Show the BeatLeader star rating."),
                    OptionBuilder.Bool(CounterOptionKeys.ShowScoreSaberStars, "ScoreSaber stars", false, "Show the ScoreSaber star rating."),
                    OptionBuilder.Int(CounterOptionKeys.MaxLineLength, "Max line length", 40, 0, 200, 1, "Longer lines are shortened with \"...\". 0 means no limit."),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            var map = stats.Map;
            var maxLength = CounterOptions.GetInt(settings, CounterOptionKeys.MaxLineLength);
            var builder = new StringBuilder();

            if (map != null)
            {
                if (CounterOptions.GetBool(settings, CounterOptionKeys.ShowSong))
                {
                    var song = string.IsNullOrWhiteSpace(map.SongSubName) ? map.SongName : map.SongName + " " + map.SongSubName;
                    AppendLine(builder, song, maxLength);
                }

                if (CounterOptions.GetBool(settings, CounterOptionKeys.ShowArtist))
                {
                    AppendLine(builder, map.SongAuthor, maxLength);
                }

                if (CounterOptions.GetBool(settings, CounterOptionKeys.ShowMapper) && !string.IsNullOrWhiteSpace(map.Mapper))
                {
                    AppendLine(builder, "Mapped by " + map.Mapper, maxLength);
                }

                if (CounterOptions.GetBool(settings, CounterOptionKeys.ShowDifficulty))
                {
                    AppendLine(builder, DifficultyLine(map.DifficultyName, map.Characteristic), maxLength);
                }
            }

            var showBeatLeader = CounterOptions.GetBool(settings, CounterOptionKeys.ShowBeatLeaderStars);
            var showScoreSaber = CounterOptions.GetBool(settings, CounterOptionKeys.ShowScoreSaberStars);
            if (showBeatLeader || showScoreSaber)
            {
                var beatLeader = showBeatLeader ? "BL " + Stars(stats.BeatLeader?.Stars) : string.Empty;
                var scoreSaber = showScoreSaber ? "SS " + Stars(stats.ScoreSaber?.Stars) : string.Empty;
                AppendLine(builder, showBeatLeader && showScoreSaber ? beatLeader + " | " + scoreSaber : beatLeader + scoreSaber, maxLength);
            }

            return new CounterRender(builder.Length == 0 ? NoData : builder.ToString());
        }

        private static string DifficultyLine(string? difficulty, string? characteristic)
        {
            var name = difficulty == "ExpertPlus" ? "Expert+" : difficulty ?? string.Empty;
            var mode = CharacteristicName(characteristic);
            if (mode.Length == 0)
            {
                return name;
            }

            return name.Length == 0 ? mode : name + " (" + mode + ")";
        }

        private static string CharacteristicName(string? serializedName)
        {
            switch (serializedName)
            {
                case null:
                case "":
                case "Standard":
                    return string.Empty;
                case "OneSaber": return "One Saber";
                case "NoArrows": return "No Arrows";
                case "90Degree": return "90 Degree";
                case "360Degree": return "360 Degree";
                default: return serializedName;
            }
        }

        private static string Stars(double? stars) =>
            stars.HasValue && NumberFormatting.IsFinite(stars.Value) ? NumberFormatting.Fixed(stars.Value, 2) + "*" : NoData;

        private static void AppendLine(StringBuilder builder, string? line, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            var text = line!.Trim();
            if (text.IndexOfAny(LineBreakingChars) >= 0)
            {
                text = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            }

            if (maxLength > 0 && text.Length > maxLength)
            {
                text = Shorten(text, maxLength);
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(text);
        }

        private static string Shorten(string text, int maxLength)
        {
            var withEllipsis = maxLength > Ellipsis.Length;
            var keep = withEllipsis ? maxLength - Ellipsis.Length : maxLength;

            if (keep > 0 && char.IsHighSurrogate(text[keep - 1]))
            {
                keep--;
            }

            var kept = text.Substring(0, keep);
            return withEllipsis ? kept.TrimEnd() + Ellipsis : kept;
        }
    }
}
