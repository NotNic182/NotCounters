using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Core.Leaderboards;
using NotCounters.Util;
using SiraUtil.Logging;
using Zenject;

namespace NotCounters.Services
{
    internal sealed class PlayerAccountService : IPlayerAccountService, IInitializable, ITickable, IDisposable
    {
        public const double SettingsSettleSeconds = 1.0;

        public const double PlatformRetrySeconds = 2.0;

        public const int MaxPlatformAttempts = 30;

        public const double FirstErrorRetrySeconds = 30.0;

        public const double MaxErrorRetrySeconds = 600.0;

        private const string LogName = "PlayerAccounts";

        private readonly IConfigService _config;
        private readonly LeaderboardRequestScheduler _scheduler;
        private readonly SiraLog _log;
        private readonly IPlatformUserModel? _platform;
        private readonly Board _beatLeader = new Board(LeaderboardService.BeatLeader);
        private readonly Board _scoreSaber = new Board(LeaderboardService.ScoreSaber);

        private readonly Dictionary<string, PlayerLookupResult> _resolved = new Dictionary<string, PlayerLookupResult>(StringComparer.Ordinal);

        private double _settingsCheckAt = double.PositiveInfinity;

        private string? _lastRawInputs;

        private Task<UserInfo>? _userInfo;

        private bool _initialized;
        private bool _disposed;

        public PlayerAccountService(IConfigService config, LeaderboardRequestScheduler scheduler, SiraLog log, [InjectOptional] IPlatformUserModel? platform)
        {
            _config = config;
            _scheduler = scheduler;
            _log = log;
            _platform = platform;
        }

        public event Action? Changed;

        public PlayerAccount BeatLeader => _beatLeader.Account;

        public PlayerAccount ScoreSaber => _scoreSaber.Account;

        public void Initialize()
        {
            if (_initialized || _disposed)
            {
                return;
            }

            _initialized = true;
            _config.Changed += OnConfigChanged;
            if (_platform == null)
            {
                _log.Warn("IPlatformUserModel is not bound; leaderboard accounts can only come from the overrides in the settings.");
            }

            Safe.Invoke(LogName + ".Initialize", () =>
            {
                _lastRawInputs = RawInputs();
                EvaluateBoth(force: true);
            });
        }

        public void Refresh() =>
            Safe.Invoke(LogName + ".Refresh", () =>
            {
                if (!_initialized || _disposed)
                {
                    return;
                }

                _lastRawInputs = RawInputs();
                _settingsCheckAt = double.PositiveInfinity;
                EvaluateBoth(force: false);
            });

        public void Tick()
        {
            if (!_initialized || _disposed)
            {
                return;
            }

            double now = MonotonicClock.Now;
            if (now >= _settingsCheckAt)
            {
                _settingsCheckAt = double.PositiveInfinity;
                Safe.Invoke(LogName + ".SettingsChanged", () => EvaluateBoth(force: false));
            }

            RetryIfDue(_beatLeader, now);
            RetryIfDue(_scoreSaber, now);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_initialized)
            {
                _config.Changed -= OnConfigChanged;
            }

            _beatLeader.Reset(null);
            _scoreSaber.Reset(null);
            _resolved.Clear();
            Changed = null;
        }

        private void OnConfigChanged() =>
            Safe.Invoke(LogName + ".ConfigChanged", () =>
            {
                if (_disposed)
                {
                    return;
                }

                string raw = RawInputs();
                if (string.Equals(raw, _lastRawInputs, StringComparison.Ordinal))
                {
                    return;
                }

                _lastRawInputs = raw;
                if (!string.Equals(ReadInputs(_beatLeader.Service).Key, _beatLeader.InputKey, StringComparison.Ordinal)
                    || !string.Equals(ReadInputs(_scoreSaber.Service).Key, _scoreSaber.InputKey, StringComparison.Ordinal))
                {
                    _settingsCheckAt = MonotonicClock.Now + SettingsSettleSeconds;
                }
            });

