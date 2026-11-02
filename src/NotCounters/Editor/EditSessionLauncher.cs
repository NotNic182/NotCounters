using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IPA.Loader;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Core.Editing;
using NotCounters.Util;
using SiraUtil.Logging;
using Zenject;

namespace NotCounters.Editor
{
    internal sealed class EditSessionLauncher : IDisposable
    {
        internal const string GameMode = "NotCountersEditor";

        private const string StandardCharacteristic = "Standard";
        private const string Degree360Characteristic = "360Degree";

        private const string PauseMenuBackButtonText = "Exit NotCounters";

        private const int MaxEntitlementChecks = 40;

        private static readonly TimeSpan EntitlementQueryTimeout = TimeSpan.FromSeconds(5);

        private const float MaxStartFraction = 0.9f;

        private const string SongCorePluginId = "SongCore";

        private const string SongCoreLoaderTypeName = "SongCore.Loader";
        private const string SongCoreLoadingPropertyName = "AreSongsLoading";

        private const string SongCoreInstanceFieldName = "Instance";
        private const string SongCoreScanTaskFieldName = "_loadingTask";

        private const string SongCoreProgressPropertyName = "LoadingProgress";

        private const string MainMenuLeftMessage = "HUD editor launch cancelled: the main menu was left while the launch was waiting.";

        private readonly SiraLog _log;
        private readonly IConfigService _config;
        private readonly EditSessionState _state;
        private readonly EnvironmentCatalog _catalog;
        private readonly EnvironmentsListModel _environments;
        private readonly MenuTransitionsHelper _transitions;
        private readonly GameScenesManager _scenes;
        private readonly BeatmapLevelsModel _levels;
        private readonly PlayerDataModel _playerData;
        private readonly BeatmapCharacteristicCollection? _characteristics;
        private readonly MainMenuViewController? _mainMenu;

        private CancellationTokenSource? _launchCancellation;
        private bool _earlyBindingsSubscribed;
        private bool _transitionWatchSubscribed;

        private bool _startingTransition;
        private bool _disposed;

        private bool _retryAvailable;

        private bool _audioInvalidAtFinish;

        private StandardLevelScenesTransitionSetupDataSO? _watchedSetupData;

        private bool _songCoreProbeResolved;
        private Func<bool>? _songCoreLoading;
        private FieldInfo? _songCoreInstanceField;
        private FieldInfo? _songCoreScanTaskField;
        private Func<float>? _songCoreProgress;

        public EditSessionLauncher(
            SiraLog log,
            IConfigService config,
            EditSessionState state,
            EnvironmentCatalog catalog,
            EnvironmentsListModel environments,
            MenuTransitionsHelper transitions,
            GameScenesManager scenes,
            BeatmapLevelsModel levels,
            PlayerDataModel playerData,
            [InjectOptional] BeatmapCharacteristicCollection? characteristics,
            [InjectOptional] MainMenuViewController? mainMenu)
        {
            _log = log;
            _config = config;
            _state = state;
            _catalog = catalog;
            _environments = environments;
            _transitions = transitions;
            _scenes = scenes;
            _levels = levels;
            _playerData = playerData;
            _characteristics = characteristics;
            _mainMenu = mainMenu;
        }

        public event Action? WaitingChanged;

        public bool IsLaunching { get; private set; }

        public bool IsWaiting { get; private set; }

        public bool StartFromMenu()
        {
            try
            {
                if (IsLaunching)
                {
                    _log.Info("The HUD editor is already starting; it opens by itself as soon as SongCore has finished loading songs.");
                    return true;
                }

                var environment = _catalog.FindOrDefault(_config.Config.Global.PreviewEnvironment);
                if (environment == null)
                {
                    _log.Error("Cannot start the HUD editor: the game's environment list is not available.");
                    return false;
                }

                _config.SaveNow();
                return StartSession(environment.SerializedName);
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to start the HUD editor from the menu button: {ex}");
                return false;
            }
        }

        public bool StartSession(string environmentSerializedName, HudConfig? configAtStart = null) =>
            StartSessionCore(environmentSerializedName, configAtStart, isRetry: false);

