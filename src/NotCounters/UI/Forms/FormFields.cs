using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using BeatSaberMarkupLanguage.Attributes;
using NotCounters.UI.Panels;
using NotCounters.Util;
using UnityEngine;
using static NotCounters.UI.Forms.BsmlMarkup;

namespace NotCounters.UI.Forms
{
    internal sealed class FormStyle
    {
        public FormStyle(float rowWidth)
        {
            RowWidth = rowWidth;
        }

        public float RowWidth { get; }

        public bool IsFirstRow { get; private init; }

        public FormStyle ForFirstRow() => new FormStyle(RowWidth) { IsFirstRow = true };
    }

    internal abstract class FormField
    {
        protected FormField(string label)
        {
            Label = label ?? string.Empty;
        }

        public string Label { get; }

        public string? HoverHint { get; init; }

        public Func<bool>? EnabledWhen { get; init; }

        public float? LabelFontSize { get; init; }

        public bool IsConfigEdit { get; init; } = true;

        public abstract string BuildMarkup(FormStyle style);

        public abstract ISettingHost? CreateHost(FormRowHooks hooks);

        protected string SettingAttributes(FormStyle style)
        {
            var markup = Attr("id", "field") + Attr("text", Label) + Attr("pref-width", style.RowWidth);
            if (LabelFontSize.HasValue)
            {
                markup += Attr("font-size", LabelFontSize.Value);
            }

            return string.IsNullOrEmpty(HoverHint) ? markup : markup + Attr("hover-hint", HoverHint);
        }
    }

    internal sealed class SectionField : FormField
    {
        public SectionField(string title)
            : base(title)
        {
        }

        public override string BuildMarkup(FormStyle style) =>
            "<horizontal" + Attr("pref-width", style.RowWidth) + Attr("pref-height", style.IsFirstRow ? 6 : 8) + Attr("pad-top", style.IsFirstRow ? 0 : 2) + Attr("child-align", "LowerLeft") + ">"
            + "<text" + Attr("text", Label) + Attr("font-size", 4.4) + Attr("color", "#8FD3FF") + Attr("align", "BottomLeft") + Attr("rich-text", false) + "/>"
            + "</horizontal>";

        public override ISettingHost? CreateHost(FormRowHooks hooks) => null;
    }

    internal sealed class NoteField : FormField
    {
        private readonly float _height;

        public NoteField(string text, float height = 8f)
            : base(text)
        {
            _height = height;
        }

        public override string BuildMarkup(FormStyle style) =>
            "<horizontal" + Attr("pref-width", style.RowWidth) + Attr("pref-height", _height) + ">"
            + "<text" + Attr("text", Label) + Attr("font-size", 3.4) + Attr("color", "#FFFFFFCC") + Attr("word-wrapping", true)
            + Attr("align", "TopLeft") + Attr("rich-text", false) + "/>"
            + "</horizontal>";

        public override ISettingHost? CreateHost(FormRowHooks hooks) => null;
    }

    internal sealed class DynamicTextField : FormField
    {
        private readonly Func<string> _text;
        private readonly float _height;

        public DynamicTextField(Func<string> text, float height = 6f)
            : base(string.Empty)
        {
            _text = text;
            _height = height;
        }

        public override string BuildMarkup(FormStyle style) =>
            "<horizontal" + Attr("pref-width", style.RowWidth) + Attr("pref-height", _height) + ">"
            + "<text" + Attr("text", "~text") + Attr("font-size", 3.4) + Attr("color", "#FFFFFFCC") + Attr("word-wrapping", true)
            + Attr("align", "MidlineLeft") + Attr("rich-text", false) + "/>"
            + "</horizontal>";

        public override ISettingHost? CreateHost(FormRowHooks hooks) => new DynamicTextHost(_text);

        private sealed class DynamicTextHost : ISettingHost, IRefreshableHost, INotifyPropertyChanged
        {
            private readonly Func<string> _text;

            public DynamicTextHost(Func<string> text)
            {
                _text = text;
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            [UIValue("text")]
            public string Text => Safe.Invoke("ui.form.dynamicText", _text, string.Empty) ?? string.Empty;

            public BeatSaberMarkupLanguage.Components.Settings.GenericSetting? Setting => null;

            public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }
    }