        private void EvaluateBoth(bool force)
        {
            Evaluate(_beatLeader, force);
            Evaluate(_scoreSaber, force);
        }

        private void Evaluate(Board board, bool force)
        {
            BoardInputs inputs = ReadInputs(board.Service);
            if (!force && string.Equals(inputs.Key, board.InputKey, StringComparison.Ordinal))
            {
                return;
            }

            board.Reset(inputs);
            if (!inputs.ModEnabled)
            {
                Set(board, new PlayerAccount(PlayerAccountStatus.Disabled, message: PlayerAccountTexts.ModSwitchedOff));
                return;
            }

            if (!inputs.Enabled)
            {
                Set(board, new PlayerAccount(PlayerAccountStatus.Disabled, message: PlayerAccountTexts.Disabled));
                return;
            }

            if (inputs.Override.Kind == PlayerOverrideKind.Unsupported)
            {
                Set(board, new PlayerAccount(PlayerAccountStatus.NotFound, isOverride: true, message: PlayerAccountTexts.UnsupportedOverride));
                return;
            }

            Start(board);
        }

        private void Start(Board board, bool isErrorRetry = false)
        {
            BoardInputs? inputs = board.Inputs;
            if (inputs == null)
            {
                return;
            }

            bool isOverride = inputs.Override.Kind == PlayerOverrideKind.Query;
            string cacheKey = inputs.Key;
            ulong platformUserId = 0;
            if (!isOverride)
            {
                platformUserId = ReadPlatformUserId();
                if (platformUserId == 0)
                {
                    WaitForPlatformUser(board);
                    return;
                }

                cacheKey += "|" + platformUserId;
            }

            if (_resolved.TryGetValue(cacheKey, out PlayerLookupResult? cached))
            {
                Set(board, ToAccount(board.Service, cached));
                return;
            }

            board.CacheKey = cacheKey;
            board.Lookup = isOverride
                ? PlayerLookup.ForOverride(board.Service, inputs.BaseUrl, inputs.Override)
                : PlayerLookup.ForPlatformUser(board.Service, inputs.BaseUrl, platformUserId);
            if (!isErrorRetry || board.Account.Status != PlayerAccountStatus.Error)
            {
                Set(board, new PlayerAccount(PlayerAccountStatus.Detecting, isOverride: isOverride, message: PlayerAccountTexts.Detecting));
            }

            Continue(board);
        }

        private void WaitForPlatformUser(Board board)
        {
            board.PlatformAttempts++;
            if (_platform != null && board.PlatformAttempts < MaxPlatformAttempts)
            {
                board.RetryAt = MonotonicClock.Now + PlatformRetrySeconds;
                Set(board, new PlayerAccount(PlayerAccountStatus.Detecting, message: PlayerAccountTexts.WaitingForGameAccount));
                return;
            }

            _log.Debug($"{board.Service} account: the game's platform user id is unavailable; automatic detection stopped.");
            Set(board, new PlayerAccount(PlayerAccountStatus.NotFound, message: PlayerAccountTexts.GameAccountUnavailable));
        }

        private void Continue(Board board)
        {
            PlayerLookup? lookup = board.Lookup;
            if (lookup == null)
            {
                return;
            }

            if (lookup.IsComplete)
            {
                Complete(board, lookup.Result!);
                return;
            }

            int generation = board.Generation;
            _scheduler.Enqueue(board.Service, lookup.NextUrl!, response => OnResponse(board, generation, lookup, response));
        }

        private void OnResponse(Board board, int generation, PlayerLookup lookup, LeaderboardResponse response) =>
            Safe.Invoke(LogName + ".Response", () =>
            {
                if (_disposed || generation != board.Generation || !ReferenceEquals(lookup, board.Lookup))
                {
                    return;
                }

                lookup.Advance(ToLookupResponse(response), response.Http.Body);
                Continue(board);
            });

