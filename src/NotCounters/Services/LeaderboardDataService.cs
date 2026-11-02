using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Core.Counters;
using NotCounters.Core.Leaderboards;
using NotCounters.Core.Pp;
using NotCounters.Util;
using SiraUtil.Logging;
using Zenject;

namespace NotCounters.Services
{
    internal sealed class LeaderboardDataService : ILeaderboardDataService, IInitializable, ITickable, IDisposable
    {
        public static readonly TimeSpan ErrorRetryDelay = TimeSpan.FromSeconds(30);

        public static readonly TimeSpan StalePendingAge = TimeSpan.FromMinutes(30);

        private const int BackSolveScoreCount = 10;

        private readonly IConfigService _config;
        private readonly LeaderboardRequestScheduler _scheduler;
        private readonly SiraLog _log;
        private readonly LeaderboardDiskCache _disk;

        private readonly Action _tickDisk;

        private readonly LeaderboardCache<LeaderboardCacheKey, BeatLeaderPart> _beatLeaderParts =
            new LeaderboardCache<LeaderboardCacheKey, BeatLeaderPart>(LeaderboardCache<LeaderboardCacheKey, BeatLeaderPart>.SessionLifetime);

        private readonly LeaderboardCache<LeaderboardCacheKey, ScoreSaberPart> _scoreSaberParts =
            new LeaderboardCache<LeaderboardCacheKey, ScoreSaberPart>(LeaderboardCache<LeaderboardCacheKey, ScoreSaberPart>.SessionLifetime);

        private readonly Dictionary<LevelIdentity, PendingLevel> _inProgress = new Dictionary<LevelIdentity, PendingLevel>();
        private readonly Dictionary<int, CurveState> _curves = new Dictionary<int, CurveState>();
        private bool _disposed;

        public LeaderboardDataService(IConfigService config, LeaderboardRequestScheduler scheduler, SiraLog log)
        {
            _config = config;
            _scheduler = scheduler;
            _log = log;
            _disk = new LeaderboardDiskCache(StoragePaths.LeaderboardCacheFile, log);
            _tickDisk = _disk.Tick;
        }

        public IDisposable Request(LevelIdentity level, Action<LeaderboardDataResult>? onUpdate) =>
            Safe.Invoke("Leaderboards.Request", () => RequestCore(level, onUpdate), ListenerSubscription.None);

