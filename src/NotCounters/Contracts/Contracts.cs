using System;
using NotCounters.Core.Config;
using NotCounters.Core.Editing;
using NotCounters.Core.Leaderboards;
using NotCounters.Core.Pp;
using NotCounters.Core.Stats;
using UnityEngine;

namespace NotCounters.Contracts
{
    public interface IConfigService
    {
        HudConfig Config { get; }

        HudProfile ActiveProfile { get; }

        bool IsReadOnly { get; }

        bool LastSaveFailed { get; }

        HudLayout GetEffectiveLayout(string? environmentSerializedName);

        HudLayout GetOrCreateEnvironmentLayout(string environmentSerializedName);

        bool RemoveEnvironmentOverride(string environmentSerializedName);

        void SetActiveProfile(string profileId);

        HudConfig CreateSnapshot();

        void Restore(HudConfig snapshot);

        void MarkDirty();

        void SaveNow();

        ConfigLoadNotice TakeLoadNotice();

        void KeepBeforeEditCopy(HudConfig snapshot);

        void DropBeforeEditCopy();

        void DropBeforeEditCopyAfterNextSave();

        event Action? Changed;
    }

    public interface IStatsSource
    {
        StatsSnapshot Current { get; }

        event Action? Updated;
    }

    public enum LeaderboardFetchStatus
    {
        Pending,
        Success,
        NotFound,
        NetworkError,
        ParseError,
        Disabled,
    }

    public sealed class LeaderboardDataResult
    {
        public LeaderboardDataResult(LevelIdentity level)
        {
            Level = level;
        }

        public LevelIdentity Level { get; }

        public LeaderboardFetchStatus BeatLeaderStatus { get; set; } = LeaderboardFetchStatus.Pending;

        public BeatLeaderDifficulty? BeatLeader { get; set; }

        public LeaderboardFetchStatus ScoreSaberStatus { get; set; } = LeaderboardFetchStatus.Pending;

        public ScoreSaberLeaderboard? ScoreSaber { get; set; }

        public ScoreSaberCurves? ScoreSaberCurves { get; set; }

        public double? ScoreSaberMaxPp { get; set; }

        public bool ScoreSaberMaxPpApproximate { get; set; }

        public bool IsComplete =>
            BeatLeaderStatus != LeaderboardFetchStatus.Pending && ScoreSaberStatus != LeaderboardFetchStatus.Pending;
    }

    public interface ILeaderboardDataService
    {
        IDisposable Request(LevelIdentity level, Action<LeaderboardDataResult>? onUpdate);

        bool TryGetCached(LevelIdentity level, out LeaderboardDataResult result);
    }

    public enum PlayerAccountStatus
    {
        Detecting,
        Found,
        NotFound,
        Ambiguous,
        Error,
        Disabled,
    }

    public sealed class PlayerAccount
    {
        public PlayerAccount(PlayerAccountStatus status, string? id = null, string? name = null, int? rank = null, bool isOverride = false, string? message = null)
        {
            Status = status;
            Id = id;
            Name = name;
            Rank = rank;
            IsOverride = isOverride;
            Message = message;
        }

        public PlayerAccountStatus Status { get; }

        public string? Id { get; }

        public string? Name { get; }

        public int? Rank { get; }

        public bool IsOverride { get; }

        public string? Message { get; }
    }

    public interface IPlayerAccountService
    {
        PlayerAccount BeatLeader { get; }

        PlayerAccount ScoreSaber { get; }

        void Refresh();

        event Action? Changed;
    }

    public interface IRemotePersonalBestService
    {
        IDisposable Request(LevelIdentity level, PbSource source, Action<PersonalBestInfo> onResult);
    }

    public interface IEditSessionState
    {
        bool IsActive { get; }

        string EnvironmentSerializedName { get; }

        HudConfig? ConfigAtStart { get; }

        void Begin(string environmentSerializedName, HudConfig configAtStart);

        void End();

        event Action? StateChanged;
    }

    public interface IHudAnchorProvider
    {
        Transform Anchor { get; }

        string EnvironmentSerializedName { get; }

        AnchorPose ToAnchor(CounterAttachment attachment, Vector3 position, Quaternion rotation, float scale);

        AnchorPose FromAnchor(CounterAttachment attachment, AnchorPose pose);

        float? FloorHeight { get; }

        Vector3 EyePosition { get; }

        Vector2? EnergyBarSpan { get; }
    }

    public struct AnchorPose
    {
        public AnchorPose(Vector3 position, Quaternion rotation, float scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public Vector3 Position;

        public Quaternion Rotation;

        public float Scale;
    }

    public enum EditableElementKind
    {
        Vanilla,
        Counter,
    }

    public interface IEditableElement
    {
        string Id { get; }

        string DisplayName { get; }

        EditableElementKind Kind { get; }

        Transform Root { get; }

        bool IsVisible { get; }

        Vector2 GetWorldSize();

        Vector2 GetLocalContentCentre();

        AnchorPose GetPose();

        void SetPose(AnchorPose pose);

        void CommitToConfig(HudLayout editableLayout);

        void ResetToDefault(HudLayout editableLayout);

        void SetVisibleInLayout(HudLayout editableLayout, bool visible);
    }

    public interface IAttachableElement
    {
        void Reattach(CounterSettings settings, CounterAttachment attachment);
    }

    public interface IHudFollowElement
    {
        void SetMoveWithHud(HudLayout editableLayout, bool moveWithHud);
    }

    public interface IEditableElementRegistry
    {
        System.Collections.Generic.IReadOnlyList<IEditableElement> Elements { get; }

        void Register(IEditableElement element);

        void Unregister(IEditableElement element);

        event Action? ElementsChanged;
    }
}
