using System;
using NotCounters.Contracts;
using NotCounters.Core.Counters;
using NotCounters.Core.Leaderboards;
using NotCounters.Gameplay;
using NotCounters.Util;
using SiraUtil.Logging;
using Zenject;

namespace NotCounters.Services
{
    internal sealed class LevelSelectionPrefetcher : IInitializable, ITickable, IDisposable
    {
        public const double SettleSeconds = 0.35;

        private readonly ILeaderboardDataService _leaderboards;
        private readonly SiraLog _log;
        private readonly StandardLevelDetailViewController? _levelDetail;
        private readonly IConfigService? _config;

        private LevelIdentity? _pending;
        private double _pendingDueAt;
        private bool _subscribed;

        public LevelSelectionPrefetcher(ILeaderboardDataService leaderboards, SiraLog log,
            [InjectOptional] StandardLevelDetailViewController? levelDetail, [InjectOptional] IConfigService? config)
        {
            _leaderboards = leaderboards;
            _log = log;
            _levelDetail = levelDetail;
            _config = config;
        }

        public void Initialize()
        {
            if (_levelDetail == null)
            {
                _log.Warn("StandardLevelDetailViewController is not bound; leaderboard data will be fetched at song start instead.");
                return;
            }

            _levelDetail.didChangeDifficultyBeatmapEvent += OnDifficultyChanged;
            _levelDetail.didChangeContentEvent += OnContentChanged;
            _subscribed = true;
        }

        public void Tick()
        {
            if (_pending == null || MonotonicClock.Now < _pendingDueAt)
            {
                return;
            }

            LevelIdentity level = _pending;
            _pending = null;

            if (!IsModEnabled() || !IsLeaderboardDataShown())
            {
                return;
            }

            Prefetch(level);
        }

        public void Dispose()
        {
            if (_subscribed && _levelDetail != null)
            {
                _levelDetail.didChangeDifficultyBeatmapEvent -= OnDifficultyChanged;
                _levelDetail.didChangeContentEvent -= OnContentChanged;
            }

            _subscribed = false;
            _pending = null;
        }

        private void Prefetch(LevelIdentity level)
        {
            Safe.Invoke("Prefetch.Request", () => _leaderboards.Request(level, null));
        }

        private void OnDifficultyChanged(StandardLevelDetailViewController controller) =>
            Safe.Invoke("Prefetch.DifficultyChanged", () => Schedule(controller));

        private void OnContentChanged(StandardLevelDetailViewController? controller, StandardLevelDetailViewController.ContentType contentType) =>
            Safe.Invoke("Prefetch.ContentChanged", () =>
            {
                if (contentType == StandardLevelDetailViewController.ContentType.OwnedAndReady)
                {
                    Schedule(controller);
                }
                else if (contentType == StandardLevelDetailViewController.ContentType.Inactive)
                {
                    _pending = null;
                }
            });

        private void Schedule(StandardLevelDetailViewController? controller)
        {
            if (controller == null || !IsModEnabled() || !IsLeaderboardDataShown())
            {
                _pending = null;
                return;
            }

            BeatmapKey key = controller.beatmapKey;
            if (!key.IsValid() || !LevelIdentityFactory.TryCreate(in key, out LevelIdentity? level))
            {
                _pending = null;
                return;
            }

            _pending = level;
            _pendingDueAt = MonotonicClock.Now + SettleSeconds;
        }

        private bool IsModEnabled()
        {
            IConfigService? config = _config;
            return config != null && Safe.Invoke("Prefetch.ReadEnabled", () => config.Config.Global.Enabled, false);
        }

        private bool IsLeaderboardDataShown()
        {
            IConfigService? config = _config;
            return config != null && Safe.Invoke("Prefetch.ReadDemand", () => LeaderboardDemand.Needs(config.ActiveProfile), false);
        }
    }
}
