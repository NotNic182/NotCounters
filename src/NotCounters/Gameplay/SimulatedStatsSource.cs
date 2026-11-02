using System;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Core.Editing;
using NotCounters.Core.Pp;
using NotCounters.Core.Preview;
using NotCounters.Core.Stats;
using NotCounters.Util;
using SiraUtil.Logging;
using UnityEngine;
using Zenject;

namespace NotCounters.Gameplay
{
    internal sealed class SimulatedStatsSource : StatsSourceBase, IInitializable, ITickable, IDisposable
    {
        private const string LogName = nameof(SimulatedStatsSource);

        private const float DefaultStartFraction = 0.25f;

        private readonly SiraLog _log;
        private readonly GameplayCoreSceneSetupData? _sceneSetupData;
        private readonly AudioTimeSyncController? _audioTimeSyncController;
        private readonly BeatmapObjectSpawnController.InitData? _spawnInitData;
        private readonly IConfigService? _config;
        private readonly PreviewClock _clock = new PreviewClock();

        private PreviewPlay? _play;
        private BeatLeaderDifficulty? _beatLeader;
        private ScoreSaberLeaderboard? _scoreSaber;
        private bool _beatLeaderEnabled = true;
        private bool _scoreSaberEnabled = true;
        private bool _initialized;
        private bool _disposed;
        private float _fallbackSongTime;

        public SimulatedStatsSource(
            SiraLog log,
            [InjectOptional] GameplayCoreSceneSetupData? sceneSetupData,
            [InjectOptional] AudioTimeSyncController? audioTimeSyncController,
            [InjectOptional] BeatmapObjectSpawnController.InitData? spawnInitData,
            [InjectOptional] IConfigService? config)
            : base(LogName)
        {
            _log = log;
            _sceneSetupData = sceneSetupData;
            _audioTimeSyncController = audioTimeSyncController;
            _spawnInitData = spawnInitData;
            _config = config;
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
                if (SyncClock())
                {
                    PublishSnapshot();
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
            _play = null;
            if (_config != null)
            {
                _config.Changed -= HandleConfigChanged;
            }
        }

        private void InitializeCore()
        {
            StatsSnapshot snapshot = Current;

            var play = new PreviewPlay(snapshot);
            _play = play;
            _beatLeader = SyntheticLeaderboards.CreateBeatLeader(play.ColorNotes, play.NoteSpanSeconds, play.FullMapMaxMultipliedScore);
            _scoreSaber = SyntheticLeaderboards.CreateScoreSaber(play.FullMapMaxMultipliedScore);

            snapshot.IsPractice = false;
            snapshot.IsPartyOrCampaign = false;
            snapshot.SongSpeed = 1f;
            snapshot.StartSongTime = 0f;
            if (_sceneSetupData != null)
            {
                MapInfoFactory.Fill(snapshot.Map, _sceneSetupData, _spawnInitData?.noteJumpMovementSpeed);
            }
            else
            {
                FillPlaceholderMap(snapshot.Map);
            }

            snapshot.SongLength = ResolveSongLength();
            _fallbackSongTime = _sceneSetupData?.practiceSettings?.startSongTime ?? snapshot.SongLength * DefaultStartFraction;
            SyncClock();

            ReadLeaderboardSettings();
            ApplyLeaderboards();
            if (_config != null)
            {
                _config.Changed += HandleConfigChanged;
            }

            _initialized = true;
            PublishSnapshot();
            _log.Debug($"{LogName}: showing the fixed preview snapshot ({PreviewPlay.SnapshotSongTime:0} s into a {PreviewPlay.MapLengthSeconds:0} s synthetic map) for '{snapshot.Map.SongName}' ({snapshot.SongLength:0.0} s).");
        }

        private float ResolveSongLength()
        {
            float length = _audioTimeSyncController != null ? _audioTimeSyncController.songLength : 0f;
            if (!(length > 0f) && _sceneSetupData?.beatmapLevel != null) length = _sceneSetupData.beatmapLevel.songDuration;
            return length > 0f && !float.IsInfinity(length) ? length : PreviewPlay.MapLengthSeconds;
        }

        private bool SyncClock()
        {
            float time = _audioTimeSyncController != null ? _audioTimeSyncController.songTime : _fallbackSongTime;
            if (float.IsNaN(time) || float.IsInfinity(time)) time = _fallbackSongTime;
            time = Mathf.Clamp(time, 0f, Current.SongLength);

            Current.SongTime = time;
            return _clock.Update(time);
        }

        private void PublishSnapshot()
        {
            _clock.MarkPublished(Current.SongTime);
            Publish();
        }

        private bool ReadLeaderboardSettings()
        {
            GlobalSettings? global = _config?.Config?.Global;
            bool beatLeader = global?.EnableBeatLeader ?? true;
            bool scoreSaber = global?.EnableScoreSaber ?? true;
            if (beatLeader == _beatLeaderEnabled && scoreSaber == _scoreSaberEnabled) return false;

            _beatLeaderEnabled = beatLeader;
            _scoreSaberEnabled = scoreSaber;
            return true;
        }

        private void ApplyLeaderboards() =>
            _play?.SetLeaderboards(_beatLeaderEnabled ? _beatLeader : null, _scoreSaberEnabled ? _scoreSaber : null);

        private void HandleConfigChanged()
        {
            if (!_initialized || _disposed || _play == null) return;
            try
            {
                if (ReadLeaderboardSettings())
                {
                    ApplyLeaderboards();
                }

                PublishSnapshot();
            }
            catch (Exception ex)
            {
                Safe.Report(LogName + ".ConfigChanged", ex);
            }
        }

        private static void FillPlaceholderMap(MapInfo map)
        {
            map.SongName = "Preview";
            map.SongSubName = string.Empty;
            map.SongAuthor = "NotCounters";
            map.Mapper = string.Empty;
            map.DifficultyName = "ExpertPlus";
            map.Characteristic = "Standard";
            map.Bpm = 120f;
            map.NoteJumpSpeed = 18f;
            map.Hash = null;
        }
    }
}
