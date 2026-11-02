using System;
using System.Collections.Generic;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Components.Settings;
using HMUI;
using NotCounters.UI.Panels;
using NotCounters.Util;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NotCounters.UI.Forms
{
    internal sealed class SettingsForm
    {
        private readonly BSMLParser _parser;
        private readonly Transform _container;
        private readonly FormStyle _style;
        private readonly Action<GameObject>? _prepareRows;
        private readonly Action<FormField, Action>? _recordWrite;
        private readonly List<RenderedRow> _rows = new List<RenderedRow>();

        private bool _pushingValues;

        public SettingsForm(
            BSMLParser parser,
            GameObject container,
            FormStyle style,
            Action<GameObject>? prepareRows = null,
            Action<FormField, Action>? recordWrite = null)
        {
            _parser = parser;
            _container = container.transform;
            _style = style;
            _prepareRows = prepareRows;
            _recordWrite = recordWrite;
        }

        public event Action<FormField>? RowChanged;

        public void Build(IEnumerable<FormField> fields, bool keepScrollPosition = false)
        {
            var scrollView = _container.GetComponentInParent<ScrollView>(true);
            var previousPosition = keepScrollPosition && scrollView != null ? scrollView.position : 0f;

            Clear();

            _pushingValues = true;
            try
            {
                var firstRow = true;
                foreach (var field in fields)
                {
                    Render(field, firstRow);
                    firstRow = false;
                }
            }
            finally
            {
                _pushingValues = false;
            }

            if (_prepareRows != null)
            {
                var root = _container.gameObject;
                Safe.Invoke("ui.form.prepareRows", () => _prepareRows(root));
            }

            RefreshState();

            if (scrollView != null)
            {
                var target = previousPosition;
                Safe.Invoke("ui.form.scroll", () =>
                {
                    if (target > 0f && scrollView.contentTransform != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollView.contentTransform);
                        scrollView.UpdateContentSize();
                    }

                    scrollView.ScrollTo(target, animated: false);
                });
            }
        }

        public void Clear()
        {
            foreach (var row in _rows)
            {
                var modal = RowModal(row.Setting);
                if (modal == null)
                {
                    continue;
                }

                if (modal.isShown)
                {
                    Safe.Invoke("ui.form.closeModal", () => modal.Hide(animated: false));
                }
                else
                {
                    Safe.Invoke("ui.form.finishModal", () => PanelSupport.FinishDismissal(modal));
                }
            }

            _rows.Clear();
            for (var i = _container.childCount - 1; i >= 0; i--)
            {
                var child = _container.GetChild(i).gameObject;

                child.SetActive(false);
                Object.Destroy(child);
            }
        }

        public void ReloadValues()
        {
            _pushingValues = true;
            try
            {
                foreach (var row in _rows)
                {
                    if (row.Setting != null)
                    {
                        var setting = row.Setting;
                        Safe.Invoke("ui.form.reload", setting.ReceiveValue);
                    }
                    else if (row.Host is IRefreshableHost refreshable)
                    {
                        Safe.Invoke("ui.form.refresh", refreshable.Refresh);
                    }
                }
            }
            finally
            {
                _pushingValues = false;
            }

            RefreshState();
        }

        public void RefreshState()
        {
            foreach (var row in _rows)
            {
                try
                {
                    RefreshRowState(row);
                }
                catch (Exception ex)
                {
                    Safe.Report("ui.form.state:" + row.Field.Label, ex);
                }
            }
        }

        private static void RefreshRowState(RenderedRow row)
        {
            var enabled = row.Field.EnabledWhen == null || Safe.Invoke("ui.form.enabledWhen", row.Field.EnabledWhen, true);
            if (row.Setting == null)
            {
                if (row.Host is IInteractableHost host)
                {
                    Safe.Invoke("ui.form.interactable", () => host.SetInteractable(enabled));
                }

                return;
            }

            if (row.Setting is GenericInteractableSetting interactable && interactable.Interactable != enabled)
            {
                interactable.Interactable = enabled;
            }

            if (row.Field is NumberField number && number.StepSource != null && row.Setting is IncrementSetting increment)
            {
                increment.Increments = (float)number.CurrentStep;
            }
        }

        private void Render(FormField field, bool firstRow)
        {
            var firstNewChild = _container.childCount;
            try
            {
                var hooks = new FormRowHooks(() => OnRowChanged(field), () => _pushingValues, write => WriteRow(field, write));
                var host = field.CreateHost(hooks);
                _parser.Parse(field.BuildMarkup(firstRow ? _style.ForFirstRow() : _style), _container.gameObject, host);
                _rows.Add(new RenderedRow(field, host, host?.Setting));
            }
            catch (Exception ex)
            {
                Safe.Report("ui.form.render:" + field.Label, ex);

                for (var i = _container.childCount - 1; i >= firstNewChild; i--)
                {
                    var leftover = _container.GetChild(i).gameObject;
                    leftover.SetActive(false);
                    Object.Destroy(leftover);
                }
            }
        }

        private static ModalView? RowModal(GenericSetting? setting)
        {
            ModalView? modal = null;
            if (setting is ColorSetting color && color.ModalColorPicker != null)
            {
                modal = color.ModalColorPicker.ModalView;
            }
            else if (setting is StringSetting text && text.ModalKeyboard != null)
            {
                modal = text.ModalKeyboard.ModalView;
            }

            return modal != null ? modal : null;
        }

        private void WriteRow(FormField field, Action write)
        {
            var recorder = _recordWrite;
            if (recorder == null)
            {
                write();
                return;
            }

            var written = false;
            Safe.Invoke("ui.form.rowChanged", () => recorder(field, () =>
            {
                if (!written)
                {
                    written = true;
                    write();
                }
            }));

            if (!written)
            {
                written = true;
                write();
            }
        }

        private void OnRowChanged(FormField field)
        {
            RefreshState();
            var handlers = RowChanged;
            if (handlers == null)
            {
                return;
            }

            foreach (var handler in handlers.GetInvocationList())
            {
                Safe.Invoke("ui.form.rowChanged", () => ((Action<FormField>)handler)(field));
            }
        }

        private readonly struct RenderedRow
        {
            public RenderedRow(FormField field, ISettingHost? host, GenericSetting? setting)
            {
                Field = field;
                Host = host;
                Setting = setting;
            }

            public FormField Field { get; }

            public ISettingHost? Host { get; }

            public GenericSetting? Setting { get; }
        }
    }
}
