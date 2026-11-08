using System;
using System.Collections.Generic;
using System.Globalization;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Core.Counters;
using NotCounters.Core.Leaderboards;
using NotCounters.Core.Scoring;
using NotCounters.Core.Stats;
using NotCounters.Util;
using SiraUtil.Logging;
using UnityEngine;
using Zenject;

namespace NotCounters.Gameplay
{
    internal sealed class GameplayStatsHub : StatsSourceBase, IInitializable, ITickable, IDisposable
    {
        private const string LogName = nameof(GameplayStatsHub);

        private const int MaxPendingBadCuts = 64;

        private readonly SiraLog _log;
        private readonly IScoreController _scoreController;
        private readonly IComboController _comboController;
        private readonly BeatmapObjectManager _beatmapObjectManager;
        private readonly PlayerHeadAndObstacleInteraction _obstacleInteraction;
        private readonly IGameEnergyCounter _energyCounter;
        private readonly AudioTimeSyncController _audioTimeSyncController;
        private readonly GameplayCoreSceneSetupData _sceneSetupData;
        private readonly IReadonlyBeatmapData _beatmapData;
        private readonly PauseController? _pauseController;
        private readonly BeatmapCallbacksController.InitData? _callbacksInitData;
        private readonly SongSpeedData? _songSpeedData;
        private readonly BeatmapObjectSpawnController.InitData? _spawnInitData;
        private readonly StandardLevelScenesTransitionSetupDataSO? _standardTransitionData;
        private readonly MissionGameplaySceneSetupData? _missionSetupData;
        private readonly PlayerDataModel? _playerDataModel;
        private readonly IConfigService? _configService;
        private readonly ILeaderboardDataService? _leaderboardDataService;
        private readonly IRemotePersonalBestService? _remotePersonalBestService;
        private readonly IPlayerAccountService? _playerAccounts;
        private readonly IPersonalBestLineService? _lineService;

        private readonly Dictionary<NoteData, Hand> _badCutSabers = new Dictionary<NoteData, Hand>();

        private readonly NoteResult _noteResult = new NoteResult();

        private ScoreTracker? _scoreTracker;
        private PpTracker? _ppTracker;
        private LeaderboardPpFeed? _leaderboardFeed;

        private IDisposable? _leaderboardSubscription;

        private readonly Dictionary<PbSource, RemotePersonalBestRequest> _remotePersonalBests = new Dictionary<PbSource, RemotePersonalBestRequest>(2);

        private LevelIdentity? _remotePersonalBestLevel;
        private bool _accountsSubscribed;

        private AccuracyLineRecorder? _lineRecorder;
        private SavedAccuracyLine? _savedLine;
        private IDisposable? _beatLeaderLineRequest;

        private bool _initialized;
        private bool _subscribed;
        private bool _disposed;

        private bool _dirty;

        private bool _noFail;
        private int _maxCombo;
        private bool _headInWall;
        private bool _energyReachedZero;
        private bool _energyPushed;
        private float _lastEnergy;
        private int _lastBatteryLives;
        private int _lastPublishedSongSecond = int.MinValue;
        private float _lastPublishRealtime = float.NegativeInfinity;
        private string? _reportedPpError;