        private void Complete(Board board, PlayerLookupResult result)
        {
            board.Lookup = null;
            if (result.Outcome == PlayerLookupOutcome.Error)
            {
                board.ErrorCount++;
                double delay = Math.Min(MaxErrorRetrySeconds, FirstErrorRetrySeconds * Math.Pow(2, Math.Min(board.ErrorCount - 1, 16)));
                board.RetryAt = MonotonicClock.Now + delay;
                _log.Debug($"{board.Service} account lookup failed ({result.Error}); retrying in {delay:0} s.");
            }
            else
            {
                board.ErrorCount = 0;
                if (board.CacheKey != null)
                {
                    _resolved[board.CacheKey] = result;
                }

                _log.Debug($"{board.Service} account ({(result.IsOverride ? "override" : "detected")}): {result}");
            }

            Set(board, ToAccount(board.Service, result));
        }

        private void RetryIfDue(Board board, double now)
        {
            if (now < board.RetryAt)
            {
                return;
            }

            board.RetryAt = double.PositiveInfinity;
            Retry(board);
        }

        private void Retry(Board board)
        {
            Safe.Invoke(LogName + ".Retry", () => Start(board, isErrorRetry: board.ErrorCount > 0));
        }

        private ulong ReadPlatformUserId()
        {
            IPlatformUserModel? platform = _platform;
            if (platform == null)
            {
                return 0;
            }

            return Safe.Invoke(LogName + ".PlatformUser", () =>
            {
                Task<UserInfo> task = _userInfo ??= platform.GetUserInfo(CancellationToken.None);
                if (task.IsFaulted || task.IsCanceled)
                {
                    _userInfo = null;
                }

                return task.Status == TaskStatus.RanToCompletion && ulong.TryParse(task.Result?.platformUserId, out ulong id) ? id : 0UL;
            }, 0UL);
        }

        private string RawInputs()
        {
            GlobalSettings global = _config.Config?.Global ?? new GlobalSettings();
            return string.Join("\n", global.Enabled ? "1" : "0", global.EnableBeatLeader ? "1" : "0", global.EnableScoreSaber ? "1" : "0",
                global.BeatLeaderApiBaseUrl ?? string.Empty, global.ScoreSaberApiBaseUrl ?? string.Empty,
                global.BeatLeaderPlayerId ?? string.Empty, global.ScoreSaberPlayerId ?? string.Empty);
        }

        private BoardInputs ReadInputs(LeaderboardService service)
        {
            GlobalSettings global = _config.Config?.Global ?? new GlobalSettings();
            bool beatLeader = service == LeaderboardService.BeatLeader;
            bool enabled = beatLeader ? global.EnableBeatLeader : global.EnableScoreSaber;
            string baseUrl = beatLeader
                ? new BeatLeaderApi(global.BeatLeaderApiBaseUrl).BaseUrl
                : new ScoreSaberApi(global.ScoreSaberApiBaseUrl).BaseUrl;
            PlayerOverride input = PlayerOverride.Parse(beatLeader ? global.BeatLeaderPlayerId : global.ScoreSaberPlayerId);

            string key;
            if (!global.Enabled)
            {
                key = "mod-off";
            }
            else if (!enabled)
            {
                key = "off";
            }
            else
            {
                switch (input.Kind)
                {
                    case PlayerOverrideKind.Query:
                        key = "override|" + baseUrl + "|" + input.Query;
                        break;
                    case PlayerOverrideKind.Automatic:
                        key = "auto|" + baseUrl;
                        break;
                    default:
                        key = "unsupported";
                        break;
                }
            }

            return new BoardInputs(global.Enabled, enabled, baseUrl, input, key);
        }

        private static PlayerLookupResponse ToLookupResponse(LeaderboardResponse response)
        {
            switch (response.Outcome)
            {
                case LeaderboardRequestOutcome.Success:
                    return PlayerLookupResponse.Success;
                case LeaderboardRequestOutcome.NotFound:
                    return PlayerLookupResponse.NotFound;
                case LeaderboardRequestOutcome.Failed:
                    int status = response.Http.Status;
                    return status == 400 || status == 422
                        ? PlayerLookupResponse.Rejected
                        : PlayerLookupResponse.Failed;
                default:
                    return PlayerLookupResponse.Failed;
            }
        }

