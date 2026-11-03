using System;
using System.Collections.Generic;
using System.Linq;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Parser;
using HMUI;
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

namespace NotCounters.UI.Panels
{
    internal sealed class ElementsPanel : IEditorPanel
    {
        private const float FormRowWidth = 90f;

        private const string RemovedVanillaSubtext = "Vanilla HUD - removed from this layout";

        private readonly SiraLog _log;
        private readonly EditSessionController _session;
        private readonly EditSelection _selection;
        private readonly ElementGrabController _grab;
        private readonly EditPlayHints _hints;
        private readonly IConfigService _config;

        private readonly List<ElementRow> _rows = new List<ElementRow>();
        private readonly List<string> _rowTexts = new List<string>();
        private readonly List<CustomListTableData.CustomCellInfo> _cells = new List<CustomListTableData.CustomCellInfo>();
        private readonly List<AddChoice> _addRows = new List<AddChoice>();
        private readonly List<CustomListTableData.CustomCellInfo> _addCells = new List<CustomListTableData.CustomCellInfo>();
        private readonly ElementFormContext _formContext;

        private SettingsForm? _form;
        private object? _formTarget;
        private string? _formSelectionKey;
        private AdjustStep _step = AdjustStep.Fine;
        private AddChoice? _pendingAdd;
        private Action? _pendingConfirmation;

        private HudLayout? _confirmationLayout;

        private HudLayout? _addLayout;
        private bool _attached;
        private bool _dirty;
        private bool _listLoaded;
        private bool _scrollToSelection;
        private bool _selectingFromList;
        private bool _formWriteInProgress;

        private bool _formNeedsReload;

        [UIComponent("element-list")]
        private CustomListTableData? _elementList = null;

        [UIComponent("add-list")]
        private CustomListTableData? _addList = null;

        [UIComponent("add-button")]
        private Button? _addButton = null;

        [UIComponent("copy-button")]
        private Button? _copyButton = null;

        [UIComponent("remove-button")]
        private Button? _removeButton = null;

        [UIComponent("visibility-button")]
        private Button? _visibilityButton = null;

        [UIComponent("settings-title")]
        private TextMeshProUGUI? _settingsTitle = null;

        [UIComponent("reset-all-button")]
        private Button? _resetAllButton = null;

        [UIObject("form-container")]
        private GameObject? _formContainer = null;

        [UIComponent("add-description")]
        private TextMeshProUGUI? _addDescription = null;

        [UIComponent("add-modal")]
        private ModalView? _addModal = null;

        [UIComponent("confirm-modal")]
        private ModalView? _confirmModal = null;

        [UIComponent("add-confirm-button")]
        private Button? _addConfirmButton = null;

        [UIComponent("confirm-prompt")]
        private TextMeshProUGUI? _confirmPrompt = null;

        [UIComponent("confirm-button")]
        private Button? _confirmButton = null;

        [UIParams]
        private BSMLParserParams? _parserParams = null;

        public ElementsPanel(
            SiraLog log,
            IConfigService config,
            EditSessionController session,
            EditSelection selection,
            ElementGrabController grab,
            EditPlayHints hints)
        {
            _log = log;
            _config = config;
            _session = session;
            _selection = selection;
            _grab = grab;
            _hints = hints;

            _formContext = new ElementFormContext(
                () => _step,
                step => _step = step,
                Reattach,
                _grab.ResetSelected,
                () => _session.IsEditing && _selection.Selected != null,
                SetMoveWithHud);
        }

        public string Name => "NotCountersElementsPanel";

        public string ViewResource => "NotCounters.UI.Views.ElementsPanel.bsml";

        public void PrepareView()
        {
        }