        public GameplayStatsHub(
            SiraLog log,
            IScoreController scoreController,
            IComboController comboController,
            BeatmapObjectManager beatmapObjectManager,
            PlayerHeadAndObstacleInteraction obstacleInteraction,
            IGameEnergyCounter energyCounter,
            AudioTimeSyncController audioTimeSyncController,
            GameplayCoreSceneSetupData sceneSetupData,
            IReadonlyBeatmapData beatmapData,
            [InjectOptional] PauseController? pauseController,
            [InjectOptional] BeatmapCallbacksController.InitData? callbacksInitData,
            [InjectOptional] SongSpeedData? songSpeedData,
            [InjectOptional] BeatmapObjectSpawnController.InitData? spawnInitData,
            [InjectOptional] StandardLevelScenesTransitionSetupDataSO? standardTransitionData,
            [InjectOptional] MissionGameplaySceneSetupData? missionSetupData,
            [InjectOptional] PlayerDataModel? playerDataModel,
            [InjectOptional] IConfigService? configService,
            [InjectOptional] ILeaderboardDataService? leaderboardDataService,
            [InjectOptional] IRemotePersonalBestService? remotePersonalBestService,
            [InjectOptional] IPlayerAccountService? playerAccounts,
            [InjectOptional] IPersonalBestLineService? lineService)
            : base(LogName)
        {
            _log = log;
            _scoreController = scoreController;
            _comboController = comboController;
            _beatmapObjectManager = beatmapObjectManager;
            _obstacleInteraction = obstacleInteraction;
            _energyCounter = energyCounter;
            _audioTimeSyncController = audioTimeSyncController;
            _sceneSetupData = sceneSetupData;
            _beatmapData = beatmapData;
            _pauseController = pauseController;
            _callbacksInitData = callbacksInitData;
            _songSpeedData = songSpeedData;
            _spawnInitData = spawnInitData;
            _standardTransitionData = standardTransitionData;
            _missionSetupData = missionSetupData;
            _playerDataModel = playerDataModel;
            _configService = configService;
            _leaderboardDataService = leaderboardDataService;
            _remotePersonalBestService = remotePersonalBestService;
            _playerAccounts = playerAccounts;
            _lineService = lineService;
        }

        public void Initialize()
        {
            try
            {
                InitializeCore();
            }
            catch (Exception ex)
            {
                Safe.Report(LogName + ".Initialize", ex);
            }
        }

        public void Tick()
        {
            if (!_initialized || _disposed) return;
            try
            {
                float songTime = _audioTimeSyncController.songTime;
                Current.SongTime = songTime;
                PollWallExit(songTime);
                PollEnergy(songTime);

                bool songSecondChanged = Mathf.FloorToInt(songTime) != _lastPublishedSongSecond;
                bool realSecondElapsed = Time.unscaledTime - _lastPublishRealtime >= 1f;
                if (_dirty || songSecondChanged || realSecondElapsed)
                {
                    PublishSnapshot(songTime);
                }
            }
            catch (Exception ex)
            {
                Safe.Report(LogName + ".Tick", ex);
            }
        }

        public void Dispose()
        {
            _disposed = true;
            DetachServiceCallbacks();
            if (!_subscribed) return;
            _subscribed = false;
            try
            {
                Unsubscribe();
            }
            catch (Exception ex)
            {
                Safe.Report(LogName + ".Dispose", ex);
            }

            _badCutSabers.Clear();
        }