        private bool StartSessionCore(string environmentSerializedName, HudConfig? configAtStart, bool isRetry)
        {
            try
            {
                if (_disposed || _state.IsActive || IsLaunching)
                {
                    _log.Warn("A HUD edit session is already running or starting.");
                    return false;
                }

                if (_scenes.isInTransition)
                {
                    _log.Warn("Cannot start the HUD editor during a scene transition.");
                    return false;
                }

                var environment = _catalog.Find(environmentSerializedName);
                if (environment == null)
                {
                    _log.Warn($"Cannot start the HUD editor: unknown or unsupported environment '{environmentSerializedName}'.");
                    return false;
                }

                var launchContainer = ReadCurrentContainer();

                IsLaunching = true;
                _launchCancellation?.Dispose();
                _launchCancellation = new CancellationTokenSource();
                WatchOtherTransitions(true);

                var launch = LaunchAsync(environment, configAtStart, launchContainer, isRetry, _launchCancellation.Token);
                if (launch.IsCompleted)
                {
                    return launch.Result;
                }

                SetWaiting(true);
                ReportLateFailure(launch);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to start the HUD editor: {ex}");
                IsLaunching = false;
                WatchOtherTransitions(false);
                SetWaiting(false);
                EndSession();
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Safe.Invoke("EditSessionLauncher.Dispose", () =>
            {
                WatchOtherTransitions(false);
                _launchCancellation?.Cancel();
                _launchCancellation?.Dispose();
                _launchCancellation = null;
                EndSession();
            });
        }

        private async Task<bool> LaunchAsync(
            EnvironmentOption environment,
            HudConfig? configAtStart,
            DiContainer? launchContainer,
            bool isRetry,
            CancellationToken cancellation)
        {
            try
            {
                var songLoadingWait = WaitForSongLoadingAsync(isRetry, cancellation);
                var waited = !songLoadingWait.IsCompleted;
                if (!await songLoadingWait)
                {
                    return false;
                }

                var entitlementCache = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var candidate in EnumerateCandidates(environment.Is360))
                {
                    if (!entitlementCache.TryGetValue(candidate.Level.levelID, out var owned))
                    {
                        if (entitlementCache.Count >= MaxEntitlementChecks)
                        {
                            break;
                        }

                        var ownedQuery = IsOwnedAsync(candidate.Level, cancellation);
                        waited |= !ownedQuery.IsCompleted;
                        owned = await ownedQuery;
                        entitlementCache[candidate.Level.levelID] = owned;
                    }

                    if (cancellation.IsCancellationRequested || _disposed)
                    {
                        return false;
                    }

                    if (!owned)
                    {
                        continue;
                    }

                    if (_state.IsActive || _scenes.isInTransition)
                    {
                        _log.Warn("HUD editor launch abandoned: another scene transition started first.");
                        return false;
                    }

                    if (!IsStillCurrent(launchContainer))
                    {
                        _log.Warn("HUD editor launch abandoned: another scene was opened while the level was resolving.");
                        return false;
                    }

                    if (waited && HasLeftMainMenu())
                    {
                        _log.Info(MainMenuLeftMessage);
                        return false;
                    }

                    if (TryStartLevel(environment, candidate, configAtStart, isRetry))
                    {
                        return true;
                    }
                }

                _log.Error($"Cannot start the HUD editor: no owned OST level with a usable characteristic was found for '{environment.SerializedName}'.");
                return false;
            }
            catch (Exception ex)
            {
                _log.Error($"HUD editor launch failed: {ex}");
                EndSession();
                return false;
            }
            finally
            {
                IsLaunching = false;
                WatchOtherTransitions(false);
                SetWaiting(false);
            }
        }

        private void SetWaiting(bool waiting)
        {
            if (IsWaiting == waiting)
            {
                return;
            }

            IsWaiting = waiting;
            Safe.Invoke("EditSessionLauncher.WaitingChanged", () => WaitingChanged?.Invoke());
        }