        public void Attach(BSMLParser parser, Action<GameObject> prepareRows)
        {
            if (_attached)
            {
                return;
            }

            if (_formContainer != null)
            {
                _form = new SettingsForm(parser, _formContainer, new FormStyle(FormRowWidth), prepareRows, RecordFormWrite);
            }
            else
            {
                _log.Warn("The Elements panel has no settings container; the Settings tab stays empty.");
            }

            _config.Changed += MarkDirty;
            _session.HistoryChanged += MarkDirty;
            _selection.Changed += HandleSelectionChanged;
            _attached = true;
            _dirty = true;
            _scrollToSelection = true;
        }

        public void OnShown()
        {
            if (!_attached)
            {
                return;
            }

            _scrollToSelection = true;
            Refresh();
        }

        public void LateTick()
        {
            if (_attached && _dirty)
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
            _config.Changed -= MarkDirty;
            _session.HistoryChanged -= MarkDirty;
            _selection.Changed -= HandleSelectionChanged;
        }

        [UIAction("element-selected")]
        private void OnElementSelected(TableView tableView, int index) => Safe.Invoke("ElementsPanel.Select", () =>
        {
            if (index < 0 || index >= _rows.Count)
            {
                return;
            }

            _selectingFromList = true;
            try
            {
                _selection.Select(_rows[index].Kind, _rows[index].Id);
            }
            finally
            {
                _selectingFromList = false;
            }
        });

        [UIAction("show-add-modal")]
        private void OnShowAddModal() => Safe.Invoke("ElementsPanel.ShowAdd", () =>
        {
            _parserParams?.EmitEvent("show-add");
            _addLayout = _session.GetEditableLayout();
            FillAddList();
        });

        [UIAction("add-choice-selected")]
        private void OnAddChoiceSelected(TableView tableView, int index) => Safe.Invoke("ElementsPanel.SelectAddChoice", () =>
        {
            _pendingAdd = index >= 0 && index < _addRows.Count ? _addRows[index] : null;
            UpdatePendingAdd();
        });

        [UIAction("add-selected")]
        private void OnAddSelected() => Safe.Invoke("ElementsPanel.Add", () =>
        {
            if (PanelSupport.IsClosing(_addModal))
            {
                return;
            }

            var choice = _pendingAdd;
            _addLayout = null;
            _parserParams?.EmitEvent("hide-add");
            if (choice == null)
            {
                return;
            }

            var id = choice.Id;
            if (choice.Kind == EditableElementKind.Vanilla)
            {
                if (!VanillaElementPresence.IsRemoved(_session.GetEditableLayout(), id))
                {
                    return;
                }

                var restored = false;
                _session.RecordEdit(layout =>
                {
                    VanillaElementPresence.Restore(layout, id);
                    restored = true;
                });
                if (restored)
                {
                    _selection.Select(EditableElementKind.Vanilla, id);
                }

                return;
            }

            string? newId = null;
            _session.RecordEdit(layout => newId = LayoutEditing.AddCounter(layout, id).Id);
            if (newId != null)
            {
                _selection.Select(EditableElementKind.Counter, newId);
            }
        });

        [UIAction("copy-counter")]
        private void OnCopyCounter() => Safe.Invoke("ElementsPanel.Copy", () =>
        {
            var id = SelectedCounterId();
            if (id == null)
            {
                return;
            }

            string? copyId = null;
            _session.RecordEdit(layout =>
            {
                var source = LayoutEditing.FindCounter(layout, id);
                if (source != null && CounterCatalog.GetDescriptor(source.Type) != null)
                {
                    copyId = LayoutEditing.Duplicate(layout, source).Id;
                }
            });
            if (copyId != null)
            {
                _selection.Select(EditableElementKind.Counter, copyId);
            }
        });