        private void InitializeCore()
        {
            StatsSnapshot snapshot = Current;
            GameplayModifiers modifiers = _sceneSetupData.gameplayModifiers;
            PracticeSettings? practice = _sceneSetupData.practiceSettings;
            GlobalSettings global = _configService?.Config.Global ?? new GlobalSettings();
            PlayMode mode = PlayMode.Detect(_sceneSetupData, _standardTransitionData, _missionSetupData);

            snapshot.IsPractice = mode.IsPractice;
            snapshot.IsPartyOrCampaign = mode.IsPartyOrCampaign;
            _noFail = modifiers.noFailOn0Energy;

            float startFilterTime = _callbacksInitData?.startFilterTime ?? practice?.startSongTime ?? 0f;
            snapshot.StartSongTime = startFilterTime;
            float effectiveSpeed = _songSpeedData?.speedMul ?? practice?.songSpeedMul ?? modifiers.songSpeedMul;
            snapshot.SongSpeed = effectiveSpeed;
            bool speedHasLeaderboardCode = PlaySpeed.TryGetSongSpeed(effectiveSpeed, out SongSpeed songSpeed);
            snapshot.SongLength = ResolveSongLength();
            snapshot.SongTime = _audioTimeSyncController.songTime;
            Safe.Invoke(LogName + ".MapInfo", () => MapInfoFactory.Fill(snapshot.Map, _sceneSetupData, _spawnInitData?.noteJumpMovementSpeed));
            Safe.Invoke(LogName + ".SaberColors", () => SaberColorReader.Fill(snapshot, _sceneSetupData));

            BeatmapNoteSummary summary = Safe.Invoke(
                LogName + ".CollectNotes",
                () => BeatmapNoteCollector.Collect(_beatmapData, startFilterTime),
                BeatmapNoteSummary.Empty);
            _scoreTracker = new ScoreTracker(summary, snapshot);
            _ppTracker = new PpTracker(snapshot) { BeatLeaderNoFailCountsAsZero = global.BeatLeaderNoFailCountsAsZero };
            _ppTracker.SetModifiers(ModifierMapper.ToModifierSet(modifiers, songSpeed));
            _leaderboardFeed = new LeaderboardPpFeed(_ppTracker, global.EnableBeatLeader, global.EnableScoreSaber);

            Subscribe();
            _dirty = true;
            _initialized = true;

            BeatmapKey key = _sceneSetupData.beatmapKey;
            LevelIdentityFactory.TryCreate(in key, out LevelIdentity? level);
            Safe.Invoke(
                LogName + ".LocalPersonalBest",
                () => PersonalBestReader.ReadLocal(snapshot.PersonalBest, _playerDataModel, in key, summary.FullMapMaxMultipliedScore, mode));
            HudLayout? layout = Safe.Invoke<HudLayout?>(
                LogName + ".Layout",
                () => _configService?.GetEffectiveLayout(EnvironmentName()),
                null);
            Safe.Invoke(LogName + ".AccuracyLines", () => StartAccuracyLines(key, mode, layout, summary.FullMapMaxMultipliedScore));
            Safe.Invoke(LogName + ".Leaderboards", () => RequestLeaderboards(level, modifiers, speedHasLeaderboardCode, mode, layout));
            Safe.Invoke(LogName + ".RemotePersonalBest", () => RequestRemotePersonalBest(level, mode, layout));

            _log.Debug($"{LogName}: {key.levelId} {snapshot.Map.Characteristic} {snapshot.Map.DifficultyName}; " +
                       $"{summary.TotalNotes} notes, {summary.TotalChainLinks} chain links, {summary.TotalBombs} bombs, " +
                       $"max {summary.FullMapMaxMultipliedScore}; speed={effectiveSpeed} practice={mode.IsPractice} " +
                       $"party={mode.IsParty} campaign={mode.IsCampaign} leaderboard={level?.ToString() ?? "none"}");
        }

        private void StartAccuracyLines(BeatmapKey key, PlayMode mode, HudLayout? layout, int maxScore)
        {
            if (_lineService == null || mode.IsPractice || mode.IsPartyOrCampaign || !LeaderboardDemand.WantsAccuracyLines(layout)) return;

            BeatmapCharacteristicSO? characteristic = key.beatmapCharacteristic;
            string mapKey = key.levelId + "_" + (characteristic != null ? characteristic.serializedName : "Unknown") + "_" + key.difficulty;
            _savedLine = _lineService.LoadSaved(mapKey);
            _lineRecorder = new AccuracyLineRecorder { MaxScore = maxScore };
            _lineService.StartRecording(mapKey, _lineRecorder);
            AttachSavedLine(Current.PersonalBest);
            string saved = _savedLine != null ? _savedLine.Line.Final.ToString("0.0000", CultureInfo.InvariantCulture) : "none";
            _log.Debug($"{LogName}: recording the accuracy line for {mapKey}; saved line ends at {saved}.");
        }

        private void AttachLines(PbSource source, PersonalBestInfo personalBest)
        {
            AttachSavedLine(personalBest);
            if (source != PbSource.BeatLeader || !personalBest.ScoreId.HasValue || _lineService == null) return;

            long scoreId = personalBest.ScoreId.Value;
            IDisposable? previous = _beatLeaderLineRequest;
            _beatLeaderLineRequest = null;
            if (previous != null) Safe.Invoke(LogName + ".DetachPbLine", previous.Dispose);
            _beatLeaderLineRequest = _lineService.RequestBeatLeader(scoreId, line => HandleBeatLeaderLine(scoreId, line));
        }

        private void AttachSavedLine(PersonalBestInfo? personalBest)
        {
            if (personalBest == null || personalBest.Line != null || _savedLine == null || !_savedLine.Matches(personalBest)) return;
            personalBest.Line = _savedLine.Line;
            _dirty = true;
        }

        private void HandleBeatLeaderLine(long scoreId, AccuracyLine? line)
        {
            if (_disposed || line == null) return;
            PersonalBestInfo personalBest = Current.BeatLeaderPersonalBest;
            if (!personalBest.Available || personalBest.ScoreId != scoreId) return;
            personalBest.Line = line;
            _dirty = true;
        }