        private bool HasLeftMainMenu()
        {
            var mainMenu = _mainMenu;
            if (mainMenu is null)
            {
                return false;
            }

            return Safe.Invoke("EditSessionLauncher.MainMenuShown", () => mainMenu == null || !mainMenu.isActivated, false);
        }

        private void WatchOtherTransitions(bool watch)
        {
            if (watch == _transitionWatchSubscribed)
            {
                return;
            }

            if (watch)
            {
                _scenes.transitionDidStartEvent += HandleTransitionDidStart;
            }
            else
            {
                _scenes.transitionDidStartEvent -= HandleTransitionDidStart;
            }

            _transitionWatchSubscribed = watch;
        }

        private void HandleTransitionDidStart(GameScenesManager.SceneTransitionType transitionType, float minDuration) =>
            Safe.Invoke("EditSessionLauncher.TransitionDidStart", () =>
            {
                if (_startingTransition || !IsLaunching)
                {
                    return;
                }

                _log.Info($"HUD editor launch cancelled: a {transitionType} scene transition started while the level was resolving.");
                _launchCancellation?.Cancel();
            });

        private DiContainer? ReadCurrentContainer() =>
            Safe.Invoke<DiContainer?>("EditSessionLauncher.CurrentContainer", () => _scenes.currentScenesContainer, null);

        private bool IsStillCurrent(DiContainer? launchContainer) =>
            launchContainer == null || ReferenceEquals(ReadCurrentContainer(), launchContainer);

        private async Task<bool> WaitForSongLoadingAsync(bool isRetry, CancellationToken cancellation)
        {
            var wait = new SongLoadingWait(
                minimumSeconds: isRetry ? EditLaunchRecovery.SettleSeconds : 0d,
                settleSeconds: EditLaunchRecovery.SettleSeconds,
                timeoutSeconds: EditLaunchRecovery.SongLoadingTimeoutSeconds,
                finalStepTimeoutSeconds: EditLaunchRecovery.SongLoadingFinalStepTimeoutSeconds);
            var clock = Stopwatch.StartNew();
            var announced = false;
            var announcedFinalStep = false;
            while (true)
            {
                var loading = IsSongCoreLoading(out var finalStep);
                var elapsed = clock.Elapsed.TotalSeconds;
                switch (wait.Next(loading, finalStep, elapsed))
                {
                    case SongLoadingWaitStep.Proceed:
                        if (wait.SawLoading)
                        {
                            _log.Info($"SongCore finished loading songs; starting the HUD editor ({elapsed:0.0} s after the request).");
                        }

                        return true;

                    case SongLoadingWaitStep.TimedOut:
                        if (finalStep)
                        {
                            _log.Warn($"SongCore's last song-loading step is still running after {wait.FinalStepTimeoutSeconds:0} s; starting the HUD editor anyway.");
                        }
                        else
                        {
                            _log.Warn($"SongCore is still scanning song folders after {wait.TimeoutSeconds:0} s; starting the HUD editor anyway (starting the level cancels the scan, and SongCore resumes it in the menu).");
                        }

                        return true;
                }

                if (loading && !announced)
                {
                    announced = true;
                    _log.Info("SongCore is loading songs; the HUD editor starts as soon as it has finished (its last step unloads the audio of a level that is loading at that moment).");
                }

                if (finalStep && !announcedFinalStep && elapsed >= wait.TimeoutSeconds)
                {
                    announcedFinalStep = true;
                    _log.Info($"SongCore is still in its last song-loading step after {wait.TimeoutSeconds:0} s; that step cannot be interrupted, so the HUD editor keeps waiting for it (at most {wait.FinalStepTimeoutSeconds:0} s).");
                }

                if (SynchronizationContext.Current == null)
                {
                    _log.Debug("No main-thread synchronization context; starting the HUD editor without waiting.");
                    return true;
                }

                await Task.Yield();
                if (cancellation.IsCancellationRequested || _disposed)
                {
                    return false;
                }

                if (HasLeftMainMenu())
                {
                    _log.Info(MainMenuLeftMessage);
                    return false;
                }
            }
        }

