using System;
using System.Collections.Generic;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components.Settings;
using NotCounters.Util;

namespace NotCounters.UI.Forms
{
    internal interface ISettingHost
    {
        GenericSetting? Setting { get; }
    }

    internal interface IRefreshableHost
    {
        void Refresh();
    }

    internal interface IInteractableHost
    {
        void SetInteractable(bool interactable);
    }

    internal sealed class FormRowHooks
    {
        public FormRowHooks(Action afterChange, Func<bool> writesSuppressed, Action<Action> write)
        {
            AfterChange = afterChange;
            WritesSuppressed = writesSuppressed;
            Write = write;
        }

        public Action<Action> Write { get; }

        public Action AfterChange { get; }

        public Func<bool> WritesSuppressed { get; }
    }

    internal sealed class SettingHost<T> : ISettingHost
    {
        private readonly string _key;
        private readonly Func<T> _get;
        private readonly Action<T> _set;
        private readonly T _fallback;
        private readonly Func<object?, string>? _format;
        private readonly FormRowHooks _hooks;

        public SettingHost(
            string key,
            Func<T> get,
            Action<T> set,
            T fallback,
            FormRowHooks hooks,
            Func<object?, string>? format = null,
            List<object>? options = null)
        {
            _key = key;
            _get = get;
            _set = set;
            _fallback = fallback;
            _hooks = hooks;
            _format = format;
            Options = options ?? new List<object>();
        }

        [UIValue("value")]
        public T Value
        {
            get => Safe.Invoke("ui.form.get:" + _key, _get, _fallback);
            set
            {
                if (_hooks.WritesSuppressed() || IsCurrentValue(value))
                {
                    return;
                }

                _hooks.Write(() => Safe.Invoke("ui.form.set:" + _key, () => _set(value)));
                Safe.Invoke("ui.form.afterChange", _hooks.AfterChange);
            }
        }

        [UIValue("options")]
        public List<object> Options { get; }

        [UIComponent("field")]
        public GenericSetting? Setting { get; set; }

        [UIAction("format")]
        public string Format(object? value)
        {
            if (_format == null)
            {
                return value?.ToString() ?? string.Empty;
            }

            return Safe.Invoke("ui.form.format:" + _key, () => _format(value), value?.ToString() ?? string.Empty);
        }

        private bool IsCurrentValue(T value) =>
            Safe.Invoke("ui.form.get:" + _key, () => EqualityComparer<T>.Default.Equals(value, _get()), false);
    }
}