        private float ResolveSongLength()
        {
            float clipLength = _audioTimeSyncController.songLength;
            if (clipLength > 0f) return clipLength;
            BeatmapLevel? level = _sceneSetupData.beatmapLevel;
            return level != null ? level.songDuration : 0f;
        }

        private string? EnvironmentName()
        {
            EnvironmentInfoSO? environment = _sceneSetupData.targetEnvironmentInfo;
            return environment != null ? environment.serializedName : null;
        }

        private void RequestLeaderboards(LevelIdentity? level, GameplayModifiers modifiers, bool speedHasLeaderboardCode, PlayMode mode, HudLayout? layout)
        {
            LeaderboardPpFeed feed = _leaderboardFeed!;
            if (ModifierMapper.HasModifierWithoutLeaderboardCode(modifiers))
            {
                feed.SetStatus(PpStatus.Incompatible);
                _log.Info($"{LogName}: a modifier without a leaderboard code is active (fast notes / saber clash); pp disabled.");
                return;
            }

            if (!speedHasLeaderboardCode)
            {
                feed.SetStatus(PpStatus.Incompatible);
                _log.Info($"{LogName}: practice speed {Current.SongSpeed} has no leaderboard code; pp disabled.");
                return;
            }

            if (level == null || mode.IsCampaign)
            {
                feed.SetStatus(PpStatus.NotFound);
                return;
            }

            if (!feed.AnyEnabled) return;

            if (layout != null && !LeaderboardDemand.Needs(layout))
            {
                _log.Debug($"{LogName}: no counter uses leaderboard data; nothing requested.");
                return;
            }

            if (_leaderboardDataService == null)
            {
                feed.SetStatus(PpStatus.Error);
                Safe.WarnOnce(LogName + ".NoLeaderboardService", $"{LogName}: no ILeaderboardDataService is bound; pp unavailable.");
                return;
            }

            _leaderboardSubscription = _leaderboardDataService.Request(level, HandleLeaderboardData);
        }

        private void RequestRemotePersonalBest(LevelIdentity? level, PlayMode mode, HudLayout? layout)
        {
            if (mode.IsPractice || mode.IsPartyOrCampaign) return;

            foreach (PbSource source in LeaderboardDemand.RemotePersonalBestSources(layout))
            {
                PersonalBestInfo? personalBest = Current.GetPersonalBest(source);
                if (personalBest == null || _remotePersonalBests.ContainsKey(source)) continue;

                if (level == null || _remotePersonalBestService == null)
                {
                    PersonalBestReader.SetUnavailable(personalBest, PersonalBestReader.NoPersonalBestText, source);
                    continue;
                }

                _remotePersonalBestLevel = level;
                _remotePersonalBests.Add(source, new RemotePersonalBestRequest());
                LoadRemotePersonalBest(source);
            }

            if (_remotePersonalBests.Count > 0 && _playerAccounts != null && !_accountsSubscribed)
            {
                _playerAccounts.Changed += HandleAccountsChanged;
                _accountsSubscribed = true;
            }

            _dirty = true;
        }

        private void LoadRemotePersonalBest(PbSource source)
        {
            LevelIdentity? level = _remotePersonalBestLevel;
            PersonalBestInfo? personalBest = Current.GetPersonalBest(source);
            if (level == null || _remotePersonalBestService == null || personalBest == null
                || !_remotePersonalBests.TryGetValue(source, out RemotePersonalBestRequest? request))
            {
                return;
            }

            IDisposable? previous = request.Subscription;
            request.Subscription = null;
            if (previous != null) Safe.Invoke(LogName + ".DetachRemotePb", previous.Dispose);

            request.AccountKey = AccountKey(source);
            PersonalBestReader.SetUnavailable(personalBest, PersonalBestReader.LoadingText, source);
            request.Subscription = _remotePersonalBestService.Request(level, source, remote => HandleRemotePersonalBest(source, remote));
            _dirty = true;
        }