        private bool IsSongCoreLoading(out bool finalStep)
        {
            finalStep = false;
            if (!_songCoreProbeResolved)
            {
                _songCoreProbeResolved = true;
                ResolveSongCoreProbe();
            }

            var loading = _songCoreLoading;
            if (loading == null)
            {
                return false;
            }

            try
            {
                if (!loading())
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"Could not read SongCore's song-loading state; the HUD editor no longer waits for it: {ex.Message}");
                _songCoreLoading = null;
                return false;
            }

            finalStep = IsSongCoreInFinalStep();
            return true;
        }

        private bool IsSongCoreInFinalStep()
        {
            try
            {
                var instanceField = _songCoreInstanceField;
                var scanField = _songCoreScanTaskField;
                if (instanceField != null && scanField != null)
                {
                    var loader = instanceField.GetValue(null);
                    if (loader != null)
                    {
                        return scanField.GetValue(loader) is Task scan && scan.Status == TaskStatus.RanToCompletion;
                    }
                }

                var progress = _songCoreProgress;
                return progress != null && progress() >= 1f;
            }
            catch (Exception ex)
            {
                _log.Warn($"Could not read which step of its song loading SongCore is in; the HUD editor now waits at most {EditLaunchRecovery.SongLoadingTimeoutSeconds:0} s for it: {ex.Message}");
                _songCoreInstanceField = null;
                _songCoreScanTaskField = null;
                _songCoreProgress = null;
                return false;
            }
        }

        private void ResolveSongCoreProbe()
        {
            Type? loaderType;
            try
            {
                var metadata = PluginManager.GetPluginFromId(SongCorePluginId);
                if (metadata == null || !PluginManager.IsEnabled(metadata))
                {
                    return;
                }

                loaderType = metadata.Assembly?.GetType(SongCoreLoaderTypeName, throwOnError: false);
                var property = loaderType?.GetProperty(SongCoreLoadingPropertyName, BindingFlags.Public | BindingFlags.Static);
                var getter = property?.GetGetMethod();
                if (loaderType == null
                    || property == null
                    || getter == null
                    || property.PropertyType != typeof(bool)
                    || property.GetIndexParameters().Length != 0)
                {
                    _log.Warn($"SongCore is installed but {SongCoreLoaderTypeName}.{SongCoreLoadingPropertyName} was not found; the HUD editor does not wait for its song loading.");
                    return;
                }

                _songCoreLoading = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter);
            }
            catch (Exception ex)
            {
                _log.Warn($"Could not look up SongCore's song-loading state; the HUD editor does not wait for it: {ex.Message}");
                _songCoreLoading = null;
                return;
            }