        [UIAction("ask-remove")]
        private void OnAskRemove() => Safe.Invoke("ElementsPanel.AskRemove", () =>
        {
            var id = _selection.SelectedId;
            if (id == null)
            {
                return;
            }

            var kind = _selection.SelectedKind;
            var layout = _session.GetEditableLayout();
            string prompt;
            if (kind == EditableElementKind.Vanilla)
            {
                var descriptor = VanillaElementCatalog.Get(id);
                if (descriptor == null || VanillaElementPresence.IsRemoved(layout, id))
                {
                    return;
                }

                prompt = "Remove the vanilla \"" + descriptor.DisplayName + "\" from this layout? Add brings it back.";
            }
            else
            {
                var counter = LayoutEditing.FindCounter(layout, id);
                if (counter == null)
                {
                    return;
                }

                prompt = "Remove the counter \"" + LayoutEditing.DisplayName(counter) + "\" from this layout?";
            }

            var row = new ElementRow(kind, id);
            AskConfirmation(prompt, "Remove", () => RemoveRow(row));
        });

        private void RemoveRow(ElementRow row)
        {
            var shown = _session.GetEditableLayout();
            if (!ExistsInLayout(shown, row.Kind, row.Id))
            {
                return;
            }

            var index = ListedRows(shown).FindIndex(listed => listed.Is(row.Kind, row.Id));
            var removed = false;
            _session.RecordEdit(layout =>
            {
                if (row.Kind == EditableElementKind.Vanilla)
                {
                    if (VanillaElementCatalog.Get(row.Id) != null)
                    {
                        VanillaElementPresence.Remove(layout, row.Id);
                        removed = true;
                    }
                }
                else
                {
                    removed = LayoutEditing.Remove(layout, row.Id) >= 0;
                }
            });

            if (removed)
            {
                SelectListedRow(Math.Max(index, 0));
            }
        }

        [UIAction("toggle-visibility")]
        private void OnToggleVisibility() => Safe.Invoke("ElementsPanel.Visibility", () =>
        {
            var id = _selection.SelectedId;
            if (id == null)
            {
                return;
            }

            if (_selection.SelectedKind == EditableElementKind.Vanilla)
            {
                if (VanillaElementCatalog.Get(id) != null)
                {
                    _session.RecordEdit(layout =>
                    {
                        var settings = LayoutEditing.GetVanilla(layout, id);
                        settings.Visible = !settings.Visible;
                    });
                }

                return;
            }

            _session.RecordEdit(layout =>
            {
                var counter = LayoutEditing.FindCounter(layout, id);
                if (counter != null)
                {
                    counter.Enabled = !counter.Enabled;
                }
            });
        });

        [UIAction("ask-reset-all")]
        private void OnAskResetAll() => Safe.Invoke("ElementsPanel.AskResetAll", () =>
        {
            var id = _selection.SelectedId;
            if (id == null)
            {
                return;
            }

            var kind = _selection.SelectedKind;
            var layout = _session.GetEditableLayout();
            string prompt;
            if (kind == EditableElementKind.Vanilla)
            {
                var descriptor = VanillaElementCatalog.Get(id);
                if (descriptor == null || VanillaElementPresence.IsRemoved(layout, id))
                {
                    return;
                }

                prompt = ResetAllPrompt.ForVanilla(descriptor.DisplayName, descriptor.SupportsPose);
            }
            else
            {
                var counter = LayoutEditing.FindCounter(layout, id);
                if (counter == null || CounterCatalog.GetDescriptor(counter.Type) == null)
                {
                    return;
                }

                prompt = ResetAllPrompt.ForCounter(LayoutEditing.DisplayName(counter));
            }

            var row = new ElementRow(kind, id);
            AskConfirmation(prompt, "Reset", () => ResetRow(row));
        });

        private void ResetRow(ElementRow row)
        {
            if (!ExistsInLayout(_session.GetEditableLayout(), row.Kind, row.Id))
            {
                return;
            }

            if (row.Kind == EditableElementKind.Vanilla)
            {
                _session.RecordEdit(layout => LayoutEditing.ResetVanilla(layout, row.Id));
                return;
            }

            _session.RecordEdit(layout =>
            {
                var counter = LayoutEditing.FindCounter(layout, row.Id);
                if (counter != null && CounterCatalog.GetDescriptor(counter.Type) != null)
                {
                    LayoutEditing.ResetCounter(layout, counter);
                }
            });
        }