        public bool TryGetCached(LevelIdentity level, out LeaderboardDataResult result)
        {
            result = null!;
            try
            {
                if (level == null)
                {
                    return false;
                }

                if (!level.IsValid)
                {
                    result = CreateNotFound(level);
                    return true;
                }

                GlobalSettings settings = _config.Config.Global;
                var candidate = new LeaderboardDataResult(level);
                if (TryResolveBeatLeader(level, settings, candidate) && TryResolveScoreSaber(level, settings, candidate))
                {
                    result = candidate;
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Safe.Report("Leaderboards.TryGetCached", ex);
                result = null!;
                return false;
            }
        }

        public void Initialize() => Safe.Invoke("Leaderboards.Initialize", () =>
        {
            _disk.BeginLoad();
            if (LeaderboardDataWillBeShown())
            {
                Task.Run(WarmUpParsers);
            }
        });

        public void Tick()
        {
            if (!_disposed)
            {
                Safe.Invoke("Leaderboards.Tick", _tickDisk);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _disk.Flush();
            _inProgress.Clear();
            _curves.Clear();
        }

        private TimeSpan DiskCacheLifetime
        {
            get
            {
                double hours = _config.Config.Global.LeaderboardCacheHours;
                if (!(hours > 0))
                {
                    return TimeSpan.Zero;
                }

                TimeSpan max = LeaderboardCache<LeaderboardCacheKey, BeatLeaderPart>.PersistedMaxLifetime;
                return hours >= max.TotalHours ? max : TimeSpan.FromHours(hours);
            }
        }

        private bool LeaderboardDataWillBeShown()
        {
            GlobalSettings settings = _config.Config.Global;
            return settings.Enabled
                   && (settings.EnableBeatLeader || settings.EnableScoreSaber)
                   && LeaderboardDemand.Needs(_config.ActiveProfile);
        }

        private void WarmUpParsers()
        {
            try
            {
                double started = MonotonicClock.Now;
                int parsed = LeaderboardParserWarmUp.Run();
                _log.Debug($"Leaderboard parsers compiled on a worker: {parsed}/{LeaderboardParserWarmUp.SampleCount} samples in {(MonotonicClock.Now - started) * 1000.0:0} ms.");
            }
            catch (Exception)
            {
            }
        }

        private IDisposable RequestCore(LevelIdentity? level, Action<LeaderboardDataResult>? onUpdate)
        {
            if (level == null || _disposed)
            {
                return ListenerSubscription.None;
            }

            if (!level.IsValid)
            {
                Deliver(onUpdate, CreateNotFound(level));
                return ListenerSubscription.None;
            }

            if (_inProgress.TryGetValue(level, out PendingLevel? running))
            {
                if (MonotonicClock.Now - running.StartedAt < StalePendingAge.TotalSeconds)
                {
                    IDisposable joined = Subscribe(running, onUpdate);
                    if (onUpdate != null && running.HasFinalPart)
                    {
                        Deliver(onUpdate, running.Result);
                    }

                    return joined;
                }

                _log.Warn($"Leaderboard data for {level} has been pending for over {StalePendingAge.TotalMinutes:0} minutes; requesting it again.");
                _inProgress.Remove(level);
            }

            GlobalSettings settings = _config.Config.Global;
            var result = new LeaderboardDataResult(level);
            bool fetchBeatLeader = !TryResolveBeatLeader(level, settings, result);
            bool fetchScoreSaber = !TryResolveScoreSaber(level, settings, result);
            if (!fetchBeatLeader && !fetchScoreSaber)
            {
                Deliver(onUpdate, result);
                return ListenerSubscription.None;
            }

            var pending = new PendingLevel(level, result, MonotonicClock.Now);
            IDisposable subscription = Subscribe(pending, onUpdate);
            _inProgress[level] = pending;
            bool resolvedFromCache = pending.HasFinalPart;

            if (fetchBeatLeader)
            {
                try
                {
                    StartBeatLeader(pending, settings);
                }
                catch (Exception ex)
                {
                    Safe.Report("Leaderboards.StartBeatLeader", ex);
                    FailBeatLeader(pending);
                }
            }

            if (fetchScoreSaber)
            {
                try
                {
                    StartScoreSaber(pending, settings);
                }
                catch (Exception ex)
                {
                    Safe.Report("Leaderboards.StartScoreSaber", ex);
                    FailScoreSaber(pending, job: null);
                }
            }

            if (resolvedFromCache && !pending.Notified)
            {
                NotifyListeners(pending);
            }

            return subscription;
        }

        private static IDisposable Subscribe(PendingLevel pending, Action<LeaderboardDataResult>? onUpdate)
        {
            if (onUpdate == null)
            {
                return ListenerSubscription.None;
            }

            pending.Listeners.Add(onUpdate);
            return new ListenerSubscription<LeaderboardDataResult>(pending.Listeners, onUpdate);
        }

        private bool TryResolveBeatLeader(LevelIdentity level, GlobalSettings settings, LeaderboardDataResult result)
        {
            if (!settings.EnableBeatLeader)
            {
                result.BeatLeaderStatus = LeaderboardFetchStatus.Disabled;
                return true;
            }

            if (_beatLeaderParts.TryGet(LeaderboardCacheKey.For(LeaderboardService.BeatLeader, level), out BeatLeaderPart? part))
            {
                part.ApplyTo(result);
                return true;
            }

            return false;
        }

        private bool TryResolveScoreSaber(LevelIdentity level, GlobalSettings settings, LeaderboardDataResult result)
        {
            if (!settings.EnableScoreSaber)
            {
                result.ScoreSaberStatus = LeaderboardFetchStatus.Disabled;
                return true;
            }

            if (_scoreSaberParts.TryGet(LeaderboardCacheKey.For(LeaderboardService.ScoreSaber, level), out ScoreSaberPart? part))
            {
                part.ApplyTo(result);
                return true;
            }

            return false;
        }

        private void NotifyListeners(PendingLevel pending)
        {
            pending.Notified = true;
            LeaderboardDataResult result = pending.Result;
            Action<LeaderboardDataResult>[] listeners = pending.Listeners.ToArray();
            if (result.IsComplete)
            {
                if (IsCurrent(pending))
                {
                    _inProgress.Remove(pending.Level);
                }

                pending.Listeners.Clear();
            }

            foreach (Action<LeaderboardDataResult> listener in listeners)
            {
                Deliver(listener, result);
            }
        }

        private bool IsCurrent(PendingLevel pending) =>
            _inProgress.TryGetValue(pending.Level, out PendingLevel? current) && ReferenceEquals(current, pending);

        private void ReleaseIfComplete(PendingLevel pending)
        {
            if (pending.Result.IsComplete && IsCurrent(pending))
            {
                NotifyListeners(pending);
            }
        }

        private static void Deliver(Action<LeaderboardDataResult>? listener, LeaderboardDataResult result)
        {
            if (listener != null)
            {
                Safe.Invoke("Leaderboards.Callback", () => listener(result));
            }
        }

        private static LeaderboardDataResult CreateNotFound(LevelIdentity level) => new LeaderboardDataResult(level)
        {
            BeatLeaderStatus = LeaderboardFetchStatus.NotFound,
            ScoreSaberStatus = LeaderboardFetchStatus.NotFound,
        };

        private void StartBeatLeader(PendingLevel pending, GlobalSettings settings)
        {
            LevelIdentity level = pending.Level;
            string? url = new BeatLeaderApi(settings.BeatLeaderApiBaseUrl).LeaderboardUrl(level);
            if (url == null)
            {
                CompleteBeatLeader(pending, BeatLeaderPart.Failed(LeaderboardFetchStatus.NotFound), cacheFor: null);
                return;
            }

            string diskKey = BeatLeaderDiskKey(level);
            if (TryReadDisk(diskKey, out string cachedBody)
                && BeatLeaderApiParser.TryParseLeaderboard(cachedBody, out BeatLeaderDifficulty? cached, out _))
            {
                CompleteBeatLeader(pending, BeatLeaderPart.Found(cached), LeaderboardCache<LeaderboardCacheKey, BeatLeaderPart>.SessionLifetime);
                return;
            }

            _scheduler.Enqueue(LeaderboardService.BeatLeader, url, response =>
            {
                try
                {
                    OnBeatLeaderResponse(pending, diskKey, response);
                }
                catch (Exception ex)
                {
                    Safe.Report("Leaderboards.BeatLeaderResponse", ex);
                    FailBeatLeader(pending);
                }
            });
        }

        private void OnBeatLeaderResponse(PendingLevel pending, string diskKey, LeaderboardResponse response)
        {
            if (_disposed)
            {
                return;
            }

            switch (response.Outcome)
            {
                case LeaderboardRequestOutcome.Success:
                    if (BeatLeaderApiParser.TryParseLeaderboard(response.Http.Body, out BeatLeaderDifficulty? difficulty, out string? error))
                    {
                        WriteDisk(diskKey, response.Http.Body);
                        _log.Debug($"BeatLeader {pending.Level}: status {difficulty.StatusKind}, stars {difficulty.Stars?.ToString("0.00", CultureInfo.InvariantCulture) ?? "-"}.");
                        CompleteBeatLeader(pending, BeatLeaderPart.Found(difficulty), LeaderboardCache<LeaderboardCacheKey, BeatLeaderPart>.SessionLifetime);
                    }
                    else
                    {
                        _log.Warn($"BeatLeader response for {pending.Level} could not be parsed: {error}");
                        CompleteBeatLeader(pending, BeatLeaderPart.Failed(LeaderboardFetchStatus.ParseError), ErrorRetryDelay);
                    }

                    break;
                case LeaderboardRequestOutcome.NotFound:
                    CompleteBeatLeader(pending, BeatLeaderPart.Failed(LeaderboardFetchStatus.NotFound), LeaderboardCache<LeaderboardCacheKey, BeatLeaderPart>.SessionLifetime);
                    break;
                case LeaderboardRequestOutcome.Dropped:
                    CompleteBeatLeader(pending, BeatLeaderPart.Failed(LeaderboardFetchStatus.NetworkError), cacheFor: null);
                    break;
                default:
                    CompleteBeatLeader(pending, BeatLeaderPart.Failed(LeaderboardFetchStatus.NetworkError), ErrorRetryDelay);
                    break;
            }
        }

        private void CompleteBeatLeader(PendingLevel pending, BeatLeaderPart part, TimeSpan? cacheFor)
        {
            if (pending.Result.BeatLeaderStatus != LeaderboardFetchStatus.Pending)
            {
                return;
            }

            if (cacheFor.HasValue)
            {
                _beatLeaderParts.Set(LeaderboardCacheKey.For(LeaderboardService.BeatLeader, pending.Level), part, cacheFor.Value);
            }

            part.ApplyTo(pending.Result);
            NotifyListeners(pending);
        }

        private void FailBeatLeader(PendingLevel pending) =>
            Safe.Invoke("Leaderboards.FailBeatLeader", () =>
            {
                CompleteBeatLeader(pending, BeatLeaderPart.Failed(LeaderboardFetchStatus.ParseError), ErrorRetryDelay);
                ReleaseIfComplete(pending);
            });

        private void StartScoreSaber(PendingLevel pending, GlobalSettings settings)
        {
            var job = new ScoreSaberJob(pending, new ScoreSaberApi(settings.ScoreSaberApiBaseUrl));
            LevelIdentity level = pending.Level;
            string? url = job.Api.LeaderboardUrl(level);
            if (url == null)
            {
                CompleteScoreSaber(job, ScoreSaberPart.Failed(LeaderboardFetchStatus.NotFound), cacheFor: null);
                return;
            }

            if (TryReadDisk(ScoreSaberV2DiskKey(level), out string v2Body)
                && ScoreSaberApiParser.TryParseLeaderboardV2(v2Body, out ScoreSaberLeaderboard? fromV2, out _))
            {
                OnScoreSaberLeaderboard(job, fromV2);
                return;
            }

            if (TryReadDisk(ScoreSaberV1DiskKey(level), out string v1Body)
                && ScoreSaberApiParser.TryParseInfoV1(v1Body, out ScoreSaberLeaderboard? fromV1, out _))
            {
                OnScoreSaberLeaderboard(job, fromV1);
                return;
            }

            _scheduler.Enqueue(LeaderboardService.ScoreSaber, url, GuardScoreSaber(job, OnScoreSaberV2Response));
        }

        private void OnScoreSaberV2Response(ScoreSaberJob job, LeaderboardResponse response)
        {
            switch (response.Outcome)
            {
                case LeaderboardRequestOutcome.Success:
                    if (ScoreSaberApiParser.TryParseLeaderboardV2(response.Http.Body, out ScoreSaberLeaderboard? leaderboard, out string? error))
                    {
                        WriteDisk(ScoreSaberV2DiskKey(job.Level), response.Http.Body);
                        OnScoreSaberLeaderboard(job, leaderboard);
                    }
                    else
                    {
                        _log.Warn($"ScoreSaber v2 response for {job.Level} could not be parsed ({error}); trying the v1 info endpoint.");
                        RequestScoreSaberV1(job);
                    }

                    break;
                default:
                    CompleteScoreSaberFromOutcome(job, response.Outcome);
                    break;
            }
        }

        private void RequestScoreSaberV1(ScoreSaberJob job)
        {
            string? url = job.Api.LeaderboardInfoV1Url(job.Level);
            if (url == null)
            {
                CompleteScoreSaber(job, ScoreSaberPart.Failed(LeaderboardFetchStatus.ParseError), ErrorRetryDelay);
                return;
            }

            _scheduler.Enqueue(LeaderboardService.ScoreSaber, url, GuardScoreSaber(job, OnScoreSaberV1Response));
        }

        private void OnScoreSaberV1Response(ScoreSaberJob job, LeaderboardResponse response)
        {
            switch (response.Outcome)
            {
                case LeaderboardRequestOutcome.Success:
                    if (ScoreSaberApiParser.TryParseInfoV1(response.Http.Body, out ScoreSaberLeaderboard? leaderboard, out string? error))
                    {
                        WriteDisk(ScoreSaberV1DiskKey(job.Level), response.Http.Body);
                        OnScoreSaberLeaderboard(job, leaderboard);
                    }
                    else
                    {
                        _log.Warn($"ScoreSaber v1 response for {job.Level} could not be parsed: {error}");
                        CompleteScoreSaber(job, ScoreSaberPart.Failed(LeaderboardFetchStatus.ParseError), ErrorRetryDelay);
                    }

                    break;
                default:
                    CompleteScoreSaberFromOutcome(job, response.Outcome);
                    break;
            }
        }

        private void OnScoreSaberLeaderboard(ScoreSaberJob job, ScoreSaberLeaderboard leaderboard)
        {
            job.Leaderboard = leaderboard;
            bool ranked = leaderboard.IsRanked;
            bool needsCurves = ranked || leaderboard.Status == ScoreSaberLeaderboardStatus.Qualified;

            job.OutstandingSteps = 1 + (ranked ? 1 : 0) + (needsCurves ? 1 : 0);
            if (ranked)
            {
                FetchTopScores(job);
            }

            if (needsCurves)
            {
                EnsureCurves(leaderboard.RealmId, job.Api, () => GuardedStepDone(job));
            }

            GuardedStepDone(job);
        }

        private void FetchTopScores(ScoreSaberJob job)
        {
            long leaderboardId = job.Leaderboard!.Id;
            string diskKey = ScoreSaberScoresDiskKey(leaderboardId);
            if (TryReadDisk(diskKey, out string cachedBody)
                && ScoreSaberApiParser.TryParseScores(cachedBody, out IReadOnlyList<ScoreSaberScoreSample> cachedScores, out _))
            {
                job.Scores = cachedScores;
                GuardedStepDone(job);
                return;
            }

            string? url = job.Api.LeaderboardScoresUrl(leaderboardId, BackSolveScoreCount);
            if (url == null)
            {
                GuardedStepDone(job);
                return;
            }

            _scheduler.Enqueue(LeaderboardService.ScoreSaber, url, GuardScoreSaber(job, (j, response) =>
            {
                if (response.Outcome == LeaderboardRequestOutcome.Success)
                {
                    if (ScoreSaberApiParser.TryParseScores(response.Http.Body, out IReadOnlyList<ScoreSaberScoreSample> scores, out string? error))
                    {
                        WriteDisk(diskKey, response.Http.Body);
                        j.Scores = scores;
                    }
                    else
                    {
                        _log.Warn($"ScoreSaber scores for leaderboard {leaderboardId} could not be parsed ({error}); maxPP falls back to stars.");
                    }
                }

                ScoreSaberStepDone(j);
            }));
        }

        private void ScoreSaberStepDone(ScoreSaberJob job)
        {
            if (job.Finished)
            {
                return;
            }

            if (--job.OutstandingSteps == 0)
            {
                FinalizeScoreSaber(job);
            }
        }

        private void GuardedStepDone(ScoreSaberJob job)
        {
            try
            {
                ScoreSaberStepDone(job);
            }
            catch (Exception ex)
            {
                Safe.Report("Leaderboards.ScoreSaberFinalize", ex);
                FailScoreSaber(job.Pending, job);
            }
        }

        private void FinalizeScoreSaber(ScoreSaberJob job)
        {
            ScoreSaberLeaderboard leaderboard = job.Leaderboard!;
            ScoreSaberCurves curves = ScoreSaberCurves.Choose(LiveCurves(leaderboard.RealmId), out _);
            double? maxPp = null;
            bool approximate = false;

            if (leaderboard.IsRanked)
            {
                ScoreSaberMaxPpResult solved = ScoreSaberPpCalculator.BackSolveMaxPp(job.Scores, leaderboard, curves);
                if (solved.Source != ScoreSaberMaxPpSource.None && solved.MaxPp > 0)
                {
                    maxPp = solved.MaxPp;
                    approximate = solved.IsApproximate;
                }
                else
                {
                    maxPp = ScoreSaberPpCalculator.ResolveMaxPp(leaderboard);
                    approximate = true;
                }

                if (solved.SanityCheckFailed)
                {
                    _log.Warn($"ScoreSaber {job.Level}: {solved.Note} (back-solved {solved.BackSolvedValue?.ToString("0.###", CultureInfo.InvariantCulture)}, stars {solved.StarsMaxPp.ToString("0.##", CultureInfo.InvariantCulture)}).");
                }
                else if (solved.Note != null)
                {
                    _log.Debug($"ScoreSaber {job.Level}: {solved.Note}");
                }
            }
            else if (leaderboard.Status == ScoreSaberLeaderboardStatus.Qualified)
            {
                maxPp = ScoreSaberPpCalculator.ResolveMaxPp(leaderboard);
                approximate = maxPp.HasValue;
            }

            _log.Debug($"ScoreSaber {job.Level}: {leaderboard.Status}, stars {leaderboard.Stars.ToString("0.00", CultureInfo.InvariantCulture)}, maxPP {maxPp?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-"}{(approximate ? " (approx.)" : string.Empty)}.");
            CompleteScoreSaber(job, ScoreSaberPart.Found(leaderboard, curves, maxPp, approximate),
                LeaderboardCache<LeaderboardCacheKey, ScoreSaberPart>.SessionLifetime);
        }

        private void CompleteScoreSaberFromOutcome(ScoreSaberJob job, LeaderboardRequestOutcome outcome)
        {
            switch (outcome)
            {
                case LeaderboardRequestOutcome.NotFound:
                    CompleteScoreSaber(job, ScoreSaberPart.Failed(LeaderboardFetchStatus.NotFound), LeaderboardCache<LeaderboardCacheKey, ScoreSaberPart>.SessionLifetime);
                    break;
                case LeaderboardRequestOutcome.Dropped:
                    CompleteScoreSaber(job, ScoreSaberPart.Failed(LeaderboardFetchStatus.NetworkError), cacheFor: null);
                    break;
                default:
                    CompleteScoreSaber(job, ScoreSaberPart.Failed(LeaderboardFetchStatus.NetworkError), ErrorRetryDelay);
                    break;
            }
        }

        private void CompleteScoreSaber(ScoreSaberJob job, ScoreSaberPart part, TimeSpan? cacheFor)
        {
            if (job.Finished)
            {
                return;
            }

            job.Finished = true;
            CompleteScoreSaber(job.Pending, part, cacheFor);
        }

        private void CompleteScoreSaber(PendingLevel pending, ScoreSaberPart part, TimeSpan? cacheFor)
        {
            if (pending.Result.ScoreSaberStatus != LeaderboardFetchStatus.Pending)
            {
                return;
            }

            if (cacheFor.HasValue)
            {
                _scoreSaberParts.Set(LeaderboardCacheKey.For(LeaderboardService.ScoreSaber, pending.Level), part, cacheFor.Value);
            }

            part.ApplyTo(pending.Result);
            NotifyListeners(pending);
        }

        private void FailScoreSaber(PendingLevel pending, ScoreSaberJob? job) =>
            Safe.Invoke("Leaderboards.FailScoreSaber", () =>
            {
                if (job != null)
                {
                    job.Finished = true;
                }

                CompleteScoreSaber(pending, ScoreSaberPart.Failed(LeaderboardFetchStatus.ParseError), ErrorRetryDelay);
                ReleaseIfComplete(pending);
            });

        private Action<LeaderboardResponse> GuardScoreSaber(ScoreSaberJob job, Action<ScoreSaberJob, LeaderboardResponse> handler) =>
            response =>
            {
                if (_disposed || job.Finished)
                {
                    return;
                }

                try
                {
                    handler(job, response);
                }
                catch (Exception ex)
                {
                    Safe.Report("Leaderboards.ScoreSaberResponse", ex);
                    FailScoreSaber(job.Pending, job);
                }
            };

        private ScoreSaberCurves? LiveCurves(int realmId) =>
            _curves.TryGetValue(NormalizeRealm(realmId), out CurveState? state) ? state.Live : null;

        private void EnsureCurves(int realmId, ScoreSaberApi api, Action onReady)
        {
            int realm = NormalizeRealm(realmId);
            if (!_curves.TryGetValue(realm, out CurveState? state))
            {
                state = new CurveState();
                _curves[realm] = state;
            }

            if (state.Live != null || (!state.Fetching && MonotonicClock.Now < state.RetryNotBefore))
            {
                onReady();
                return;
            }

            state.Waiters.Add(onReady);
            if (state.Fetching)
            {
                return;
            }

            string diskKey = ScoreSaberCurveDiskKey(realm);
            if (TryReadDisk(diskKey, out string cachedBody)
                && ScoreSaberApiParser.TryParseCurves(cachedBody, out ScoreSaberCurves? cached, out _))
            {
                SetCurves(realm, state, cached);
                return;
            }

            state.Fetching = true;
            _scheduler.Enqueue(LeaderboardService.ScoreSaber, api.PpCurveUrl(realm), response =>
            {
                state.Fetching = false;
                if (_disposed)
                {
                    return;
                }

                try
                {
                    OnCurveResponse(realm, state, diskKey, response);
                }
                catch (Exception ex)
                {
                    Safe.Report("Leaderboards.CurveResponse", ex);
                }
                finally
                {
                    ReleaseCurveWaiters(state);
                }
            });
        }

        private void OnCurveResponse(int realm, CurveState state, string diskKey, LeaderboardResponse response)
        {
            string? error = null;
            if (response.Outcome == LeaderboardRequestOutcome.Success
                && ScoreSaberApiParser.TryParseCurves(response.Http.Body, out ScoreSaberCurves? live, out error))
            {
                WriteDisk(diskKey, response.Http.Body);
                SetCurves(realm, state, live);
                return;
            }

            if (response.Outcome != LeaderboardRequestOutcome.Dropped)
            {
                state.RetryNotBefore = MonotonicClock.Now + ErrorRetryDelay.TotalSeconds;
            }

            _log.Warn($"ScoreSaber pp curve for realm {realm} unavailable ({error ?? response.Reason}); using the embedded {ScoreSaberCurves.SnapshotDate} snapshot.");
        }

        private void SetCurves(int realm, CurveState state, ScoreSaberCurves live)
        {
            state.Live = live;
            if (live.DiffersFromSnapshot)
            {
                _log.Info($"ScoreSaber pp curve for realm {realm} differs from the embedded {ScoreSaberCurves.SnapshotDate} snapshot; using the live curve.");
            }

            ReleaseCurveWaiters(state);
        }

        private static void ReleaseCurveWaiters(CurveState state)
        {
            Action[] waiters = state.Waiters.ToArray();
            state.Waiters.Clear();
            foreach (Action waiter in waiters)
            {
                Safe.Invoke("Leaderboards.CurveWaiter", waiter);
            }
        }

        private static int NormalizeRealm(int realmId) => realmId > 0 ? realmId : 1;

        private bool TryReadDisk(string key, out string body)
        {
            body = string.Empty;
            TimeSpan lifetime = DiskCacheLifetime;
            return lifetime > TimeSpan.Zero && _disk.TryGet(key, lifetime, out body);
        }

        private void WriteDisk(string key, string? body)
        {
            if (DiskCacheLifetime > TimeSpan.Zero)
            {
                _disk.Put(key, body);
            }
        }

        private static string BeatLeaderDiskKey(LevelIdentity level) => ("bl/leaderboard/" + level).ToLowerInvariant();

        private static string ScoreSaberV2DiskKey(LevelIdentity level) => ("ss/leaderboard-v2/" + level).ToLowerInvariant();

        private static string ScoreSaberV1DiskKey(LevelIdentity level) => ("ss/leaderboard-v1/" + level).ToLowerInvariant();

        private static string ScoreSaberScoresDiskKey(long leaderboardId) =>
            "ss/scores/" + leaderboardId.ToString(CultureInfo.InvariantCulture);

        private static string ScoreSaberCurveDiskKey(int realm) => "ss/pp-curve/" + realm.ToString(CultureInfo.InvariantCulture);

        private sealed class PendingLevel
        {
            public PendingLevel(LevelIdentity level, LeaderboardDataResult result, double startedAt)
            {
                Level = level;
                Result = result;
                StartedAt = startedAt;
            }

            public LevelIdentity Level { get; }

            public double StartedAt { get; }

            public LeaderboardDataResult Result { get; }

            public List<Action<LeaderboardDataResult>> Listeners { get; } = new List<Action<LeaderboardDataResult>>();

            public bool Notified { get; set; }

            public bool HasFinalPart =>
                Result.BeatLeaderStatus != LeaderboardFetchStatus.Pending || Result.ScoreSaberStatus != LeaderboardFetchStatus.Pending;
        }

        private sealed class ScoreSaberJob
        {
            public ScoreSaberJob(PendingLevel pending, ScoreSaberApi api)
            {
                Pending = pending;
                Api = api;
            }

            public PendingLevel Pending { get; }

            public LevelIdentity Level => Pending.Level;

            public ScoreSaberApi Api { get; }

            public ScoreSaberLeaderboard? Leaderboard { get; set; }

            public IReadOnlyList<ScoreSaberScoreSample>? Scores { get; set; }

            public int OutstandingSteps { get; set; }

            public bool Finished { get; set; }
        }

        private sealed class CurveState
        {
            public ScoreSaberCurves? Live { get; set; }

            public bool Fetching { get; set; }

            public double RetryNotBefore { get; set; }

            public List<Action> Waiters { get; } = new List<Action>();
        }

        private sealed class BeatLeaderPart
        {
            private BeatLeaderPart(LeaderboardFetchStatus status, BeatLeaderDifficulty? data)
            {
                Status = status;
                Data = data;
            }

            public LeaderboardFetchStatus Status { get; }

            public BeatLeaderDifficulty? Data { get; }

            public static BeatLeaderPart Found(BeatLeaderDifficulty data) => new BeatLeaderPart(LeaderboardFetchStatus.Success, data);

            public static BeatLeaderPart Failed(LeaderboardFetchStatus status) => new BeatLeaderPart(status, null);

            public void ApplyTo(LeaderboardDataResult result)
            {
                result.BeatLeader = Data;
                result.BeatLeaderStatus = Status;
            }
        }

        private sealed class ScoreSaberPart
        {
            private ScoreSaberPart(LeaderboardFetchStatus status, ScoreSaberLeaderboard? leaderboard, ScoreSaberCurves? curves,
                double? maxPp, bool maxPpApproximate)
            {
                Status = status;
                Leaderboard = leaderboard;
                Curves = curves;
                MaxPp = maxPp;
                MaxPpApproximate = maxPpApproximate;
            }

            public LeaderboardFetchStatus Status { get; }

            public ScoreSaberLeaderboard? Leaderboard { get; }

            public ScoreSaberCurves? Curves { get; }

            public double? MaxPp { get; }

            public bool MaxPpApproximate { get; }

            public static ScoreSaberPart Found(ScoreSaberLeaderboard leaderboard, ScoreSaberCurves curves, double? maxPp, bool approximate) =>
                new ScoreSaberPart(LeaderboardFetchStatus.Success, leaderboard, curves, maxPp, approximate);

            public static ScoreSaberPart Failed(LeaderboardFetchStatus status) => new ScoreSaberPart(status, null, null, null, false);

            public void ApplyTo(LeaderboardDataResult result)
            {
                result.ScoreSaber = Leaderboard;
                result.ScoreSaberCurves = Curves;
                result.ScoreSaberMaxPp = MaxPp;
                result.ScoreSaberMaxPpApproximate = MaxPpApproximate;
                result.ScoreSaberStatus = Status;
            }
        }
    }
}
