using System;
using System.Diagnostics;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Core.Editing;
using NotCounters.Util;
using SiraUtil.Logging;
using SiraUtil.Submissions;
using UnityEngine;
using Zenject;

namespace NotCounters.Editor
{
    internal sealed class EditSessionController : IInitializable, ITickable, IDisposable
    {
        internal const string SubmissionSource = "NotCounters";

        private const string SubmissionReason = "HUD edit session";

        private const float SettleSongSeconds = 1.5f;

        private const float SettleRealSecondsFallback = 4f;

        private const float PauseRetrySeconds = 0.5f;
        private const float PauseGiveUpSeconds = 10f;

        private const float PointerWaitSeconds = 1f;

        private const float SongEndMarginSeconds = 0.5f;

        private const int MaxConsecutiveTickFailures = 5;

        private readonly SiraLog _log;
        private readonly EditSessionState _state;
        private readonly IConfigService _config;
        private readonly EditSessionMarker? _marker;
        private readonly Submission? _submission;
        private readonly PauseController? _pauseController;
        private readonly IReturnToMenuController? _returnToMenu;
        private readonly ILevelStartController? _levelStart;
        private readonly IAudioTimeSource? _audioTime;
        private readonly IMenuButtonTrigger? _menuButtonTrigger;
        private readonly GameScenesManager? _scenes;
        private readonly PauseMenuFreezer _freezer;
        private readonly UndoHistory<HudConfig> _history;

        private Phase _phase = Phase.WaitingForLevelStart;
        private bool _levelStarted;
        private bool _exitWhenLevelStarts;
        private float _settleStartSongTime;
        private float _settleStartRealTime;
        private float _pausingStartRealTime;
        private float _lastPauseRequestRealTime;
        private float _pausedRealTime = -1f;
        private int _consecutiveTickFailures;
        private bool _disposed;

        public EditSessionController(
            SiraLog log,
            EditSessionState state,
            IConfigService config,
            [InjectOptional] EditSessionMarker? marker,
            [InjectOptional] Submission? submission,
            [InjectOptional] PauseController? pauseController,
            [InjectOptional] PauseMenuManager? pauseMenuManager,
            [InjectOptional] IGamePause? gamePause,
            [InjectOptional] IReturnToMenuController? returnToMenu,
            [InjectOptional] ILevelStartController? levelStart,
            [InjectOptional] IAudioTimeSource? audioTime,
            [InjectOptional] IMenuButtonTrigger? menuButtonTrigger,
            [InjectOptional] GameScenesManager? scenes)
        {
            _log = log;
            _state = state;
            _config = config;
            _marker = marker;
            _submission = submission;
            _pauseController = pauseController;
            _returnToMenu = returnToMenu;
            _levelStart = levelStart;
            _audioTime = audioTime;

            _menuButtonTrigger = menuButtonTrigger;

            _scenes = scenes;
            _freezer = new PauseMenuFreezer(pauseController, pauseMenuManager, gamePause);
            _history = new UndoHistory<HudConfig>(_config.CreateSnapshot);
        }

        private enum Phase
        {
            WaitingForLevelStart,
            Settling,
            Pausing,
            Editing,
            PauseMenu,
            Exiting,
            Failed,
        }

        public bool IsEditing => _phase == Phase.Editing;

        public string EnvironmentSerializedName =>
            !string.IsNullOrEmpty(_state.EnvironmentSerializedName) ? _state.EnvironmentSerializedName : _marker?.EnvironmentSerializedName ?? string.Empty;

        public bool IsConfigReadOnly => _config.IsReadOnly;

        public bool CanUndo => IsEditing && _history.CanUndo;

        public bool CanRedo => IsEditing && _history.CanRedo;

        public event Action? EditingStateChanged;

        public event Action? HistoryChanged;

        public void Initialize()
        {
            Safe.Invoke("EditSessionController.Initialize", () =>
            {
                DisableScoreSubmission();

                if (_levelStart != null)
                {
                    _levelStart.levelDidStartEvent += HandleLevelDidStart;
                }

                if (_pauseController != null)
                {
                    _pauseController.didResumeEvent += HandleGameResumed;
                }

                if (_menuButtonTrigger != null)
                {
                    _menuButtonTrigger.menuButtonTriggeredEvent += HandleMenuButtonTriggered;
                }
                else
                {
                    _log.Warn("IMenuButtonTrigger is not available; the menu button cannot bring the pause menu back while editing.");
                }

                if (!_freezer.CanFreeze)
                {
                    _log.Error("The HUD editor cannot pause this scene (PauseController or PauseMenuManager missing); returning to the menu.");
                    _exitWhenLevelStarts = true;
                    SetPhase(Phase.Failed);
                }

                _log.Info($"HUD edit session scene loaded ({EnvironmentSerializedName}).");

                if (_freezer.CanFreeze && !_freezer.HideButtonCanvas())
                {
                    _log.Warn("The pause menu's buttons were not found; they may show for a moment when the editor freezes.");
                }
            });
        }