            ResolveSongCoreFinalStep(loaderType);
        }

        private void ResolveSongCoreFinalStep(Type loaderType)
        {
            try
            {
                var instance = loaderType.GetField(SongCoreInstanceFieldName, BindingFlags.Public | BindingFlags.Static);
                var scan = loaderType.GetField(SongCoreScanTaskFieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (instance != null
                    && instance.FieldType == loaderType
                    && scan != null
                    && typeof(Task).IsAssignableFrom(scan.FieldType))
                {
                    _songCoreInstanceField = instance;
                    _songCoreScanTaskField = scan;
                    return;
                }

                var progress = loaderType.GetProperty(SongCoreProgressPropertyName, BindingFlags.Public | BindingFlags.Static);
                var getter = progress?.GetGetMethod();
                if (progress != null
                    && getter != null
                    && progress.PropertyType == typeof(float)
                    && progress.GetIndexParameters().Length == 0)
                {
                    _songCoreProgress = (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), getter);
                    _log.Debug($"SongCore's {SongCoreScanTaskFieldName} was not found; its last song-loading step is recognised by {SongCoreProgressPropertyName} instead.");
                    return;
                }

                _log.Warn($"SongCore's last song-loading step cannot be recognised in this SongCore version; the HUD editor waits at most {EditLaunchRecovery.SongLoadingTimeoutSeconds:0} s for its song loading.");
            }
            catch (Exception ex)
            {
                _log.Warn($"Could not look up SongCore's song-loading steps; the HUD editor waits at most {EditLaunchRecovery.SongLoadingTimeoutSeconds:0} s for its song loading: {ex.Message}");
                _songCoreInstanceField = null;
                _songCoreScanTaskField = null;
                _songCoreProgress = null;
            }
        }

        private async void ReportLateFailure(Task<bool> launch)
        {
            bool started;
            try
            {
                started = await launch;
            }
            catch (Exception ex)
            {
                _log.Error($"HUD editor launch failed: {ex}");
                started = false;
            }

            if (!started && !_disposed)
            {
                _log.Warn("The live editor could not be started; the menu stays as it is (see the messages above).");
            }
        }

        private async Task<bool> IsOwnedAsync(BeatmapLevel level, CancellationToken cancellation)
        {
            try
            {
                var query = _levels.entitlements.GetLevelEntitlementStatusAsync(level.levelID, cancellation);
                if (!query.IsCompleted)
                {
                    var finished = await Task.WhenAny(query, Task.Delay(EntitlementQueryTimeout, cancellation));
                    if (finished != query)
                    {
                        _log.Debug($"Entitlement check for '{level.levelID}' timed out or was cancelled.");
                        return false;
                    }
                }

                return await query == EntitlementStatus.Owned;
            }
            catch (Exception ex)
            {
                _log.Debug($"Entitlement check failed for '{level.levelID}': {ex.Message}");
                return false;
            }
        }

        private IEnumerable<LevelCandidate> EnumerateCandidates(bool prefer360)
        {
            var levels = OrderedLevels();
            var passes = new List<string>(2);
            if (prefer360 && Is360Enabled())
            {
                passes.Add(Degree360Characteristic);
            }

            passes.Add(StandardCharacteristic);

            foreach (var characteristicName in passes)
            {
                foreach (var level in levels)
                {
                    var candidate = TryMakeCandidate(level, characteristicName);
                    if (candidate != null)
                    {
                        yield return candidate;
                    }
                }
            }
        }

        private List<BeatmapLevel> OrderedLevels()
        {
            var ordered = new List<BeatmapLevel>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            var preferredId = _config.Config.Global.PreviewLevelId;
            if (!string.IsNullOrWhiteSpace(preferredId))
            {
                var preferred = Safe.Invoke("EditSessionLauncher.PreferredLevel", () => _levels.GetBeatmapLevel(preferredId!), null);

                if (preferred != null && !IsCustomLevel(preferred.levelID) && seen.Add(preferred.levelID))
                {
                    ordered.Add(preferred);
                }
            }

            foreach (var pack in _levels.ostAndExtrasBeatmapLevelsRepository.beatmapLevelPacks)
            {
                if (pack == null)
                {
                    continue;
                }

                foreach (var level in pack.AllBeatmapLevels())
                {
                    if (level != null && !IsCustomLevel(level.levelID) && seen.Add(level.levelID))
                    {
                        ordered.Add(level);
                    }
                }
            }

            return ordered;
        }

        private static bool IsCustomLevel(string levelId) => levelId.StartsWith("custom_level_", StringComparison.Ordinal);

        private bool Is360Enabled() =>
            _characteristics != null && _characteristics.GetBeatmapCharacteristicBySerializedName(Degree360Characteristic) != null;

        private LevelCandidate? TryMakeCandidate(BeatmapLevel level, string characteristicName)
        {
            return Safe.Invoke<LevelCandidate?>(
                "EditSessionLauncher.Candidate",
                () =>
                {
                    var characteristic = level.GetCharacteristics().FirstOrDefault(c => c != null && c.serializedName == characteristicName);
                    if (characteristic == null)
                    {
                        return null;
                    }

                    foreach (var difficulty in level.GetDifficulties(characteristic))
                    {
                        if (level.GetDifficultyBeatmapData(characteristic, difficulty) != null)
                        {
                            return new LevelCandidate(level, characteristic, difficulty);
                        }
                    }

                    return null;
                },
                null);
        }

        private bool TryStartLevel(EnvironmentOption environment, LevelCandidate candidate, HudConfig? configAtStart, bool isRetry)
        {
            var level = candidate.Level;

            var original = _environments.GetEnvironmentInfoBySerializedName(level.GetEnvironmentName(candidate.Characteristic, candidate.Difficulty));
            if (original == null)
            {
                _log.Debug($"Skipping '{level.levelID}': its environment is not in the environment list.");
                return false;
            }

            var overrideSettings = new OverrideEnvironmentSettings { overrideEnvironments = true };
            overrideSettings.SetEnvironmentInfoForType(original.environmentType, environment.Info);

            var playerData = _playerData.playerData;

            var modifiers = GameplayModifiers.noModifiers.CopyWith(noFailOn0Energy: true, zenMode: true);

            var playerSettings = playerData.playerSpecificSettings.CopyWith(autoRestart: false);

            var startFraction = Math.Max(0f, Math.Min(MaxStartFraction, _config.Config.Global.Editor.PreviewStartFraction));
            var startSongTime = level.songDuration > 0f ? level.songDuration * startFraction : 0f;
            var practice = new PracticeSettings(startSongTime, 1f);

            var beatmapKey = new BeatmapKey(level.levelID, candidate.Characteristic, candidate.Difficulty);

            var colorSchemes = playerData.colorSchemesSettings;

            var snapshot = configAtStart ?? _config.CreateSnapshot();
            _state.Begin(environment.SerializedName, snapshot);
            SubscribeEarlyBindings();
            _retryAvailable = !isRetry;
            _audioInvalidAtFinish = false;
            WatchLevelFinish(true);

            _startingTransition = true;
            try
            {
                _transitions.StartStandardLevel(
                    GameMode,
                    in beatmapKey,
                    level,
                    overrideSettings,
                    colorSchemes.GetOverrideColorScheme(),
                    colorSchemes.ShouldOverrideLightshowColors(),
                    level.GetColorScheme(beatmapKey.beatmapCharacteristic, beatmapKey.difficulty),
                    modifiers,
                    playerSettings,
                    practice,
                    _environments,
                    PauseMenuBackButtonText,
                    useTestNoteCutSoundEffects: false,
                    startPaused: false,
                    beforeSceneSwitchToGameplayCallback: null,
                    afterSceneSwitchToGameplayCallback: null,
                    levelFinishedCallback: HandleLevelFinished,
                    levelRestartedCallback: HandleLevelRestarted);

                KeepBeforeEditCopy(configAtStart, snapshot);
                _log.Info($"HUD edit session started: environment '{environment.SerializedName}', level '{level.levelID}' ({candidate.Characteristic.serializedName} {candidate.Difficulty}), start {startSongTime:0.0}s.");
                return true;
            }
            catch (Exception ex)
            {
                if (_scenes.isInTransition)
                {
                    _log.Error($"HUD editor transition reported an error after it started: {ex}");
                    KeepBeforeEditCopy(configAtStart, snapshot);
                    return true;
                }

                _log.Warn($"Could not start '{level.levelID}' for the HUD editor, trying the next level: {ex.Message}");
                _retryAvailable = false;
                EndSession();
                return false;
            }
            finally
            {
                _startingTransition = false;
            }
        }

        private void KeepBeforeEditCopy(HudConfig? configAtStart, HudConfig snapshot)
        {
            if (configAtStart == null)
            {
                _config.KeepBeforeEditCopy(snapshot);
            }
        }

        private void DropBeforeEditCopyIfDone(bool sessionContinues)
        {
            switch (ConfigSafety.ForBeforeEditCopy(sessionContinues, _config.IsReadOnly, _config.LastSaveFailed))
            {
                case BeforeEditCopyAction.DropNow:
                    _config.DropBeforeEditCopy();
                    break;

                case BeforeEditCopyAction.DropAfterNextSave:
                    _config.DropBeforeEditCopyAfterNextSave();
                    break;
            }
        }

        private void SubscribeEarlyBindings()
        {
            if (_earlyBindingsSubscribed)
            {
                return;
            }

            _scenes.installEarlyBindingsEvent += HandleInstallEarlyBindings;
            _earlyBindingsSubscribed = true;
        }

        private void EndSession()
        {
            if (_earlyBindingsSubscribed)
            {
                _scenes.installEarlyBindingsEvent -= HandleInstallEarlyBindings;
                _earlyBindingsSubscribed = false;
            }

            WatchLevelFinish(false);
            _state.End();
        }

        private void WatchLevelFinish(bool watch)
        {
            if (watch)
            {
                if (_watchedSetupData is not null)
                {
                    return;
                }

                var setupData = _transitions._standardLevelScenesTransitionSetupData;
                if (setupData == null)
                {
                    return;
                }

                setupData.didFinishEvent += HandleLevelDidFinish;
                _watchedSetupData = setupData;
                return;
            }

            var watched = _watchedSetupData;
            if (watched is null)
            {
                return;
            }

            _watchedSetupData = null;
            watched.didFinishEvent -= HandleLevelDidFinish;
        }

        private void HandleLevelDidFinish(StandardLevelScenesTransitionSetupDataSO setupData, LevelCompletionResults results)
        {
            _audioInvalidAtFinish = false;
            Safe.Invoke("EditSessionLauncher.LevelDidFinish", () =>
            {
                _audioInvalidAtFinish = _state.IsActive && IsSongAudioInvalid(setupData);
            });
        }

        private void HandleInstallEarlyBindings(ScenesTransitionSetupDataSO setupData, DiContainer container)
        {
            Safe.Invoke("EditSessionLauncher.InstallEarlyBindings", () =>
            {
                if (!_state.IsActive || !ReferenceEquals(setupData, _transitions._standardLevelScenesTransitionSetupData))
                {
                    return;
                }

                // no AsSingle here, zenject asserts when the game binds the same type AsSingle
                if (!container.HasBinding<EditSessionMarker>())
                {
                    container.Bind<EditSessionMarker>().FromInstance(new EditSessionMarker(_state.EnvironmentSerializedName));
                }

                // bound early so zen mode keeps the hud. same thing, AsSingle would break the whole container
                if (container.HasBinding<CoreGameHUDController.InitData>())
                {
                    _log.Warn("CoreGameHUDController.InitData is already bound by another mod; the HUD may stay hidden in the editor.");
                }
                else
                {
                    container.Bind<CoreGameHUDController.InitData>()
                        .FromInstance(new CoreGameHUDController.InitData(hide: false, showEnergyPanel: true, advancedHUD: true));
                }
            });
        }

        private void HandleLevelRestarted(StandardLevelScenesTransitionSetupDataSO setupData, LevelCompletionResults results)
        {
            Safe.Invoke("EditSessionLauncher.LevelRestarted", () =>
            {
                var stale = _state.TakeRelaunchRequest();
                if (stale != null)
                {
                    _log.Warn($"Dropped a pending switch to '{stale.EnvironmentSerializedName}' because the edit session was restarted.");
                }

                _log.Info("HUD edit session restarted.");
            });
        }

        private void HandleLevelFinished(StandardLevelScenesTransitionSetupDataSO setupData, LevelCompletionResults results)
        {
            EditSessionRelaunchRequest? relaunch = null;
            AudioFailureRetry? retry = null;
            Safe.Invoke("EditSessionLauncher.LevelFinished", () =>
            {
                relaunch = _state.TakeRelaunchRequest();

                retry = TakeAudioFailureRetry(results, relaunchRequested: relaunch != null);
                EndSession();
                _config.SaveNow();
                DropBeforeEditCopyIfDone(sessionContinues: !_disposed && (relaunch != null || retry != null));
                _log.Info("HUD edit session ended.");
            });

            if (_disposed)
            {
                return;
            }

            if (relaunch != null)
            {
                var request = relaunch;
                Safe.Invoke("EditSessionLauncher.Relaunch", () => Relaunch(request));
            }
            else if (retry != null)
            {
                var request = retry;
                Safe.Invoke("EditSessionLauncher.Retry", () => Retry(request));
            }
        }

        private AudioFailureRetry? TakeAudioFailureRetry(LevelCompletionResults results, bool relaunchRequested)
        {
            var retryAvailable = _retryAvailable;
            var audioInvalidAtFinish = _audioInvalidAtFinish;
            _retryAvailable = false;
            _audioInvalidAtFinish = false;
            return Safe.Invoke<AudioFailureRetry?>(
                "EditSessionLauncher.AudioFailure",
                () =>
                {
                    if (results == null
                        || results.levelEndStateType != LevelCompletionResults.LevelEndStateType.Incomplete
                        || results.levelEndAction != LevelCompletionResults.LevelEndAction.Quit
                        || !EditLaunchRecovery.IsAudioLoadFailure(endedByQuit: true, results.endSongTime, audioInvalidAtFinish))
                    {
                        return null;
                    }

                    if (!EditLaunchRecovery.ShouldRetry(retryAvailable, relaunchRequested, audioLoadFailure: true))
                    {
                        if (!relaunchRequested)
                        {
                            _log.Warn("The HUD editor level returned to the menu again before it started (its song audio was unloaded while it loaded); not trying again. Press the NotCounters button to start the editor.");
                        }

                        return null;
                    }

                    var environment = _state.EnvironmentSerializedName;
                    var snapshot = _state.ConfigAtStart;
                    return string.IsNullOrEmpty(environment) || snapshot == null ? null : new AudioFailureRetry(environment, snapshot);
                },
                null);
        }

        private static bool IsSongAudioInvalid(StandardLevelScenesTransitionSetupDataSO setupData)
        {
            if (setupData == null)
            {
                return false;
            }

            var sceneSetupData = setupData.gameplayCoreSceneSetupData;
            if (sceneSetupData == null)
            {
                return false;
            }

            var clip = sceneSetupData.songAudioClip;
            if (clip == null)
            {
                return EditLaunchRecovery.IsSongAudioInvalid(clipMissing: true, loadFailed: false, samples: 0, lengthSeconds: 0f);
            }

            return EditLaunchRecovery.IsSongAudioInvalid(
                clipMissing: false,
                loadFailed: clip.loadState == UnityEngine.AudioDataLoadState.Failed,
                samples: clip.samples,
                lengthSeconds: clip.length);
        }

        private void Retry(AudioFailureRetry retry)
        {
            var environment = retry.EnvironmentSerializedName;
            _log.Warn("The HUD editor level returned to the menu before it started: its song audio was unloaded while the level loaded "
                + "(the game logs \"Could not load StandardLevel. Audio valid: False\"; a song-list refresh by another mod, such as SongCore, "
                + $"finishing at that moment does this). Starting the HUD editor in '{environment}' once more.");
            if (!StartSessionCore(environment, retry.ConfigAtStart, isRetry: true))
            {
                _log.Warn("The second attempt to start the HUD editor did not start; staying in the menu. Press the NotCounters button to try again.");
                DropBeforeEditCopyIfDone(sessionContinues: false);
            }
        }

        private void Relaunch(EditSessionRelaunchRequest request)
        {
            var environment = request.EnvironmentSerializedName;

            _log.Info($"Continuing the HUD edit session in '{environment}'.");
            if (!StartSession(environment, request.ConfigAtStart))
            {
                _log.Warn($"Could not continue the HUD edit session in '{environment}'; staying in the menu.");
                DropBeforeEditCopyIfDone(sessionContinues: false);
            }
        }

        private sealed class LevelCandidate
        {
            public LevelCandidate(BeatmapLevel level, BeatmapCharacteristicSO characteristic, BeatmapDifficulty difficulty)
            {
                Level = level;
                Characteristic = characteristic;
                Difficulty = difficulty;
            }

            public BeatmapLevel Level { get; }

            public BeatmapCharacteristicSO Characteristic { get; }

            public BeatmapDifficulty Difficulty { get; }
        }

        private sealed class AudioFailureRetry
        {
            public AudioFailureRetry(string environmentSerializedName, HudConfig configAtStart)
            {
                EnvironmentSerializedName = environmentSerializedName;
                ConfigAtStart = configAtStart;
            }

            public string EnvironmentSerializedName { get; }

            public HudConfig ConfigAtStart { get; }
        }
    }
}
