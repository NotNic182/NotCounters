using NotCounters.Core.Config;
using NotCounters.Core.Leaderboards;

namespace NotCounters.Core.Stats
{
    public sealed class StatsSnapshot
    {
        public StatsSnapshot()
        {
            Left = new HandStats();
            Right = new HandStats();
            BeatLeader = new LeaderboardPpInfo();
            ScoreSaber = new LeaderboardPpInfo();
            PersonalBest = new PersonalBestInfo { Source = PbSource.Local };
            BeatLeaderPersonalBest = new PersonalBestInfo { Source = PbSource.BeatLeader };
            ScoreSaberPersonalBest = new PersonalBestInfo { Source = PbSource.ScoreSaber };
            Map = new MapInfo();
            Modifiers = new ModifierSet();
        }

        public bool IsPractice { get; set; }

        public bool IsPartyOrCampaign { get; set; }

        public ModifierSet Modifiers { get; set; }

        public MapInfo Map { get; set; }

        public float SongTime { get; set; }

        public float SongLength { get; set; }

        public float StartSongTime { get; set; }

        public float SongSpeed { get; set; } = 1f;

        public int MultipliedScore { get; set; }

        public int ModifiedScore { get; set; }

        public int ImmediateMaxMultipliedScore { get; set; }

        public int FullMapMaxMultipliedScore { get; set; }

        public double? Accuracy { get; set; }

        public double? ModifiedRelativeScore { get; set; }

        public double? FcAccuracy { get; set; }

        public double? BestPossibleAccuracy { get; set; }

        public string? Rank { get; set; }

        public int Combo { get; set; }

        public int MaxCombo { get; set; }

        public bool IsFullCombo { get; set; } = true;

        public int Multiplier { get; set; } = 1;

        public float MultiplierProgress { get; set; }

        public int TotalNotes { get; set; }

        public int TotalChainLinks { get; set; }

        public int ResolvedNotes { get; set; }

        public int ResolvedChainLinks { get; set; }

        public int GoodCuts { get; set; }

        public int ChainLinkGoodCuts { get; set; }

        public int Misses { get; set; }

        public int BadCuts { get; set; }

        public int ChainLinkMisses { get; set; }

        public int ChainLinkBadCuts { get; set; }

        public int BombsHit { get; set; }

        public int ResolvedBombs { get; set; }

        public int TotalBombs { get; set; }

        public int WallsHit { get; set; }

        public float SecondsInWalls { get; set; }

        public int Pauses { get; set; }

        public HandStats Left { get; }

        public HandStats Right { get; }

        public float Energy { get; set; } = 0.5f;

        public bool IsBatteryEnergy { get; set; }

        public int BatteryLives { get; set; }

        public int BatteryMaxLives { get; set; }

        public bool EnergyReachedZero { get; set; }

        public float? FailedAtSongTime { get; set; }

        public bool LevelFailed { get; set; }

        public PersonalBestInfo PersonalBest { get; set; }

        public PersonalBestInfo BeatLeaderPersonalBest { get; set; }

        public PersonalBestInfo ScoreSaberPersonalBest { get; set; }

        public LeaderboardPpInfo BeatLeader { get; set; }

        public LeaderboardPpInfo ScoreSaber { get; set; }

        public long Version { get; set; }

        public PersonalBestInfo? GetPersonalBest(PbSource source)
        {
            switch (source)
            {
                case PbSource.Local: return PersonalBest;
                case PbSource.BeatLeader: return BeatLeaderPersonalBest;
                case PbSource.ScoreSaber: return ScoreSaberPersonalBest;
                default: return null;
            }
        }
    }

    public sealed class HandStats
    {
        public int GoodCuts { get; set; }

        public int Misses { get; set; }

        public int BadCuts { get; set; }

        public long CutScoreSum { get; set; }

        public int CutScoreCount { get; set; }

        public long PreSwingSum { get; set; }

        public int PreSwingCount { get; set; }

        public long PostSwingSum { get; set; }

        public int PostSwingCount { get; set; }

        public long CenterSum { get; set; }

        public int CenterCount { get; set; }

        public int FullPreSwings { get; set; }

        public int FullPostSwings { get; set; }

        public double TimingDeviationSum { get; set; }

        public double TimingDeviationAbsSum { get; set; }

        public int TimingCount { get; set; }

        public double SaberSpeedSum { get; set; }

        public int SaberSpeedCount { get; set; }

        public System.Collections.Generic.List<float> RecentSaberSpeeds { get; } = new System.Collections.Generic.List<float>();

        public ColorValue? SaberColor { get; set; }
    }

    public enum PbSource
    {
        Local,
        BeatLeader,
        ScoreSaber,
    }

    public sealed class PersonalBestInfo
    {
        public bool Available { get; set; }

        public PbSource Source { get; set; } = PbSource.Local;

        public int Score { get; set; }

        public double Accuracy { get; set; }

        public int MaxCombo { get; set; }

        public bool FullCombo { get; set; }

        public string? StatusText { get; set; }

        public long? ScoreId { get; set; }

        public AccuracyLine? Line { get; set; }
    }

    public enum PpStatus
    {
        Loading,
        Ranked,
        Qualified,
        InEvent,
        Unranked,
        Loved,
        NotFound,
        Error,
        Disabled,
        Incompatible,
        Failed,
    }

    public sealed class LeaderboardPpInfo
    {
        public PpStatus Status { get; set; } = PpStatus.Loading;

        public double? Pp { get; set; }

        public double? FcPp { get; set; }

        public double? MaxPp { get; set; }

        public double? Stars { get; set; }

        public double? AccPp { get; set; }

        public double? PassPp { get; set; }

        public double? TechPp { get; set; }

        public bool IsApproximate { get; set; }
    }

    public sealed class MapInfo
    {
        public string SongName { get; set; } = string.Empty;

        public string SongSubName { get; set; } = string.Empty;

        public string SongAuthor { get; set; } = string.Empty;

        public string Mapper { get; set; } = string.Empty;

        public string DifficultyName { get; set; } = string.Empty;

        public string Characteristic { get; set; } = string.Empty;

        public float NoteJumpSpeed { get; set; }

        public float Bpm { get; set; }

        public string? Hash { get; set; }
    }
}