        private void HandleAccountsChanged()
        {
            if (_disposed) return;
            try
            {
                var sources = new PbSource[_remotePersonalBests.Count];
                _remotePersonalBests.Keys.CopyTo(sources, 0);
                foreach (PbSource source in sources)
                {
                    if (_remotePersonalBests.TryGetValue(source, out RemotePersonalBestRequest? request)
                        && PersonalBestStatus.IsPending(Current.GetPersonalBest(source))
                        && !string.Equals(request.AccountKey, AccountKey(source), StringComparison.Ordinal))
                    {
                        LoadRemotePersonalBest(source);
                    }
                }
            }
            catch (Exception ex)
            {
                Safe.Report(LogName + ".AccountsChanged", ex);
            }
        }

        private string AccountKey(PbSource source)
        {
            IPlayerAccountService? accounts = _playerAccounts;
            if (accounts == null) return string.Empty;
            PlayerAccount account = source == PbSource.BeatLeader ? accounts.BeatLeader : accounts.ScoreSaber;
            return account.Status + "|" + account.Id;
        }

        private void DetachServiceCallbacks()
        {
            IDisposable? leaderboard = _leaderboardSubscription;
            _leaderboardSubscription = null;
            if (leaderboard != null) Safe.Invoke(LogName + ".DetachLeaderboard", leaderboard.Dispose);

            if (_accountsSubscribed && _playerAccounts != null)
            {
                _accountsSubscribed = false;
                IPlayerAccountService accounts = _playerAccounts;
                Safe.Invoke(LogName + ".DetachAccounts", () => accounts.Changed -= HandleAccountsChanged);
            }

            foreach (RemotePersonalBestRequest request in _remotePersonalBests.Values)
            {
                IDisposable? subscription = request.Subscription;
                request.Subscription = null;
                if (subscription != null) Safe.Invoke(LogName + ".DetachRemotePb", subscription.Dispose);
            }

            _remotePersonalBests.Clear();

            IDisposable? line = _beatLeaderLineRequest;
            _beatLeaderLineRequest = null;
            if (line != null) Safe.Invoke(LogName + ".DetachPbLine", line.Dispose);
        }

        private void Subscribe()
        {
            _scoreController.scoringForNoteStartedEvent += HandleScoringForNoteStarted;
            _scoreController.scoringForNoteFinishedEvent += HandleScoringForNoteFinished;
            _scoreController.scoreDidChangeEvent += HandleScoreDidChange;
            _scoreController.multiplierDidChangeEvent += HandleMultiplierDidChange;
            _comboController.comboDidChangeEvent += HandleComboDidChange;
            _beatmapObjectManager.noteWasCutEvent += HandleNoteWasCut;
            _obstacleInteraction.headDidEnterObstaclesEvent += HandleHeadDidEnterObstacles;
            _energyCounter.gameEnergyDidReach0Event += HandleEnergyDidReach0;
            if (_pauseController is not null) _pauseController.didPauseEvent += HandleDidPause;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            _scoreController.scoringForNoteStartedEvent -= HandleScoringForNoteStarted;
            _scoreController.scoringForNoteFinishedEvent -= HandleScoringForNoteFinished;
            _scoreController.scoreDidChangeEvent -= HandleScoreDidChange;
            _scoreController.multiplierDidChangeEvent -= HandleMultiplierDidChange;
            _comboController.comboDidChangeEvent -= HandleComboDidChange;
            _beatmapObjectManager.noteWasCutEvent -= HandleNoteWasCut;
            _obstacleInteraction.headDidEnterObstaclesEvent -= HandleHeadDidEnterObstacles;
            _energyCounter.gameEnergyDidReach0Event -= HandleEnergyDidReach0;
            if (_pauseController is not null) _pauseController.didPauseEvent -= HandleDidPause;
        }

        private void HandleScoringForNoteStarted(ScoringElement element)
        {
            try
            {
                NoteData? noteData = element.noteData;
                if (noteData == null) return;

                if (noteData.gameplayType == NoteData.GameplayType.Bomb)
                {
                    _scoreTracker!.OnBombResolved();
                    _dirty = true;
                    return;
                }

                NoteScoringKind kind = GameTypeMapping.ToScoringKind(noteData.scoringType);
                if (!ScoreDefinitions.IsScored(kind)) return;

                NoteResultKind outcome = element is GoodCutScoringElement ? NoteResultKind.GoodCut
                    : element is BadCutScoringElement ? NoteResultKind.BadCut
                    : NoteResultKind.Miss;
                _scoreTracker!.OnNoteStarted(kind, outcome);
                _dirty = true;
            }
            catch (Exception ex)
            {
                Safe.Report(LogName + ".NoteStarted", ex);
            }
        }