        [UIAction("confirm")]
        private void OnConfirm() => Safe.Invoke("ElementsPanel.Confirm", () =>
        {
            if (PanelSupport.IsClosing(_confirmModal))
            {
                return;
            }

            _parserParams?.EmitEvent("hide-confirm");
            var action = _pendingConfirmation;
            _pendingConfirmation = null;
            _confirmationLayout = null;
            action?.Invoke();
        });

        private void AskConfirmation(string prompt, string buttonText, Action onConfirm)
        {
            _pendingConfirmation = onConfirm;
            _confirmationLayout = _session.GetEditableLayout();
            PanelSupport.SetText(_confirmPrompt, prompt);
            PanelSupport.SetButton(_confirmButton, true, buttonText);
            _parserParams?.EmitEvent("show-confirm");
        }

        private void MarkDirty() => _dirty = true;

        private void HandleSelectionChanged()
        {
            _dirty = true;
            if (!_selectingFromList)
            {
                _scrollToSelection = true;
            }
        }

        private void RecordFormWrite(FormField field, Action write)
        {
            if (!field.IsConfigEdit || !_attached)
            {
                write();
                return;
            }

            _formWriteInProgress = true;
            var written = false;
            _session.RecordEdit(
                _ =>
                {
                    written = true;
                    write();
                },
                "form:" + _formSelectionKey + ":" + field.Label);
            if (!written)
            {
                write();
                _config.MarkDirty();
            }
        }

        private void Refresh()
        {
            _dirty = false;

            var layout = Safe.Invoke<HudLayout?>("ElementsPanel.Layout", () => _session.GetEditableLayout(), null);
            if (layout != null && _pendingConfirmation != null && !ReferenceEquals(layout, _confirmationLayout))
            {
                _pendingConfirmation = null;
                _confirmationLayout = null;
                _parserParams?.EmitEvent("hide-confirm");
            }

            if (layout != null && _addLayout != null && !ReferenceEquals(layout, _addLayout))
            {
                _pendingAdd = null;
                _addLayout = null;
                _parserParams?.EmitEvent("hide-add");
            }

            if (layout != null)
            {
                Safe.Invoke("ElementsPanel.ValidateSelection", () => ValidateSelection(layout));
                Safe.Invoke("ElementsPanel.RefreshList", () => RefreshList(layout));
                Safe.Invoke("ElementsPanel.RefreshButtons", () => RefreshButtons(layout));
            }

            Safe.Invoke("ElementsPanel.RefreshForm", RefreshForm);
        }

        private void ValidateSelection(HudLayout layout)
        {
            var id = _selection.SelectedId;
            if (id != null && ExistsInLayout(layout, _selection.SelectedKind, id))
            {
                return;
            }

            var rows = ListedRows(layout);
            if (rows.Count == 0)
            {
                _selection.Select(EditableElementKind.Vanilla, null);
                return;
            }

            if (id == null)
            {
                _selection.EnsureSelection();
                var picked = _selection.SelectedId;
                if (picked != null && ExistsInLayout(layout, _selection.SelectedKind, picked))
                {
                    return;
                }
            }

            _selection.Select(rows[0].Kind, rows[0].Id);
        }