    internal sealed class ButtonField : FormField
    {
        private const float ButtonWidth = 60f;

        private const float ButtonHeight = 8f;

        private const float RowHeight = 10f;

        private const float FontSize = 3.8f;

        private readonly Action _click;

        public ButtonField(string label, Action click)
            : base(label)
        {
            _click = click;
        }

        public override string BuildMarkup(FormStyle style)
        {
            var button = "<button" + Attr("id", "button") + Attr("text", Label) + Attr("on-click", "click")
                + Attr("pref-width", ButtonWidth) + Attr("pref-height", ButtonHeight) + Attr("font-size", FontSize);
            if (!string.IsNullOrEmpty(HoverHint))
            {
                button += Attr("hover-hint", HoverHint);
            }

            return "<horizontal" + Attr("pref-width", style.RowWidth) + Attr("pref-height", RowHeight)
                + Attr("child-control-width", true) + Attr("child-control-height", true)
                + Attr("child-expand-width", false) + Attr("child-expand-height", false) + Attr("child-align", "MiddleCenter") + ">"
                + button + "/>"
                + "</horizontal>";
        }

        public override ISettingHost? CreateHost(FormRowHooks hooks) => new ButtonHost(Label, _click, hooks);

        private sealed class ButtonHost : ISettingHost, IInteractableHost
        {
            private readonly string _key;
            private readonly Action _click;
            private readonly FormRowHooks _hooks;

            [UIComponent("button")]
            private UnityEngine.UI.Button? _button = null;

            public ButtonHost(string key, Action click, FormRowHooks hooks)
            {
                _key = key;
                _click = click;
                _hooks = hooks;
            }

            public BeatSaberMarkupLanguage.Components.Settings.GenericSetting? Setting => null;

            public void SetInteractable(bool interactable) => PanelSupport.SetButton(_button, interactable);

            [UIAction("click")]
            private void Click()
            {
                _hooks.Write(() => Safe.Invoke("ui.form.click:" + _key, _click));
                Safe.Invoke("ui.form.afterChange", _hooks.AfterChange);
            }
        }
    }

    internal sealed class ToggleField : FormField
    {
        private readonly Func<bool> _get;
        private readonly Action<bool> _set;

        public ToggleField(string label, Func<bool> get, Action<bool> set)
            : base(label)
        {
            _get = get;
            _set = set;
        }

        public override string BuildMarkup(FormStyle style) =>
            "<toggle-setting" + SettingAttributes(style) + Attr("value", "value") + Attr("apply-on-change", true) + "/>";

        public override ISettingHost? CreateHost(FormRowHooks hooks) =>
            new SettingHost<bool>(Label, _get, _set, false, hooks);
    }

    internal enum NumberStyle
    {
        Slider,
        Increment,
    }

    internal sealed class NumberField : FormField
    {
        private readonly Func<double> _get;
        private readonly Action<double> _set;

        public NumberField(string label, double min, double max, double step, Func<double> get, Action<double> set)
            : base(label)
        {
            Min = Math.Min(min, max);
            Max = Math.Max(min, max);
            Step = step > 0 ? step : 1;
            _get = get;
            _set = set;
        }

        public double Min { get; }

        public double Max { get; }

        public double Step { get; }

        public bool IsInteger { get; init; }

        public NumberStyle Style { get; init; } = NumberStyle.Increment;

        public Func<double, string>? Format { get; init; }

        public Func<double>? StepSource { get; init; }

        public double CurrentStep => StepSource?.Invoke() ?? Step;

        public override string BuildMarkup(FormStyle style)
        {
            var markup = new StringBuilder();
            markup.Append(Style == NumberStyle.Slider ? "<slider-setting" : "<increment-setting");
            markup.Append(SettingAttributes(style));
            markup.Append(Attr("min", Min)).Append(Attr("max", Max)).Append(Attr("increment", CurrentStep));
            markup.Append(Attr("integer-only", IsInteger));
            if (Style == NumberStyle.Slider)
            {
                markup.Append(Attr("show-buttons", true));
            }

            markup.Append(Attr("formatter", "format")).Append(Attr("value", "value")).Append(Attr("apply-on-change", true)).Append("/>");
            return markup.ToString();
        }

