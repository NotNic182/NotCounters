using System.Collections.Generic;

namespace NotCounters.Core.Leaderboards
{
    public enum SongSpeed
    {
        Normal,
        Slower,
        Faster,
        SuperFast,
    }

    public sealed record ModifierSet
    {
        public static ModifierSet None { get; } = new ModifierSet();

        public bool NoFail { get; init; }

        public bool InstaFail { get; init; }

        public bool FourLives { get; init; }

        public bool NoBombs { get; init; }

        public bool NoWalls { get; init; }

        public bool NoArrows { get; init; }

        public bool GhostNotes { get; init; }

        public bool DisappearingArrows { get; init; }

        public bool SmallNotes { get; init; }

        public bool ProMode { get; init; }

        public bool StrictAngles { get; init; }

        public bool ZenMode { get; init; }

        public SongSpeed SongSpeed { get; init; } = SongSpeed.Normal;

        public bool IsLeaderboardIncompatible => ZenMode;

        public string? SpeedCode
        {
            get
            {
                switch (SongSpeed)
                {
                    case SongSpeed.Slower: return "SS";
                    case SongSpeed.Faster: return "FS";
                    case SongSpeed.SuperFast: return "SF";
                    default: return null;
                }
            }
        }

        public string ToBeatLeaderCodes(bool noFailCountsFromStart = false, bool energyReachedZero = false)
        {
            var codes = new List<string>(8);
            if (DisappearingArrows) codes.Add("DA");
            string? speed = SpeedCode;
            if (speed != null) codes.Add(speed);
            if (GhostNotes) codes.Add("GN");
            if (NoArrows) codes.Add("NA");
            if (NoBombs) codes.Add("NB");
            if (NoFail && (noFailCountsFromStart || energyReachedZero)) codes.Add("NF");
            if (NoWalls) codes.Add("NO");
            if (SmallNotes) codes.Add("SC");
            if (ProMode) codes.Add("PM");
            if (StrictAngles) codes.Add("SA");
            if (InstaFail) codes.Add("IF");
            if (FourLives) codes.Add("BE");
            return string.Join(",", codes);
        }

        public IReadOnlyList<string> ToScoreSaberCodes(bool energyReachedZero)
        {
            var codes = new List<string>(8);
            if (NoWalls) codes.Add("NO");
            if (NoBombs) codes.Add("NB");
            if (NoFail && energyReachedZero) codes.Add("NF");
            if (SongSpeed == SongSpeed.Slower) codes.Add("SS");
            if (NoArrows) codes.Add("NA");
            if (SongSpeed == SongSpeed.Faster) codes.Add("FS");
            if (DisappearingArrows) codes.Add("DA");
            if (GhostNotes) codes.Add("GN");
            if (FourLives) codes.Add("BE");
            if (InstaFail) codes.Add("IF");
            if (SmallNotes) codes.Add("SC");
            if (StrictAngles) codes.Add("SA");
            if (ProMode) codes.Add("PM");
            if (SongSpeed == SongSpeed.SuperFast) codes.Add("SF");
            return codes.AsReadOnly();
        }
    }
}