        private void RefreshList(HudLayout layout)
        {
            if (_elementList == null)
            {
                return;
            }

            var rows = ListedRows(layout);
            var texts = new List<string>(rows.Count);
            foreach (var row in rows)
            {
                texts.Add(row.Kind == EditableElementKind.Vanilla ? VanillaRowText(layout, row.Id) : CounterRowText(layout, row.Id));
            }

            if (!_listLoaded || !texts.SequenceEqual(_rowTexts))
            {
                _rows.Clear();
                _rows.AddRange(rows);
                _cells.Clear();
                foreach (var text in texts)
                {
                    _cells.Add(new CustomListTableData.CustomCellInfo(text));
                }

                _elementList.Data = _cells;
                if (_listLoaded)
                {
                    _elementList.TableView.ReloadDataKeepingPosition();
                }
                else
                {
                    _elementList.TableView.ReloadData();
                    _listLoaded = true;
                }

                _rowTexts.Clear();
                _rowTexts.AddRange(texts);
            }
            else
            {
                _rows.Clear();
                _rows.AddRange(rows);
            }

            var index = _rows.FindIndex(row => _selection.IsSelected(row.Kind, row.Id));
            var table = _elementList.TableView;
            if (index < 0)
            {
                table.ClearSelection();
            }
            else
            {
                table.SelectCellWithIdx(index, callbackTable: false);
                if (_scrollToSelection)
                {
                    table.ScrollToCellWithIdx(index, TableView.ScrollPositionType.Center, animated: false);
                }
            }

            _scrollToSelection = false;
        }

        private string VanillaRowText(HudLayout layout, string id)
        {
            var settings = ShownVanilla(layout, id);
            var hiddenInPlay = settings.Visible
                && AdvancedHudElements.IsHiddenInPlay(id, _hints.PlayerAdvancedHud, _hints.ForceAdvancedHud);
            var name = VanillaElementCatalog.Get(id)?.DisplayName ?? id;
            return RowText(name, VanillaRowColor, !settings.Visible ? "hidden" : hiddenInPlay ? "hidden in play" : null);
        }

        private static string CounterRowText(HudLayout layout, string id)
        {
            var counter = LayoutEditing.FindCounter(layout, id);
            if (counter == null)
            {
                return RowText(id, CounterRowColor, "counter");
            }

            var state = CounterCatalog.GetDescriptor(counter.Type) == null ? "counter, unsupported" : counter.Enabled ? "counter" : "counter, hidden";
            return RowText(LayoutEditing.DisplayName(counter), CounterRowColor, state);
        }

        private void RefreshButtons(HudLayout layout)
        {
            var editing = _session.IsEditing;
            var id = _selection.SelectedId;
            var kind = _selection.SelectedKind;
            var counter = kind == EditableElementKind.Counter && id != null ? LayoutEditing.FindCounter(layout, id) : null;
            var vanilla = kind == EditableElementKind.Vanilla && id != null ? VanillaElementCatalog.Get(id) : null;
            var inLayout = vanilla != null || counter != null;
            var visible = vanilla != null ? ShownVanilla(layout, vanilla.Id).Visible : counter?.Enabled ?? true;

            PanelSupport.SetButton(_addButton, editing);
            PanelSupport.SetButton(_copyButton, editing && counter != null && CounterCatalog.GetDescriptor(counter.Type) != null);
            PanelSupport.SetButton(_removeButton, editing && inLayout);
            PanelSupport.SetButton(_visibilityButton, editing && inLayout, visible ? "Hide" : "Show");

            var name = vanilla != null ? vanilla.DisplayName : counter != null ? LayoutEditing.DisplayName(counter) : "Nothing selected";
            var title = vanilla != null ? name + "  (vanilla HUD)" : counter != null ? name + "  (counter)" : name;
            PanelSupport.SetText(_settingsTitle, title);
            PanelSupport.SetButton(
                _resetAllButton,
                editing && (vanilla != null || (counter != null && CounterCatalog.GetDescriptor(counter.Type) != null)));
        }