        public void Tick()
        {
            if (_disposed || _phase == Phase.Exiting)
            {
                return;
            }

            try
            {
                TickPhase(Time.realtimeSinceStartup);
                _consecutiveTickFailures = 0;
            }
            catch (Exception ex)
            {
                Safe.Report("EditSessionController.Tick", ex);
                if (++_consecutiveTickFailures >= MaxConsecutiveTickFailures)
                {
                    AbortEditing("repeated errors in the edit session");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Safe.Invoke("EditSessionController.Dispose", () =>
            {
                if (_levelStart != null)
                {
                    _levelStart.levelDidStartEvent -= HandleLevelDidStart;
                }

                if (_pauseController != null)
                {
                    _pauseController.didResumeEvent -= HandleGameResumed;
                }

                if (_menuButtonTrigger != null)
                {
                    _menuButtonTrigger.menuButtonTriggeredEvent -= HandleMenuButtonTriggered;
                }

                _freezer.Restore(keepMenuManagerDisabled: false);
                _history.Clear();
            });
        }

        public HudLayout GetEditableLayout() => _config.GetEffectiveLayout(EnvironmentSerializedName);

        public bool RecordEdit(Action<HudLayout> apply, string? coalesceKey = null) =>
            RecordChange(() => apply(GetEditableLayout()), coalesceKey);

        public bool RecordConfigChange(Action change, string? coalesceKey = null) => RecordChange(change, coalesceKey);

        public void Undo() => Safe.Invoke("EditSessionController.Undo", () => Step(redo: false));

        public void Redo() => Safe.Invoke("EditSessionController.Redo", () => Step(redo: true));

        public bool EnvironmentHasOwnLayout => HasEnvironmentOverride();

        public bool CreateEnvironmentLayout()
        {
            if (!IsEditing || HasEnvironmentOverride())
            {
                return false;
            }

            var recorded = RecordChange(() => _config.GetOrCreateEnvironmentLayout(EnvironmentSerializedName));
            if (!HasEnvironmentOverride())
            {
                _log.Warn($"No layout of its own could be created for '{EnvironmentSerializedName}'; edits still change the shared layout.");
                return false;
            }

            _log.Info($"Created the layout of '{EnvironmentSerializedName}'; edits change only this environment.");
            return recorded;
        }

        public bool RemoveEnvironmentLayout()
        {
            if (!IsEditing || !HasEnvironmentOverride())
            {
                return false;
            }

            var recorded = RecordChange(() => _config.RemoveEnvironmentOverride(EnvironmentSerializedName));
            if (HasEnvironmentOverride())
            {
                _log.Warn($"The layout of '{EnvironmentSerializedName}' could not be removed; edits still change it.");
                return false;
            }

            _log.Info($"Removed the layout of '{EnvironmentSerializedName}'; it uses the shared layout again.");
            return recorded;
        }

        public bool RelaunchIn(string environmentSerializedName)
        {
            if (!IsEditing
                || string.IsNullOrEmpty(environmentSerializedName)
                || string.Equals(environmentSerializedName, EnvironmentSerializedName, StringComparison.Ordinal))
            {
                return false;
            }

            Safe.Invoke("EditSessionController.Save", _config.SaveNow);
            if (!_state.RequestRelaunch(environmentSerializedName))
            {
                _log.Warn("The edit session could not be moved to another environment: no session is active.");
                return false;
            }

            _log.Info($"Switching the HUD edit session to '{environmentSerializedName}'.");
            if (!ExitToMenu())
            {
                _state.TakeRelaunchRequest();
                _log.Warn($"The switch to '{environmentSerializedName}' was cancelled because the level could not be left.");
                return false;
            }

            return true;
        }

        public bool SaveAndExit(bool leaveIfNotSaved = false)
        {
            if (_phase == Phase.Exiting)
            {
                return true;
            }

            Safe.Invoke("EditSessionController.Save", _config.SaveNow);

            var saveFailed = _config.LastSaveFailed;
            if (ConfigSafety.StayAfterSave(saveFailed, leaveIfNotSaved))
            {
                _log.Warn("Save & Exit: the config could not be saved, so the HUD editor stays open and asks whether to leave without saving.");
                return false;
            }

            if (saveFailed)
            {
                _log.Warn("Leaving the HUD editor without saving the config; the changes stay in use and saving is tried again in the menu, when the editor opens and when the game closes.");
            }

            ExitToMenu();
            return true;
        }

        public void DiscardAndExit()
        {
            if (_phase == Phase.Exiting)
            {
                return;
            }

            Safe.Invoke("EditSessionController.Discard", () =>
            {
                var original = _state.ConfigAtStart;
                if (original == null)
                {
                    _log.Error("No config snapshot from the session start; edits cannot be discarded.");
                    return;
                }

                _config.Restore(original);
                _log.Info("HUD edits discarded.");
            });

            ExitToMenu();
        }

        public void AbortEditing(string reason)
        {
            if (_phase == Phase.Failed || _phase == Phase.Exiting)
            {
                return;
            }

            Fail(reason);
        }

        private void Fail(string reason)
        {
            _log.Error($"HUD editor stopped: {reason}. The pause menu is available to leave the session.");
            SetPhase(Phase.Failed);
            Safe.Invoke("EditSessionController.RestorePauseMenu", () => _freezer.Restore(keepMenuManagerDisabled: false));
        }

        private void TickPhase(float now)
        {
            switch (_phase)
            {
                case Phase.WaitingForLevelStart:
                    if (_freezer.IsGamePaused)
                    {
                        BeginPausing(now, requestPause: false);
                    }
                    else if (_levelStarted || IsSongPlaying())
                    {
                        _levelStarted = true;
                        _settleStartSongTime = SongTime;
                        _settleStartRealTime = now;
                        SetPhase(Phase.Settling);
                    }

                    break;

                case Phase.Settling:
                    if (_freezer.IsGamePaused)
                    {
                        BeginPausing(now, requestPause: false);
                    }
                    else if (SongTime - _settleStartSongTime >= SettleSongSeconds || now - _settleStartRealTime >= SettleRealSecondsFallback)
                    {
                        BeginPausing(now, requestPause: true);
                    }

                    break;

                case Phase.Pausing:
                    TickPausing(now);
                    break;

                case Phase.Editing:
                    if (!_freezer.IsGamePaused)
                    {
                        _log.Info("The game resumed during the edit session; pausing again.");
                        BeginPausing(now, requestPause: true);
                    }

                    break;

                case Phase.PauseMenu:
                    if (!_freezer.IsGamePaused)
                    {
                        BeginPausing(now, requestPause: true);
                    }

                    break;

                case Phase.Failed:
                    if (_exitWhenLevelStarts && (_levelStarted || _freezer.IsGamePaused || IsSongPlaying()))
                    {
                        _exitWhenLevelStarts = false;
                        ExitToMenu();
                        return;
                    }

                    break;
            }

            if (_phase != Phase.Editing && _phase != Phase.Exiting && IsNearSongEnd())
            {
                _log.Warn("The edit-session song reached its end before editing; returning to the menu.");
                ExitToMenu();
            }
        }

        private void BeginPausing(float now, bool requestPause)
        {
            _freezer.HideButtonCanvas();
            _pausingStartRealTime = now;
            _lastPauseRequestRealTime = now;
            _pausedRealTime = -1f;
            SetPhase(Phase.Pausing);
            if (requestPause)
            {
                _freezer.RequestPause();
            }
        }

        private void TickPausing(float now)
        {
            if (!_freezer.IsGamePaused)
            {
                if (now - _pausingStartRealTime >= PauseGiveUpSeconds)
                {
                    _exitWhenLevelStarts = true;
                    AbortEditing("the game refused to pause");
                    return;
                }

                if (now - _lastPauseRequestRealTime >= PauseRetrySeconds)
                {
                    _lastPauseRequestRealTime = now;
                    _freezer.RequestPause();
                }

                return;
            }

            if (_pausedRealTime < 0f)
            {
                _pausedRealTime = now;
            }

            if (!_freezer.IsPausePointerReady && now - _pausedRealTime < PointerWaitSeconds)
            {
                return;
            }

            if (!_freezer.IsPausePointerReady)
            {
                AbortEditing("the pause-menu pointer did not become active, so the editor could not be used");
                return;
            }

            if (_freezer.HidePauseMenu())
            {
                EnterEditing();
            }
            else
            {
                AbortEditing("the pause menu's button canvas was not found");
            }
        }

        private void EnterEditing()
        {
            var stopwatch = Stopwatch.StartNew();
            if (!_history.IsStarted)
            {
                _history.Start();
            }

            SetPhase(Phase.Editing);
            _log.Info("HUD edit session frozen; editing enabled.");
            RaiseIsolated(HistoryChanged, "EditSessionController.HistoryChanged");
            _log.Debug($"Entering editing took {stopwatch.ElapsedMilliseconds} ms (config snapshot, edit frames, side panels shown).");
        }

        private bool RecordChange(Action change, string? coalesceKey = null)
        {
            if (!IsEditing || !_history.IsStarted)
            {
                return false;
            }

            _history.BeforeEdit(coalesceKey, Time.realtimeSinceStartup);
            var succeeded = true;
            try
            {
                change();
            }
            catch (Exception ex)
            {
                succeeded = false;
                _log.Error($"HUD edit failed: {ex}");
            }

            _config.MarkDirty();
            _history.AfterEdit(coalesceKey, Time.realtimeSinceStartup);
            RaiseIsolated(HistoryChanged, "EditSessionController.HistoryChanged");
            return succeeded;
        }

        private void Step(bool redo)
        {
            if (!IsEditing || !_history.IsStarted)
            {
                return;
            }

            var target = redo ? _history.Redo() : _history.Undo();
            if (target == null)
            {
                return;
            }

            target.Global = _config.Config.Global;

            _config.Restore(target);

            RaiseIsolated(HistoryChanged, "EditSessionController.HistoryChanged");
        }

        private bool HasEnvironmentOverride()
        {
            var overrides = _config.ActiveProfile.EnvironmentOverrides;
            return overrides != null && overrides.ContainsKey(EnvironmentSerializedName);
        }

        private bool ExitToMenu()
        {
            if (_phase == Phase.Exiting)
            {
                return true;
            }

            SetPhase(Phase.Exiting);
            try
            {
                if (_returnToMenu != null)
                {
                    _returnToMenu.ReturnToMenu();
                }
                else if (!_freezer.PressPauseMenuBackButton())
                {
                    throw new InvalidOperationException("Neither IReturnToMenuController nor the pause menu is available.");
                }

                return true;
            }
            catch (Exception ex)
            {
                if (IsSceneTransitionRunning())
                {
                    _log.Error($"An error was reported while leaving the HUD edit session, but the game is already returning to the menu: {ex}");
                    return true;
                }

                _log.Error($"Could not leave the HUD edit session: {ex}");
                Fail("returning to the menu failed");
                return false;
            }
        }

        private bool IsSceneTransitionRunning()
        {
            var scenes = _scenes;
            return scenes != null && Safe.Invoke("EditSessionController.IsInTransition", () => scenes.isInTransition, false);
        }

        private void DisableScoreSubmission()
        {
            if (_submission != null)
            {
                _submission.DisableScoreSubmission(SubmissionSource, SubmissionReason);
            }
            else
            {
                _log.Warn("SiraUtil Submission is not available; score submission could not be disabled through SiraUtil.");
            }

            BsUtilsSubmission.TryDisable(SubmissionSource, _log);
        }

        private void HandleLevelDidStart() => Safe.Invoke("EditSessionController.LevelDidStart", () => _levelStarted = true);

        private void HandleGameResumed() => Safe.Invoke("EditSessionController.GameResumed", () =>
        {
            if (_phase == Phase.Editing || _phase == Phase.PauseMenu)
            {
                BeginPausing(Time.realtimeSinceStartup, requestPause: true);
            }
        });

        private void HandleMenuButtonTriggered() => Safe.Invoke("EditSessionController.MenuButton", () =>
        {
            if (_phase != Phase.Editing)
            {
                return;
            }

            _log.Info("Menu button pressed: the pause menu is back. Continue returns to the HUD editor.");
            SetPhase(Phase.PauseMenu);
            _freezer.Restore(keepMenuManagerDisabled: false);
        });

        private float SongTime => _audioTime != null ? _audioTime.songTime : 0f;

        private bool IsSongPlaying() => _audioTime != null && _audioTime.state == IAudioTimeSource.State.Playing;

        private bool IsNearSongEnd() =>
            _audioTime != null
            && _audioTime.state == IAudioTimeSource.State.Playing
            && _audioTime.songEndTime > 0f
            && _audioTime.songTime >= _audioTime.songEndTime - SongEndMarginSeconds;

        private void SetPhase(Phase phase)
        {
            var wasEditing = IsEditing;
            _phase = phase;
            if (wasEditing != IsEditing)
            {
                RaiseIsolated(EditingStateChanged, "EditSessionController.EditingStateChanged");
            }
        }

        private static void RaiseIsolated(Action? handlers, string key)
        {
            if (handlers == null)
            {
                return;
            }

            foreach (var handler in handlers.GetInvocationList())
            {
                Safe.Invoke(key, (Action)handler);
            }
        }
    }
}