        private void HandleScoringForNoteFinished(ScoringElement element)
        {
            try
            {
                NoteData? noteData = element.noteData;

                if (noteData == null || noteData.gameplayType == NoteData.GameplayType.Bomb) return;

                NoteScoringKind kind = GameTypeMapping.ToScoringKind(noteData.scoringType);
                if (!ScoreDefinitions.IsScored(kind)) return;

                NoteResult result = _noteResult;
                result.Scoring = kind;
                result.SongTime = noteData.time;
                result.CutScore = element.cutScore;
                result.MaxCutScore = element.maxPossibleCutScore;
                result.Multiplier = element.multiplier;
                result.FcMultiplier = element.maxMultiplier;

                if (element is GoodCutScoringElement goodCut)
                {
                    IReadonlyCutScoreBuffer buffer = goodCut.cutScoreBuffer;
                    NoteCutInfo cutInfo = buffer.noteCutInfo;
                    result.Kind = NoteResultKind.GoodCut;
                    result.Hand = GameTypeMapping.ToHand(cutInfo.saberType);
                    result.BeforeCutScore = buffer.beforeCutScore;
                    result.AfterCutScore = buffer.afterCutScore;
                    result.CenterScore = buffer.centerDistanceCutScore;

                    result.TimeDeviation = -cutInfo.timeDeviation;
                    result.SaberSpeed = cutInfo.saberSpeed;
                }
                else
                {
                    bool badCut = element is BadCutScoringElement;
                    result.Kind = badCut ? NoteResultKind.BadCut : NoteResultKind.Miss;
                    result.Hand = GameTypeMapping.ToHand(noteData.colorType);
                    if (badCut && _badCutSabers.TryGetValue(noteData, out Hand saber))
                    {
                        _badCutSabers.Remove(noteData);
                        result.Hand = saber;
                    }

                    result.BeforeCutScore = 0;
                    result.AfterCutScore = 0;
                    result.CenterScore = 0;
                    result.TimeDeviation = float.NaN;
                    result.SaberSpeed = float.NaN;
                }

                _scoreTracker!.OnNoteFinished(result);
                _dirty = true;
            }
            catch (Exception ex)
            {
                Safe.Report(LogName + ".NoteFinished", ex);
            }
        }

        private void HandleNoteWasCut(NoteController noteController, in NoteCutInfo noteCutInfo)
        {
            try
            {
                NoteData? noteData = noteCutInfo.noteData;
                if (noteData == null) return;

                if (noteData.gameplayType == NoteData.GameplayType.Bomb)
                {
                    _scoreTracker!.OnBombHit();
                    _dirty = true;
                    return;
                }

                if (noteCutInfo.allIsOK || noteData.scoringType == NoteData.ScoringType.Ignore) return;

                if (_badCutSabers.Count >= MaxPendingBadCuts) _badCutSabers.Clear();
                _badCutSabers[noteData] = GameTypeMapping.ToHand(noteCutInfo.saberType);
            }
            catch (Exception ex)
            {
                Safe.Report(LogName + ".NoteWasCut", ex);
            }
        }

        private void HandleScoreDidChange(int multipliedScore, int modifiedScore)
        {
            try
            {
                _scoreTracker!.SetScores(
                    multipliedScore,
                    modifiedScore,
                    _scoreController.immediateMaxPossibleMultipliedScore,
                    _scoreController.immediateMaxPossibleModifiedScore);
                _dirty = true;
            }
            catch (Exception ex)
            {
                Safe.Report(LogName + ".ScoreDidChange", ex);
            }
        }

        private void HandleMultiplierDidChange(int multiplier, float normalizedProgress)
        {
            try
            {
                _scoreTracker!.SetMultiplier(multiplier, normalizedProgress);
                _dirty = true;
            }
            catch (Exception ex)
            {
                Safe.Report(LogName + ".MultiplierDidChange", ex);
            }
        }

        private void HandleComboDidChange(int combo)
        {
            try
            {
                if (combo > _maxCombo) _maxCombo = combo;
                _scoreTracker!.SetCombo(combo, _maxCombo);
                _dirty = true;
            }
            catch (Exception ex)
            {
                Safe.Report(LogName + ".ComboDidChange", ex);
            }
        }

