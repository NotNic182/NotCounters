using System;
using System.Collections.Generic;
using System.Globalization;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.Parser;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Core.Counters;
using NotCounters.Core.Editing;
using NotCounters.Editor;
using NotCounters.UI.Forms;
using NotCounters.Util;
using SiraUtil.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace NotCounters.UI.Panels
{
    internal sealed class SessionPanel : IEditorPanel
    {
        private const float FormRowWidth = 90f;

        private const float DiscardConfirmSeconds = 3f;

        private const string DiscardText = "Discard & Exit";

        private const string DiscardConfirmText = "Press again to discard";

        private const string OverrideLabel = "Username, alias, link or ID";

        private const string ScoreSaberOverrideLabel = "Username, link or ID";

        private const string OverridePlaceholder = "(empty = automatic)";

        private const float OverrideLabelFontSize = 3.4f;

        private const string UniformTextHint =
            "On: labels, multiplier and song time are drawn upright and at full width. Off: the game's own look.";

        private const string DragRotationKeep = "Keep";

        private const string DragRotationTurn = "Turn";

        private const string DragRotationFace = "Face";

        private static readonly IReadOnlyList<ChoiceItem> DragRotationChoices = new[]
        {
            new ChoiceItem(DragRotationKeep, "Keep (like vanilla)"),
            new ChoiceItem(DragRotationTurn, "Turn with controller"),
            new ChoiceItem(DragRotationFace, "Face me on release"),
        };

        private readonly SiraLog _log;
        private readonly IConfigService _config;
        private readonly EditSessionController _session;
        private readonly EnvironmentCatalog _environments;
        private readonly EditPlayHints _hints;
        private readonly IPlayerAccountService? _accounts;

        private readonly List<object> _environmentOptions = new List<object>();
        private readonly List<object> _profileOptions = new List<object>();

        private SettingsForm? _settingsForm;
        private Action? _pendingConfirmation;
        private string? _pendingEnvironment;
        private float _discardArmedUntil = -1f;
        private bool _attached;
        private bool _dirty;
        private bool _settingsDirty;
        private bool _settingsWriteInProgress;

        private ConfigLoadNotice _loadNotice;

        private bool _shownSaveFailed;

        [UIComponent("environment-dropdown")]
        private DropDownListSetting? _environmentDropdown = null;

        [UIComponent("own-layout-toggle")]
        private ToggleSetting? _ownLayoutToggle = null;

        [UIObject("status-row")]
        private GameObject? _statusRow = null;

        [UIComponent("status-text")]
        private TextMeshProUGUI? _statusText = null;

        [UIObject("hint-text-row")]
        private GameObject? _hintTextRow = null;

        [UIComponent("hint-text")]
        private TextMeshProUGUI? _hintText = null;

        [UIComponent("profile-dropdown")]
        private DropDownListSetting? _profileDropdown = null;

        [UIComponent("profile-name")]
        private StringSetting? _profileName = null;

        [UIComponent("delete-profile-button")]
        private Button? _deleteProfileButton = null;

        [UIObject("settings-container")]
        private GameObject? _settingsContainer = null;

        [UIComponent("undo-button")]
        private Button? _undoButton = null;

        [UIComponent("redo-button")]
        private Button? _redoButton = null;

        [UIComponent("snap-button")]
        private Button? _snapButton = null;

        [UIComponent("save-button")]
        private Button? _saveButton = null;

        [UIComponent("discard-button")]
        private Button? _discardButton = null;

        [UIComponent("confirm-modal")]
        private HMUI.ModalView? _confirmModal = null;

        [UIComponent("confirm-text")]
        private TextMeshProUGUI? _confirmText = null;

        [UIComponent("confirm-button")]
        private Button? _confirmButton = null;

        [UIParams]
        private BSMLParserParams? _parserParams = null;

        public SessionPanel(
            SiraLog log,
            IConfigService config,
            EditSessionController session,
            EnvironmentCatalog environments,
            EditPlayHints hints,
            [InjectOptional] IPlayerAccountService? accounts)
        {
            _log = log;
            _config = config;
            _session = session;
            _environments = environments;
            _hints = hints;
            _accounts = accounts;
        }

        public string Name => "NotCountersSessionPanel";

        public string ViewResource => "NotCounters.UI.Views.SessionPanel.bsml";

        [UIValue("environment-options")]
        private List<object> EnvironmentOptions => _environmentOptions;

        [UIValue("selected-environment")]
        private object? SelectedEnvironment
        {
            get => FindEnvironmentChoice(_session.EnvironmentSerializedName);
            set => Safe.Invoke("SessionPanel.Environment", () =>
            {
                if (value is EnvironmentChoice choice
                    && !string.Equals(choice.SerializedName, _session.EnvironmentSerializedName, StringComparison.Ordinal))
                {
                    _pendingEnvironment = choice.SerializedName;
                }
            });
        }

        [UIValue("own-layout")]
        private bool OwnLayout
        {
            get => _session.EnvironmentHasOwnLayout;
            set => Safe.Invoke("SessionPanel.OwnLayout", () =>
            {
                if (value == _session.EnvironmentHasOwnLayout)
                {
                    return;
                }

                _dirty = true;
                if (value)
                {
                    _session.CreateEnvironmentLayout();
                    return;
                }

                AskConfirmation(
                    "Remove the layout of " + EnvironmentDisplayName + "? It will use the shared layout again.",
                    "Remove",
                    () => _session.RemoveEnvironmentLayout());
            });
        }

        [UIValue("profile-options")]
        private List<object> ProfileOptions => _profileOptions;

        [UIValue("selected-profile")]
        private object? SelectedProfile
        {
            get => FindProfileChoice(_config.ActiveProfile.Id);
            set => Safe.Invoke("SessionPanel.Profile", () =>
            {
                if (value is ProfileChoice choice && !string.Equals(choice.Id, _config.ActiveProfile.Id, StringComparison.Ordinal))
                {
                    var id = choice.Id;
                    _session.RecordConfigChange(() => _config.SetActiveProfile(id));
                }
            });
        }

        [UIValue("profile-name")]
        private string ProfileName
        {
            get => _config.ActiveProfile.Name ?? string.Empty;
            set => Safe.Invoke("SessionPanel.Rename", () =>
            {
                var profile = _config.ActiveProfile;
                var name = ProfileNames.ForRename(_config.Config.Profiles, profile, value);
                if (name != null)
                {
                    _session.RecordConfigChange(() => profile.Name = name);
                }

                _dirty = true;
            });
        }

        private GlobalSettings Global => _config.Config.Global;

        private EditorSettings EditorOptions => Global.Editor ??= new EditorSettings();

        public void PrepareView()
        {
            RebuildEnvironmentOptions();
            RebuildProfileOptions();
        }

        public void Attach(BSMLParser parser, Action<GameObject> prepareRows)
        {
            if (_attached)
            {
                return;
            }

            if (_settingsContainer != null)
            {
                _settingsForm = new SettingsForm(parser, _settingsContainer, new FormStyle(FormRowWidth), prepareRows);
                _settingsForm.RowChanged += HandleSettingsRowChanged;
                _settingsForm.Build(BuildSettingsFields());
            }
            else
            {
                _log.Warn("The NotCounters panel has no settings container; the Options tab stays empty.");
            }

            _config.Changed += HandleConfigChanged;
            _session.HistoryChanged += MarkDirty;
            if (_accounts != null)
            {
                _accounts.Changed += HandleAccountsChanged;
            }

            _attached = true;
            _dirty = true;
        }

        public void OnShown()
        {
            if (!_attached)
            {
                return;
            }

            _discardArmedUntil = -1f;

            if (_loadNotice == ConfigLoadNotice.None)
            {
                _loadNotice = _config.TakeLoadNotice();
            }

            _dirty = true;
            _settingsDirty = true;
            Refresh();
        }

        public void LateTick()
        {
            if (!_attached)
            {
                return;
            }

            var environment = _pendingEnvironment;
            if (environment != null)
            {
                _pendingEnvironment = null;
                SwitchEnvironment(environment);
                return;
            }

            if (_discardArmedUntil >= 0f && Time.unscaledTime > _discardArmedUntil)
            {
                _discardArmedUntil = -1f;
                _dirty = true;
            }

            if (_config.LastSaveFailed != _shownSaveFailed)
            {
                _dirty = true;
            }

            if (_dirty || _settingsDirty)
            {
                Refresh();
            }
        }

        public void Detach()
        {
            if (!_attached)
            {
                return;
            }

            _attached = false;
            _config.Changed -= HandleConfigChanged;
            _session.HistoryChanged -= MarkDirty;
            if (_accounts != null)
            {
                _accounts.Changed -= HandleAccountsChanged;
            }

            if (_settingsForm != null)
            {
                _settingsForm.RowChanged -= HandleSettingsRowChanged;
            }
        }

        [UIAction("new-profile")]
        private void OnNewProfile() => Safe.Invoke("SessionPanel.NewProfile", () =>
            _session.RecordConfigChange(() => _config.SetActiveProfile(LayoutEditing.CreateProfile(_config.Config, ProfileNames.DefaultBaseName).Id)));

        [UIAction("duplicate-profile")]
        private void OnDuplicateProfile() => Safe.Invoke("SessionPanel.DuplicateProfile", () =>
            _session.RecordConfigChange(() => _config.SetActiveProfile(LayoutEditing.DuplicateProfile(_config.Config, _config.ActiveProfile).Id)));

        [UIAction("ask-delete-profile")]
        private void OnAskDeleteProfile() => Safe.Invoke("SessionPanel.AskDeleteProfile", () =>
        {
            if (_config.Config.Profiles.Count <= 1)
            {
                return;
            }

            AskConfirmation(
                "Delete the profile \"" + _config.ActiveProfile.Name + "\"? Undo brings it back until you exit the editor.",
                "Delete",
                () => _session.RecordConfigChange(DeleteActiveProfile));
        });

        [UIAction("ask-reset-profile")]
        private void OnAskResetProfile() => Safe.Invoke("SessionPanel.AskResetProfile", () => AskConfirmation(
            "Reset \"" + _config.ActiveProfile.Name + "\" to the vanilla HUD? Its counters and environment layouts are removed.",
            "Reset",
            () => _session.RecordConfigChange(() => LayoutEditing.ResetProfile(_config.ActiveProfile))));

        [UIAction("confirm")]
        private void OnConfirm() => Safe.Invoke("SessionPanel.Confirm", () =>
        {
            if (PanelSupport.IsClosing(_confirmModal))
            {
                return;
            }

            _parserParams?.EmitEvent("hide-confirm");
            var action = _pendingConfirmation;
            _pendingConfirmation = null;
            action?.Invoke();
        });

        [UIAction("undo")]
        private void OnUndo() => _session.Undo();

        [UIAction("redo")]
        private void OnRedo() => _session.Redo();

        [UIAction("toggle-snap")]
        private void OnToggleSnap() => Safe.Invoke("SessionPanel.Snap", () =>
        {
            EditorOptions.SnapToGrid = !EditorOptions.SnapToGrid;
            _config.MarkDirty();
        });

        [UIAction("save-exit")]
        private void OnSaveExit() => Safe.Invoke("SessionPanel.SaveExit", () =>
        {
            if (_session.SaveAndExit())
            {
                return;
            }

            _dirty = true;
            AskConfirmation(
                ConfigSafety.LeaveUnsavedQuestion,
                ConfigSafety.LeaveButton,
                () => _session.SaveAndExit(leaveIfNotSaved: true));
        });

        [UIAction("discard-exit")]
        private void OnDiscardExit() => Safe.Invoke("SessionPanel.DiscardExit", () =>
        {
            if (_discardArmedUntil >= 0f && Time.unscaledTime <= _discardArmedUntil)
            {
                _discardArmedUntil = -1f;
                _session.DiscardAndExit();
                return;
            }

            _discardArmedUntil = Time.unscaledTime + DiscardConfirmSeconds;
            PanelSupport.SetButton(_discardButton, _session.IsEditing, DiscardConfirmText);
        });

        private string EnvironmentDisplayName =>
            _environments.Find(_session.EnvironmentSerializedName)?.DisplayName ?? _session.EnvironmentSerializedName;

        private void MarkDirty() => _dirty = true;

        private void HandleConfigChanged()
        {
            _loadNotice = ConfigLoadNotice.None;
            _dirty = true;
        }

        private void HandleAccountsChanged() => _settingsDirty = true;

        private void HandleSettingsRowChanged(FormField field)
        {
            _settingsWriteInProgress = true;
            _config.MarkDirty();
        }

        private void Refresh()
        {
            var settingsOnly = !_dirty;
            _dirty = false;
            var reloadSettings = _settingsDirty || !_settingsWriteInProgress;
            _settingsDirty = false;
            _settingsWriteInProgress = false;

            if (!settingsOnly)
            {
                Safe.Invoke("SessionPanel.RefreshSession", RefreshSession);
                Safe.Invoke("SessionPanel.RefreshPlayHint", RefreshPlayHint);
                Safe.Invoke("SessionPanel.RefreshProfiles", RefreshProfiles);
                Safe.Invoke("SessionPanel.RefreshBottomBar", RefreshBottomBar);
            }

            var form = _settingsForm;
            if (reloadSettings && form != null)
            {
                Safe.Invoke("SessionPanel.ReloadOptions", form.ReloadValues);
            }
        }

        private void RefreshSession()
        {
            var saveFailed = _config.LastSaveFailed;
            _shownSaveFailed = saveFailed;

            Safe.Invoke("SessionPanel.EnvironmentChoices", RebuildEnvironmentOptions);
            Safe.Invoke("SessionPanel.OwnLayoutToggle", () => _ownLayoutToggle?.ReceiveValue());

            var notice = ConfigSafety.StatusLine(_session.IsConfigReadOnly, saveFailed, _loadNotice);
            PanelSupport.SetActive(_statusRow, notice != null);
            if (notice != null)
            {
                PanelSupport.SetText(_statusText, notice);
            }
        }

        private void RefreshPlayHint()
        {
            string? text = null;
            if (!Global.Enabled)
            {
                text = "\"Enable NotCounters in play\" is off (Options tab): play shows the game's own HUD, without your counters.";
            }
            else if (_hints.PlayerHidesHud)
            {
                text = Global.HideCountersWhenHudHidden
                    ? "No Texts & HUDs is on in Player Options: the vanilla HUD and your counters are hidden in play. Turn off \"Hide counters with the HUD\" on the Options tab to keep the counters."
                    : "No Texts & HUDs is on in Player Options: the vanilla HUD is hidden in play; your counters still show.";
            }
            else if (_hints.CanForceAdvancedHud && !_hints.ForceAdvancedHud && AdvancedHudElements.AnyOnHud(_session.GetEditableLayout()))
            {
                text = "Advanced HUD is off in Player Options: song progress, rank and accuracy are hidden in play. Turn on \"Always show Advanced HUD elements\" on the Options tab to show them.";
            }

            PanelSupport.SetActive(_hintTextRow, text != null);
            if (text != null)
            {
                PanelSupport.SetText(_hintText, text);
            }
        }

        private void RefreshProfiles()
        {
            Safe.Invoke("SessionPanel.ProfileChoices", RebuildProfileOptions);
            Safe.Invoke("SessionPanel.ProfileName", () => _profileName?.ReceiveValue());
            PanelSupport.SetButton(_deleteProfileButton, _session.IsEditing && _config.Config.Profiles.Count > 1);
        }

        private void RefreshBottomBar()
        {
            var editing = _session.IsEditing;
            PanelSupport.SetButton(_undoButton, _session.CanUndo);
            PanelSupport.SetButton(_redoButton, _session.CanRedo);
            PanelSupport.SetButton(_snapButton, editing, EditorOptions.SnapToGrid ? "Snap: On" : "Snap: Off");
            PanelSupport.SetButton(_saveButton, editing);
            PanelSupport.SetButton(_discardButton, editing, _discardArmedUntil >= 0f ? DiscardConfirmText : DiscardText);
        }

        private void SwitchEnvironment(string environment)
        {
            Safe.Invoke("SessionPanel.SwitchEnvironment", () =>
            {
                var global = Global;
                var previous = global.PreviewEnvironment;

                global.PreviewEnvironment = environment;
                if (!_session.RelaunchIn(environment))
                {
                    global.PreviewEnvironment = previous;
                    _config.MarkDirty();
                    _log.Warn($"Could not switch the HUD editor to '{environment}'.");
                    _dirty = true;
                }
            });
        }

        private void AskConfirmation(string text, string buttonText, Action onConfirm)
        {
            _pendingConfirmation = onConfirm;
            PanelSupport.SetText(_confirmText, text);
            PanelSupport.SetButton(_confirmButton, true, buttonText);
            _parserParams?.EmitEvent("show-confirm");
        }

        private void DeleteActiveProfile()
        {
            var profiles = _config.Config.Profiles;
            var index = profiles.IndexOf(_config.ActiveProfile);
            if (profiles.Count <= 1 || index < 0)
            {
                return;
            }

            profiles.RemoveAt(index);
            _config.SetActiveProfile(profiles[Math.Min(index, profiles.Count - 1)].Id);
        }

        private IEnumerable<FormField> BuildSettingsFields()
        {
            yield return new SectionField("NotCounters");
            yield return new ToggleField("Enable NotCounters in play", () => Global.Enabled, v => Global.Enabled = v)
            {
                HoverHint = "Off: play shows the game's own HUD, without your counters. The editor still works.",
            };
            yield return new ToggleField("Hide counters with the HUD", () => Global.HideCountersWhenHudHidden, v => Global.HideCountersWhenHudHidden = v)
            {
                HoverHint = "Off keeps counters visible when the game hides its HUD (No Texts & HUDs, Zen Mode).",
            };
            yield return new ToggleField("Always show Advanced HUD elements", () => Global.ForceAdvancedHudElements, v => Global.ForceAdvancedHudElements = v)
            {
                HoverHint = "Shows song progress, rank and accuracy in play even with Advanced HUD off in Player Options.",
            };

            yield return new ToggleField("Same text style everywhere", () => Global.UniformHudText, v => Global.UniformHudText = v)
            {
                HoverHint = UniformTextHint,
            };
            yield return new ToggleField("Show errors in counters", () => Global.ShowErrorsInCounters, v => Global.ShowErrorsInCounters = v)
            {
                HoverHint = "A counter that keeps failing shows \"ERR\". Off hides it instead.",
            };

            yield return new SectionField("BeatLeader");
            yield return new ToggleField("Use BeatLeader data", () => Global.EnableBeatLeader, v => Global.EnableBeatLeader = v)
            {
                HoverHint = "Fetches BeatLeader pp, stars and personal bests.",
            };
            yield return new DynamicTextField(() => AccountLine(_accounts?.BeatLeader));
            yield return new TextField(OverrideLabel, () => Global.BeatLeaderPlayerId, v => Global.BeatLeaderPlayerId = NormalizeOverride(v), OverridePlaceholder)
            {
                LabelFontSize = OverrideLabelFontSize,
            };
            yield return new ToggleField("BeatLeader: No Fail counts as 0 pp", () => Global.BeatLeaderNoFailCountsAsZero, v => Global.BeatLeaderNoFailCountsAsZero = v)
            {
                HoverHint = "On: 0 pp for the whole song with No Fail. Off: 0 pp only once your energy runs out.",
            };

            yield return new SectionField("ScoreSaber");
            yield return new ToggleField("Use ScoreSaber data", () => Global.EnableScoreSaber, v => Global.EnableScoreSaber = v)
            {
                HoverHint = "Fetches ScoreSaber pp, stars and personal bests.",
            };
            yield return new DynamicTextField(() => AccountLine(_accounts?.ScoreSaber));
            yield return new TextField(ScoreSaberOverrideLabel, () => Global.ScoreSaberPlayerId, v => Global.ScoreSaberPlayerId = NormalizeOverride(v), OverridePlaceholder)
            {
                LabelFontSize = OverrideLabelFontSize,
            };

            yield return new SectionField("Editor");
            yield return new NumberField(
                "Snap grid size",
                ConfigValidator.MinGridSize,
                ConfigValidator.MaxGridSize,
                0.01,
                () => EditorOptions.GridSize,
                v => EditorOptions.GridSize = (float)Math.Round(v, 3))
            {
                Style = NumberStyle.Slider,
                Format = v => v.ToString("0.00", CultureInfo.InvariantCulture) + " m",
                HoverHint = "Used while Snap (bottom bar) is on.",
            };
            yield return new NumberField(
                "Snap angle step",
                1,
                90,
                1,
                () => EditorOptions.AngleSnapDegrees,
                v => EditorOptions.AngleSnapDegrees = (float)Math.Round(v))
            {
                Style = NumberStyle.Slider,
                Format = v => v.ToString("0", CultureInfo.InvariantCulture) + ElementForms.DegreeSign,
                HoverHint = "Used while Snap (bottom bar) is on.",
            };
            yield return new ChoiceField("Rotation when dragging", DragRotationChoices, DragRotationKey, SetDragRotation)
            {
                HoverHint = "Turn: the element turns with your controller while held. Face me: it turns to face you when dropped.",
            };

            yield return new ToggleField("Invert joystick push/pull", () => EditorOptions.InvertThumbstickY, v => EditorOptions.InvertThumbstickY = v);

            yield return new SectionField("Controls");
            yield return new NoteField(
                "VR: point at an element and hold the trigger to move it; while holding, joystick up/down pushes or pulls it, left/right scales it.",
                10f);
            yield return new NoteField(
                "Desktop: hold the left mouse button to drag; wheel = distance, Shift+wheel = scale, Alt+wheel = turn. Arrow keys and PgUp/PgDn nudge the selection (Shift: 10 cm), Home resets it.",
                15f);
            yield return new NoteField(
                "Menu button: the game's pause menu (Continue comes back here). Esc on desktop exits and keeps your edits.",
                10f);
        }

        private static string AccountLine(PlayerAccount? account)
        {
            if (account == null)
            {
                return "Account: automatic detection is not available.";
            }

            if (!string.IsNullOrWhiteSpace(account.Message))
            {
                return "Account: " + account.Message!.Trim();
            }

            return "Account: " + account.Status switch
            {
                PlayerAccountStatus.Detecting => "Detecting…",
                PlayerAccountStatus.Found => account.Name ?? account.Id ?? "found",
                PlayerAccountStatus.NotFound => "not found",
                PlayerAccountStatus.Ambiguous => "several players match; use your profile link or ID",
                PlayerAccountStatus.Error => "the leaderboard could not be reached; retrying later",
                PlayerAccountStatus.Disabled => "Not looked up",
                _ => account.Status.ToString(),
            };
        }

        private static string? NormalizeOverride(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        private string DragRotationKey() =>
            EditorOptions.FaceHeadOnRelease ? DragRotationFace
            : EditorOptions.TurnWithController ? DragRotationTurn
            : DragRotationKeep;

        private void SetDragRotation(string key)
        {
            var editor = EditorOptions;
            editor.TurnWithController = string.Equals(key, DragRotationTurn, StringComparison.Ordinal);
            editor.FaceHeadOnRelease = string.Equals(key, DragRotationFace, StringComparison.Ordinal);
        }

        private void RebuildEnvironmentOptions()
        {
            var choices = new List<object>();
            foreach (var environment in _environments.Environments)
            {
                if (environment != null && !string.IsNullOrEmpty(environment.SerializedName))
                {
                    choices.Add(new EnvironmentChoice(environment.SerializedName, environment.DisplayName));
                }
            }

            var current = _session.EnvironmentSerializedName;
            if (!string.IsNullOrEmpty(current) && !choices.Exists(c => ((EnvironmentChoice)c).SerializedName == current))
            {
                choices.Insert(0, new EnvironmentChoice(current, current));
            }

            ReplaceIfChanged(_environmentOptions, choices, _environmentDropdown);
        }

        private void RebuildProfileOptions()
        {
            var choices = new List<object>();
            foreach (var profile in _config.Config.Profiles)
            {
                if (profile != null)
                {
                    choices.Add(new ProfileChoice(profile.Id, profile.Name));
                }
            }

            ReplaceIfChanged(_profileOptions, choices, _profileDropdown);
        }

        private static void ReplaceIfChanged(List<object> options, List<object> choices, DropDownListSetting? dropdown)
        {
            var changed = options.Count != choices.Count;
            for (var i = 0; !changed && i < choices.Count; i++)
            {
                changed = !Equals(options[i], choices[i]);
            }

            if (changed)
            {
                options.Clear();
                options.AddRange(choices);
            }

            if (dropdown == null)
            {
                return;
            }

            if (changed)
            {
                dropdown.UpdateChoices();
            }

            dropdown.ReceiveValue();
        }

        private EnvironmentChoice? FindEnvironmentChoice(string serializedName)
        {
            foreach (var option in _environmentOptions)
            {
                if (option is EnvironmentChoice choice && string.Equals(choice.SerializedName, serializedName, StringComparison.Ordinal))
                {
                    return choice;
                }
            }

            return null;
        }

        private ProfileChoice? FindProfileChoice(string profileId)
        {
            foreach (var option in _profileOptions)
            {
                if (option is ProfileChoice choice && string.Equals(choice.Id, profileId, StringComparison.Ordinal))
                {
                    return choice;
                }
            }

            return null;
        }

        private sealed class EnvironmentChoice
        {
            public EnvironmentChoice(string serializedName, string displayName)
            {
                SerializedName = serializedName;
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? serializedName : displayName;
            }

            public string SerializedName { get; }

            public string DisplayName { get; }

            public override string ToString() => DisplayName;

            public override bool Equals(object? obj) =>
                obj is EnvironmentChoice other
                && string.Equals(SerializedName, other.SerializedName, StringComparison.Ordinal)
                && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal);

            public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(SerializedName);
        }

        private sealed class ProfileChoice
        {
            public ProfileChoice(string id, string name)
            {
                Id = id;
                Name = string.IsNullOrWhiteSpace(name) ? id : name;
            }

            public string Id { get; }

            public string Name { get; }

            public override string ToString() => Name;

            public override bool Equals(object? obj) =>
                obj is ProfileChoice other
                && string.Equals(Id, other.Id, StringComparison.Ordinal)
                && string.Equals(Name, other.Name, StringComparison.Ordinal);

            public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);
        }
    }
}