        private static PlayerAccount ToAccount(LeaderboardService service, PlayerLookupResult result)
        {
            switch (result.Outcome)
            {
                case PlayerLookupOutcome.Found when result.Player != null:
                    LeaderboardPlayer player = result.Player;
                    return new PlayerAccount(PlayerAccountStatus.Found, player.Id, player.Name, player.Rank, result.IsOverride,
                        PlayerAccountTexts.Found(player.Name, player.Rank, result.IsOverride));
                case PlayerLookupOutcome.Ambiguous:
                    return new PlayerAccount(PlayerAccountStatus.Ambiguous, isOverride: true, message: PlayerAccountTexts.Ambiguous(result.Query));
                case PlayerLookupOutcome.TooManyMatches:
                    return new PlayerAccount(PlayerAccountStatus.Ambiguous, isOverride: true, message: PlayerAccountTexts.TooManyMatches(result.Query));
                case PlayerLookupOutcome.NotFound:
                    return new PlayerAccount(PlayerAccountStatus.NotFound, isOverride: result.IsOverride,
                        message: result.IsOverride ? PlayerAccountTexts.NoMatch(result.Query) : PlayerAccountTexts.NoLinkedAccount);
                default:
                    return new PlayerAccount(PlayerAccountStatus.Error, isOverride: result.IsOverride, message: PlayerAccountTexts.Unreachable(service));
            }
        }

        private void Set(Board board, PlayerAccount account)
        {
            if (SameAccount(board.Account, account))
            {
                return;
            }

            board.Account = account;
            RaiseChanged();
        }

        private static bool SameAccount(PlayerAccount a, PlayerAccount b) =>
            a.Status == b.Status
            && a.IsOverride == b.IsOverride
            && a.Rank == b.Rank
            && string.Equals(a.Id, b.Id, StringComparison.Ordinal)
            && string.Equals(a.Name, b.Name, StringComparison.Ordinal)
            && string.Equals(a.Message, b.Message, StringComparison.Ordinal);

        private void RaiseChanged()
        {
            Action? handlers = Changed;
            if (handlers == null)
            {
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                var action = (Action)handler;
                Safe.Invoke(LogName + ".Changed:" + (action.Method.DeclaringType?.Name ?? "?"), action);
            }
        }

        private sealed class BoardInputs
        {
            public BoardInputs(bool modEnabled, bool enabled, string baseUrl, PlayerOverride input, string key)
            {
                ModEnabled = modEnabled;
                Enabled = enabled;
                BaseUrl = baseUrl;
                Override = input;
                Key = key;
            }

            public bool ModEnabled { get; }

            public bool Enabled { get; }

            public string BaseUrl { get; }

            public PlayerOverride Override { get; }

            public string Key { get; }
        }

        private sealed class Board
        {
            public Board(LeaderboardService service)
            {
                Service = service;
            }

            public LeaderboardService Service { get; }

            public PlayerAccount Account { get; set; } =
                new PlayerAccount(PlayerAccountStatus.Detecting, message: PlayerAccountTexts.Detecting);

            public BoardInputs? Inputs { get; private set; }

            public string? InputKey => Inputs?.Key;

            public int Generation { get; private set; }

            public PlayerLookup? Lookup { get; set; }

            public string? CacheKey { get; set; }

            public double RetryAt { get; set; } = double.PositiveInfinity;

            public int ErrorCount { get; set; }

            public int PlatformAttempts { get; set; }

            public void Reset(BoardInputs? inputs)
            {
                Inputs = inputs;
                Generation++;
                Lookup = null;
                CacheKey = null;
                RetryAt = double.PositiveInfinity;
                ErrorCount = 0;
                PlatformAttempts = 0;
            }
        }
    }
}