        public override ISettingHost? CreateHost(FormRowHooks hooks)
        {
            string FormatBoxed(object? boxed) => FormatValue(Convert.ToDouble(boxed, CultureInfo.InvariantCulture));

            if (IsInteger)
            {
                return new SettingHost<int>(
                    Label,
                    () => (int)Math.Round(ReadClamped()),
                    v => _set(Clamp(v)),
                    (int)Math.Round(Min),
                    hooks,
                    FormatBoxed);
            }

            return new SettingHost<float>(
                Label,
                () => (float)ReadClamped(),
                v => _set(Clamp(v)),
                (float)Min,
                hooks,
                FormatBoxed);
        }

        public string DefaultFormat(double value) =>
            value.ToString("F" + DecimalsFor(CurrentStep).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        public static int DecimalsFor(double step)
        {
            for (var decimals = 0; decimals < 4; decimals++)
            {
                var scaled = step * Math.Pow(10, decimals);
                if (Math.Abs(scaled - Math.Round(scaled)) < 1e-6)
                {
                    return decimals;
                }
            }

            return 4;
        }

        private string FormatValue(double value) => Format != null ? Format(value) : DefaultFormat(value);

        private double ReadClamped() => Clamp(_get());

        private double Clamp(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return Min;
            }

            return value < Min ? Min : value > Max ? Max : value;
        }
    }

    internal sealed class ChoiceItem
    {
        public ChoiceItem(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public string Key { get; }

        public string Label { get; }

        public override string ToString() => Label;
    }

    internal sealed class ChoiceField : FormField
    {
        private readonly IReadOnlyList<ChoiceItem> _items;
        private readonly Func<string> _getKey;
        private readonly Action<string> _setKey;

        public ChoiceField(string label, IReadOnlyList<ChoiceItem> items, Func<string> getKey, Action<string> setKey)
            : base(label)
        {
            _items = items;
            _getKey = getKey;
            _setKey = setKey;
        }

        public override string BuildMarkup(FormStyle style) =>
            "<list-setting" + SettingAttributes(style) + Attr("options", "options") + Attr("value", "value") + Attr("apply-on-change", true) + "/>";

        public override ISettingHost? CreateHost(FormRowHooks hooks)
        {
            var options = new List<object>(_items.Count);
            foreach (var item in _items)
            {
                options.Add(item);
            }

            object Find()
            {
                var key = _getKey();
                foreach (var item in _items)
                {
                    if (string.Equals(item.Key, key, StringComparison.Ordinal))
                    {
                        return item;
                    }
                }

                return _items.Count > 0 ? _items[0] : string.Empty;
            }

            void Write(object selected)
            {
                if (selected is ChoiceItem item)
                {
                    _setKey(item.Key);
                }
            }

            return new SettingHost<object>(Label, Find, Write, _items.Count > 0 ? _items[0] : string.Empty, hooks, options: options);
        }
    }

    internal sealed class TextField : FormField
    {
        private readonly Func<string?> _get;
        private readonly Action<string?> _set;
        private readonly string _placeholder;

        public TextField(string label, Func<string?> get, Action<string?> set, string placeholder)
            : base(label)
        {
            _get = get;
            _set = set;
            _placeholder = placeholder;
        }

        public override string BuildMarkup(FormStyle style) =>
            "<string-setting" + SettingAttributes(style) + Attr("formatter", "format") + Attr("value", "value") + Attr("apply-on-change", true) + "/>";

        public override ISettingHost? CreateHost(FormRowHooks hooks) =>
            new SettingHost<string>(
                Label,
                () => _get() ?? string.Empty,
                v => _set(string.IsNullOrEmpty(v) ? null : v),
                string.Empty,
                hooks,
                v => v is string s && s.Length > 0 ? s : _placeholder);
    }

    internal sealed class ColorField : FormField
    {
        private readonly Func<Color> _get;
        private readonly Action<Color> _set;

        public ColorField(string label, Func<Color> get, Action<Color> set)
            : base(label)
        {
            _get = get;
            _set = set;
        }

        public override string BuildMarkup(FormStyle style) =>
            "<color-setting" + SettingAttributes(style) + Attr("value", "value") + Attr("apply-on-change", true) + "/>";

        public override ISettingHost? CreateHost(FormRowHooks hooks) =>
            new SettingHost<Color>(Label, _get, _set, Color.white, hooks);
    }
}
