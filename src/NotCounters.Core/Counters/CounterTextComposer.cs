using System.Collections.Generic;
using NotCounters.Core.Config;

namespace NotCounters.Core.Counters
{
    public enum CounterTextLayoutKind
    {
        ValueOnly,
        LabelAbove,
        LabelBelow,
        LabelLeft,
        LabelRight,
        Inline,
    }

    public readonly struct ComposedCounterText
    {
        public ComposedCounterText(string? label, string value, CounterTextLayoutKind layout, ColorValue valueColor, ColorValue labelColor, bool hidden)
            : this(label, value, layout, valueColor, labelColor, hidden, null)
        {
        }

        public ComposedCounterText(string? label, string value, CounterTextLayoutKind layout, ColorValue valueColor, ColorValue labelColor, bool hidden, IReadOnlyList<CounterRow>? rows)
            : this(label, value, layout, valueColor, labelColor, hidden, rows, null, valueColor)
        {
        }

        public ComposedCounterText(
            string? label,
            string value,
            CounterTextLayoutKind layout,
            ColorValue valueColor,
            ColorValue labelColor,
            bool hidden,
            IReadOnlyList<CounterRow>? rows,
            string? detail,
            ColorValue detailColor)
            : this(label, value, layout, valueColor, labelColor, hidden, rows, detail, detailColor, false)
        {
        }

        public ComposedCounterText(
            string? label,
            string value,
            CounterTextLayoutKind layout,
            ColorValue valueColor,
            ColorValue labelColor,
            bool hidden,
            IReadOnlyList<CounterRow>? rows,
            string? detail,
            ColorValue detailColor,
            bool reservesDetail)
            : this(label, value, layout, valueColor, labelColor, hidden, rows, detail, detailColor, reservesDetail, false)
        {
        }

        public ComposedCounterText(
            string? label,
            string value,
            CounterTextLayoutKind layout,
            ColorValue valueColor,
            ColorValue labelColor,
            bool hidden,
            IReadOnlyList<CounterRow>? rows,
            string? detail,
            ColorValue detailColor,
            bool reservesDetail,
            bool richText)
        {
            Label = label;
            Value = value;
            Layout = layout;
            ValueColor = valueColor;
            LabelColor = labelColor;
            Hidden = hidden;
            Rows = rows != null && rows.Count > 0 ? rows : null;
            Detail = Rows == null && !string.IsNullOrWhiteSpace(detail) ? detail : null;
            DetailColor = detailColor;
            ReservesDetail = Rows == null && reservesDetail;
            RichText = Rows == null && richText;
        }

        public string? Label { get; }

        public string Value { get; }

        public CounterTextLayoutKind Layout { get; }

        public ColorValue ValueColor { get; }

        public ColorValue LabelColor { get; }

        public bool Hidden { get; }

        public IReadOnlyList<CounterRow>? Rows { get; }

        public string? Detail { get; }

        public ColorValue DetailColor { get; }

        public bool ReservesDetail { get; }

        public bool RichText { get; }
    }

    public static class CounterTextComposer
    {
        private static readonly TextStyle DefaultStyle = new TextStyle();

        public static ComposedCounterText Compose(CounterRender render, CounterSettings settings, CounterTypeDescriptor descriptor)
        {
            var style = settings?.Style ?? DefaultStyle;
            var value = render.Value ?? string.Empty;
            var valueColor = style.ColorByValue && render.ValueColor.HasValue ? render.ValueColor.Value : style.ValueColor;
            var detail = render.Detail;
            var detailColor = style.ColorByValue && render.DetailColor.HasValue ? render.DetailColor.Value : style.ValueColor;
            var richText = render.RichText;
            if (!style.ShowLabel)
            {
                return new ComposedCounterText(null, value, CounterTextLayoutKind.ValueOnly, valueColor, style.LabelColor, render.Hidden, null, detail, detailColor, render.ReservesDetail, richText);
            }

            var rows = render.Rows;
            if (rows != null && rows.Count > 0)
            {
                var title = NonBlank(style.LabelText) ?? NonBlank(render.LabelOverride);
                var titleLayout = title == null ? CounterTextLayoutKind.ValueOnly : TitleLayout(style.LabelPlacement);
                return new ComposedCounterText(title, value, titleLayout, valueColor, style.LabelColor, render.Hidden, rows);
            }

            var label = NonBlank(style.LabelText) ?? NonBlank(render.LabelOverride) ?? NonBlank(descriptor?.DefaultLabel);
            if (label == null)
            {
                return new ComposedCounterText(null, value, CounterTextLayoutKind.ValueOnly, valueColor, style.LabelColor, render.Hidden, null, detail, detailColor, render.ReservesDetail, richText);
            }

            CounterTextLayoutKind layout;
            switch (style.LabelPlacement)
            {
                case LabelPlacement.Below:
                    layout = CounterTextLayoutKind.LabelBelow;
                    break;
                case LabelPlacement.Left:
                    layout = CounterTextLayoutKind.LabelLeft;
                    break;
                case LabelPlacement.Right:
                    layout = CounterTextLayoutKind.LabelRight;
                    break;
                case LabelPlacement.Inline:
                    var prefix = richText && label.IndexOf('<') >= 0 ? "<noparse>" + label + "</noparse>: " : label + ": ";
                    return new ComposedCounterText(null, prefix + value, CounterTextLayoutKind.Inline, valueColor, style.LabelColor, render.Hidden, null, detail, detailColor, render.ReservesDetail, richText);
                default:
                    layout = CounterTextLayoutKind.LabelAbove;
                    break;
            }

            return new ComposedCounterText(label, value, layout, valueColor, style.LabelColor, render.Hidden, null, detail, detailColor, render.ReservesDetail, richText);
        }

        private static CounterTextLayoutKind TitleLayout(LabelPlacement placement)
        {
            switch (placement)
            {
                case LabelPlacement.Below:
                    return CounterTextLayoutKind.LabelBelow;
                case LabelPlacement.Left:
                case LabelPlacement.Inline:
                    return CounterTextLayoutKind.LabelLeft;
                case LabelPlacement.Right:
                    return CounterTextLayoutKind.LabelRight;
                default:
                    return CounterTextLayoutKind.LabelAbove;
            }
        }

        private static string? NonBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