        private void HandleHeadDidEnterObstacles()
        {
            try
            {
                _headInWall = true;
                _scoreTracker!.OnWallEnter(_audioTimeSyncController.songTime);
                _dirty = true;
            }
            catch (Exception ex)
            {
                Safe.Report(LogName + ".HeadDidEnterObstacles", ex);
            }
        }

        private void HandleEnergyDidReach0()
        {
            try
            {
                _energyReachedZero = true;
                if (_lineRecorder != null) _lineRecorder.Spoiled = true;
                PushEnergy(_audioTimeSyncController.songTime);
            }
            catch (Exception ex)
            {
                Safe.Report(LogName + ".EnergyDidReach0", ex);
            }
        }

        private void HandleDidPause()
        {
            try
            {
                _scoreTracker!.OnPause();
                _dirty = true;
            }
            catch (Exception ex)
            {
                Safe.Report(LogName + ".DidPause", ex);
            }
        }

        private void HandleLeaderboardData(LeaderboardDataResult result)
        {
            if (_disposed || _leaderboardFeed == null) return;
            try
            {
                if (_leaderboardFeed.Apply(result)) _dirty = true;
            }
            catch (Exception ex)
            {
                Safe.Report(LogName + ".LeaderboardData", ex);
            }
        }

        private void HandleRemotePersonalBest(PbSource source, PersonalBestInfo remote)
        {
            if (_disposed || remote == null) return;
            try
            {
                PersonalBestInfo? personalBest = Current.GetPersonalBest(source);
                if (personalBest == null) return;
                if (remote.Available)
                {
                    PersonalBestReader.CopyInto(personalBest, remote);
                    personalBest.Source = source;
                    if (_lineRecorder != null) AttachLines(source, personalBest);
                }
                else
                {
                    PersonalBestReader.SetUnavailable(personalBest, remote.StatusText ?? PersonalBestReader.NoPersonalBestText, source);
                }

                _dirty = true;
            }
            catch (Exception ex)
            {
                Safe.Report(LogName + ".RemotePersonalBest", ex);
            }
        }

        private void PollWallExit(float songTime)
        {
            if (!_headInWall || _obstacleInteraction.playerHeadIsInObstacle) return;
            _headInWall = false;
            _scoreTracker!.OnWallExit(songTime);
            _dirty = true;
        }

        private void PollEnergy(float songTime)
        {
            if (!_energyCounter.isInitialized) return;
            if (_energyPushed
                && _energyCounter.energy == _lastEnergy
                && _energyCounter.batteryEnergy == _lastBatteryLives)
            {
                return;
            }

            PushEnergy(songTime);
        }

        private void PushEnergy(float songTime)
        {
            if (!_energyCounter.isInitialized) return;

            bool battery = _energyCounter.energyType == GameplayModifiers.EnergyType.Battery;
            float energy = _energyCounter.energy;
            int lives = _energyCounter.batteryEnergy;
            int maxLives = battery ? _energyCounter.batteryLives : 0;
            bool levelFailed = _energyReachedZero && !_noFail;

            _scoreTracker!.SetEnergy(energy, battery, battery ? lives : 0, maxLives, songTime, levelFailed);
            _lastEnergy = energy;
            _lastBatteryLives = lives;
            _energyPushed = true;
            _dirty = true;
        }

        private void PublishSnapshot(float songTime)
        {
            _scoreTracker!.UpdateDerived();
            _lineRecorder?.Sample(songTime, Current.Accuracy, Current.MultipliedScore);
            if (_dirty)
            {
                _ppTracker!.Update();
                ReportPpError();
            }

            _dirty = false;
            _lastPublishedSongSecond = Mathf.FloorToInt(songTime);
            _lastPublishRealtime = Time.unscaledTime;
            Publish();
        }

        private void ReportPpError()
        {
            string? error = _ppTracker!.LastError;
            if (error == null || string.Equals(error, _reportedPpError, StringComparison.Ordinal)) return;
            _reportedPpError = error;
            _log.Warn($"{LogName}: {error}");
        }

        private sealed class RemotePersonalBestRequest
        {
            public IDisposable? Subscription { get; set; }

            public string AccountKey { get; set; } = string.Empty;
        }
    }
}