        private void RefreshForm()
        {
            var formWrite = _formWriteInProgress;
            _formWriteInProgress = false;
            var reloadAfterWrite = _formNeedsReload;
            _formNeedsReload = false;

            var form = _form;
            if (form == null)
            {
                return;
            }

            var id = _selection.SelectedId;
            var kind = _selection.SelectedKind;
            var key = id == null ? null : kind + ":" + id;
            var target = id == null ? null : ResolveFormTarget(_session.GetEditableLayout(), kind, id);

            if (target == null)
            {
                if (_formTarget != null)
                {
                    form.Clear();
                }

                _formTarget = null;
                _formSelectionKey = key;
                return;
            }

            if (!ReferenceEquals(target, _formTarget) || !string.Equals(key, _formSelectionKey, StringComparison.Ordinal))
            {
                var sameElement = string.Equals(key, _formSelectionKey, StringComparison.Ordinal);
                _formTarget = target;
                _formSelectionKey = key;
                form.Build(BuildFields(target, id!), keepScrollPosition: sameElement);
            }
            else if (!formWrite || reloadAfterWrite)
            {
                form.ReloadValues();
            }
        }

        private void Reattach(CounterSettings counter, CounterAttachment attachment)
        {
            if (_selection.Selected is IAttachableElement view && string.Equals(_selection.SelectedId, counter.Id, StringComparison.Ordinal))
            {
                view.Reattach(counter, attachment);
            }
            else
            {
                counter.Attachment = attachment;
            }

            _formNeedsReload = true;
        }

        private void SetMoveWithHud(VanillaElementSettings settings, bool moveWithHud)
        {
            if (_selection.Selected is IHudFollowElement element && ReferenceEquals(settings, _formTarget))
            {
                element.SetMoveWithHud(_session.GetEditableLayout(), moveWithHud);
            }
            else
            {
                settings.MoveWithHud = moveWithHud;
            }

            // rebuild so the rows say Offset or Position
            _formTarget = null;
        }

        private List<FormField> BuildFields(object target, string id) => target switch
        {
            VanillaElementSettings vanilla => ElementForms.ForVanilla(_formContext, vanilla, VanillaElementCatalog.Get(id)),
            CounterSettings counter => ElementForms.ForCounter(_formContext, counter, CounterCatalog.GetDescriptor(counter.Type)),
            _ => new List<FormField>(),
        };

        private void FillAddList()
        {
            if (_addList == null)
            {
                return;
            }

            _addRows.Clear();
            _addCells.Clear();

            foreach (var id in VanillaElementPresence.RemovedIds(_session.GetEditableLayout()))
            {
                var descriptor = VanillaElementCatalog.Get(id);
                if (descriptor == null)
                {
                    continue;
                }

                _addRows.Add(new AddChoice(
                    EditableElementKind.Vanilla,
                    id,
                    descriptor.DisplayName,
                    "Brings the vanilla " + descriptor.DisplayName + " back where it was, with its settings."));
                _addCells.Add(new CustomListTableData.CustomCellInfo(descriptor.DisplayName, RemovedVanillaSubtext));
            }

            var ordered = CounterCatalog.All
                .Select((presenter, order) => (Descriptor: presenter.Descriptor, Order: order))
                .Where(entry => entry.Descriptor != null)
                .OrderBy(entry => (int)entry.Descriptor.Category)
                .ThenBy(entry => entry.Order);
            foreach (var entry in ordered)
            {
                var descriptor = entry.Descriptor;
                var subtext = "Counter - " + CategoryName(descriptor.Category);
                _addRows.Add(new AddChoice(EditableElementKind.Counter, descriptor.TypeId, descriptor.DisplayName, descriptor.Description));
                _addCells.Add(new CustomListTableData.CustomCellInfo(descriptor.DisplayName, subtext));
            }

            _addList.Data = _addCells;
            _addList.TableView.ReloadData();
            _addList.TableView.ClearSelection();
            if (_addCells.Count > 0)
            {
                _addList.TableView.ScrollToCellWithIdx(0, TableView.ScrollPositionType.Beginning, animated: false);
            }

            _pendingAdd = null;
            UpdatePendingAdd();
        }

        private void UpdatePendingAdd()
        {
            var choice = _pendingAdd;

            PanelSupport.SetText(
                _addDescription,
                choice == null
                    ? "Select an element to see what it adds."
                    : string.IsNullOrWhiteSpace(choice.Description) ? choice.DisplayName : choice.Description);
            PanelSupport.SetButton(_addConfirmButton, choice != null);
        }

        private const string VanillaRowColor = "#8FD8FF";

        private const string CounterRowColor = "#C49BFF";

        private static string RowText(string name, string color, string? state)
        {
            var text = "<color=" + color + "><noparse>" + name.Replace("</noparse>", string.Empty) + "</noparse></color>";
            return state == null ? text : text + "  <color=#FFFFFF80>(" + state + ")</color>";
        }

        private string? SelectedCounterId() =>
            _selection.SelectedKind == EditableElementKind.Counter ? _selection.SelectedId : null;

        private static List<ElementRow> ListedRows(HudLayout layout)
        {
            var rows = new List<ElementRow>();
            foreach (var id in VanillaElementPresence.ListedIds(layout))
            {
                rows.Add(new ElementRow(EditableElementKind.Vanilla, id));
            }

            var counters = layout.Counters;
            if (counters != null)
            {
                foreach (var counter in counters)
                {
                    if (counter != null)
                    {
                        rows.Add(new ElementRow(EditableElementKind.Counter, counter.Id));
                    }
                }
            }

            return rows;
        }

        private void SelectListedRow(int index)
        {
            var rows = ListedRows(_session.GetEditableLayout());
            if (rows.Count == 0)
            {
                _selection.Select(EditableElementKind.Vanilla, null);
                return;
            }

            var row = rows[Math.Min(index, rows.Count - 1)];
            _selection.Select(row.Kind, row.Id);
        }

        private static bool ExistsInLayout(HudLayout layout, EditableElementKind kind, string id) =>
            kind == EditableElementKind.Vanilla
                ? VanillaElementCatalog.Get(id) != null && !VanillaElementPresence.IsRemoved(layout, id)
                : LayoutEditing.FindCounter(layout, id) != null;

        private static object? ResolveFormTarget(HudLayout editable, EditableElementKind kind, string id)
        {
            if (kind == EditableElementKind.Vanilla)
            {
                return VanillaElementCatalog.Get(id) != null ? LayoutEditing.GetVanilla(editable, id) : null;
            }

            return LayoutEditing.FindCounter(editable, id);
        }

        private static VanillaElementSettings ShownVanilla(HudLayout layout, string id) =>
            layout.Vanilla != null && layout.Vanilla.TryGetValue(id, out var settings) && settings != null
                ? settings
                : DefaultConfigFactory.CreateVanillaDefaults(id);

        private static string CategoryName(CounterCategory category) => category switch
        {
            CounterCategory.Accuracy => "Accuracy",
            CounterCategory.Score => "Score",
            CounterCategory.Notes => "Notes",
            CounterCategory.Swing => "Swing",
            CounterCategory.Leaderboard => "Leaderboard",
            CounterCategory.Health => "Energy",
            CounterCategory.Time => "Time",
            CounterCategory.Info => "Info",
            _ => category.ToString(),
        };

        private readonly struct ElementRow
        {
            public ElementRow(EditableElementKind kind, string id)
            {
                Kind = kind;
                Id = id;
            }

            public EditableElementKind Kind { get; }

            public string Id { get; }

            public bool Is(EditableElementKind kind, string id) =>
                Kind == kind && string.Equals(Id, id, StringComparison.Ordinal);
        }

        private sealed class AddChoice
        {
            public AddChoice(EditableElementKind kind, string id, string displayName, string description)
            {
                Kind = kind;
                Id = id;
                DisplayName = displayName;
                Description = description;
            }

            public EditableElementKind Kind { get; }

            public string Id { get; }

            public string DisplayName { get; }

            public string Description { get; }
        }
    }
}
