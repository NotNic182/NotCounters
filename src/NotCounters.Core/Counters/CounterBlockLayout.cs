using System;
using System.Collections.Generic;
using NotCounters.Core.Config;

namespace NotCounters.Core.Counters
{
    public readonly struct CounterRowMetrics
    {
        public CounterRowMetrics(float textWidth, float textHeight, int lineCount, float logoWidth, float logoHeight, bool logoIsText)
        {
            TextWidth = textWidth;
            TextHeight = textHeight;
            LineCount = lineCount;
            LogoWidth = logoWidth;
            LogoHeight = logoHeight;
            LogoIsText = logoIsText;
        }

        public float TextWidth { get; }

        public float TextHeight { get; }

        public int LineCount { get; }

        public float LogoWidth { get; }

        public float LogoHeight { get; }

        public bool LogoIsText { get; }
    }

    public readonly struct CounterRowPlacement
    {
        public CounterRowPlacement(float textX, float textY, float logoX, float logoY)
        {
            TextX = textX;
            TextY = textY;
            LogoX = logoX;
            LogoY = logoY;
        }

        public float TextX { get; }

        public float TextY { get; }

        public float LogoX { get; }

        public float LogoY { get; }
    }

    public readonly struct CounterContentBox
    {
        public CounterContentBox(float width, float height, float firstLineY, float lastLineY)
            : this(width, height, firstLineY, lastLineY, firstLineY, lastLineY)
        {
        }

        public CounterContentBox(float width, float height, float firstLineY, float lastLineY, float inkTopY, float inkBottomY)
        {
            Width = width;
            Height = height;
            FirstLineY = firstLineY;
            LastLineY = lastLineY;
            InkTopY = inkTopY;
            InkBottomY = inkBottomY;
        }

        public float Width { get; }

        public float Height { get; }

        public float FirstLineY { get; }

        public float LastLineY { get; }

        public float InkTopY { get; }

        public float InkBottomY { get; }

        public static CounterContentBox ForText(float width, float height, int lineCount)
        {
            var line = height / Math.Max(1, lineCount);
            var offset = (height - line) * 0.5f;
            return new CounterContentBox(width, height, offset, -offset);
        }
    }

    public readonly struct CounterDetailStack
    {
        public CounterDetailStack(CounterContentBox box, float valueX, float valueY, float detailX, float detailY)
        {
            Box = box;
            ValueX = valueX;
            ValueY = valueY;
            DetailX = detailX;
            DetailY = detailY;
        }

        public CounterContentBox Box { get; }

        public float ValueX { get; }

        public float ValueY { get; }

        public float DetailX { get; }

        public float DetailY { get; }
    }

    public readonly struct CounterBlockArrangement
    {
        public CounterBlockArrangement(float width, float height, float contentX, float contentY, float captionX, float captionY)
        {
            Width = width;
            Height = height;
            ContentX = contentX;
            ContentY = contentY;
            CaptionX = captionX;
            CaptionY = captionY;
        }

        public float Width { get; }

        public float Height { get; }

        public float ContentX { get; }

        public float ContentY { get; }

        public float CaptionX { get; }

        public float CaptionY { get; }
    }

    public static class CounterBlockLayout
    {
        public const float RowLogoMarkPerCaptionEm = 1f;

        public const float RowPitchPerValueEm = 1f;

        public const float GapPerValueEm = 0.3f;

        public const float RowLogoGapPerValueEm = 0.1f;

        public const float CaptionInkClearancePerValueEm = 0.05f;

        public static float RowLogoMarkHeight(float captionEm) => Math.Max(0f, captionEm) * RowLogoMarkPerCaptionEm;

        public static CounterContentBox ArrangeRows(
            IReadOnlyList<CounterRowMetrics> rows,
            float valueEm,
            float captionEm,
            float capCentreAboveLineCentrePerEm,
            float capHeightPerEm,
            TextAlign align,
            List<CounterRowPlacement> placements)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (placements == null) throw new ArgumentNullException(nameof(placements));

            placements.Clear();
            if (rows.Count == 0)
            {
                return default;
            }

            var logoColumn = 0f;
            var textColumn = 0f;
            var tallestLine = 0f;
            var tallestLogo = 0f;
            var multiLine = false;
            for (var i = 0; i < rows.Count; i++)
            {
                var lines = Math.Max(1, rows[i].LineCount);
                logoColumn = Math.Max(logoColumn, rows[i].LogoWidth);
                textColumn = Math.Max(textColumn, rows[i].TextWidth);
                tallestLine = Math.Max(tallestLine, rows[i].TextHeight / lines);
                tallestLogo = Math.Max(tallestLogo, rows[i].LogoHeight);
                multiLine |= lines > 1;
            }

            var gap = logoColumn > 0f ? GapPerValueEm * valueEm : 0f;
            var width = logoColumn + gap + textColumn;
            var logoX = -width * 0.5f + logoColumn * 0.5f;
            var textColumnCentre = width * 0.5f - textColumn * 0.5f;

            var pitch = RowPitchPerValueEm * valueEm;
            if (multiLine)
            {
                pitch = Math.Max(pitch, tallestLine + GapPerValueEm * valueEm);
            }

            if (tallestLogo > 0f)
            {
                pitch = Math.Max(pitch, tallestLogo + RowLogoGapPerValueEm * valueEm);
            }

            var firstLine = 0f;
            var lastLine = 0f;
            var top = float.NegativeInfinity;
            var bottom = float.PositiveInfinity;
            var inkTop = 0f;
            var inkBottom = float.PositiveInfinity;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var lines = Math.Max(1, row.LineCount);
                var lineHeight = row.TextHeight / lines;
                lastLine = firstLine - (lines - 1) * lineHeight;
                var textY = (firstLine + lastLine) * 0.5f;
                top = Math.Max(top, textY + row.TextHeight * 0.5f);
                bottom = Math.Min(bottom, textY - row.TextHeight * 0.5f);

                var raise = (row.LogoIsText ? valueEm - captionEm : valueEm) * capCentreAboveLineCentrePerEm;
                var logoY = firstLine + raise;
                if (row.LogoHeight > 0f)
                {
                    top = Math.Max(top, logoY + row.LogoHeight * 0.5f);
                    bottom = Math.Min(bottom, logoY - row.LogoHeight * 0.5f);

                    var markY = row.LogoIsText ? logoY + captionEm * capCentreAboveLineCentrePerEm : logoY;
                    var markHalf = row.LogoIsText ? captionEm * capHeightPerEm * 0.5f : row.LogoHeight * 0.5f;
                    inkTop = Math.Max(inkTop, markY + markHalf);
                    inkBottom = Math.Min(inkBottom, markY - markHalf);
                }

                var textX = textColumnCentre + AlignX(row.TextWidth, textColumn, align);
                placements.Add(new CounterRowPlacement(textX, textY, logoX, logoY));
                firstLine = lastLine - pitch;
            }

            inkBottom = Math.Min(inkBottom, lastLine);
            var middle = (top + bottom) * 0.5f;
            for (var i = 0; i < placements.Count; i++)
            {
                var p = placements[i];
                placements[i] = new CounterRowPlacement(p.TextX, p.TextY - middle, p.LogoX, p.LogoY - middle);
            }

            return new CounterContentBox(width, top - bottom, -middle, lastLine - middle, inkTop - middle, inkBottom - middle);
        }

        public static CounterBlockArrangement ArrangeCaption(
            CounterContentBox content,
            float captionWidth,
            float captionHeight,
            int captionLines,
            CounterTextLayoutKind layout,
            float valueEm,
            float captionEm,
            float stackSpacing,
            float capCentreAboveLineCentrePerEm,
            float capHeightPerEm,
            TextAlign align)
        {
            switch (layout)
            {
                case CounterTextLayoutKind.LabelAbove:
                case CounterTextLayoutKind.LabelBelow:
                {
                    var captionLine = captionHeight / Math.Max(1, captionLines);
                    var distance = stackSpacing * (captionEm + valueEm) * 0.5f;
                    var clearance = CaptionInkClearancePerValueEm * valueEm;
                    var capsHalf = capHeightPerEm * 0.5f;
                    var otherLines = (captionHeight - captionLine) * 0.5f;
                    float captionY;
                    if (layout == CounterTextLayoutKind.LabelAbove)
                    {
                        var bottomLine = Math.Max(
                            content.FirstLineY + distance,
                            content.InkTopY + (capsHalf - capCentreAboveLineCentrePerEm) * captionEm + clearance);
                        captionY = bottomLine + otherLines;
                    }
                    else
                    {
                        var topLine = Math.Min(
                            content.LastLineY - distance,
                            content.InkBottomY - (capsHalf + capCentreAboveLineCentrePerEm) * captionEm - clearance);
                        captionY = topLine - otherLines;
                    }

                    var top = Math.Max(content.Height * 0.5f, captionY + captionHeight * 0.5f);
                    var bottom = Math.Min(-content.Height * 0.5f, captionY - captionHeight * 0.5f);
                    var middle = (top + bottom) * 0.5f;
                    var width = Math.Max(content.Width, captionWidth);
                    return new CounterBlockArrangement(
                        width,
                        top - bottom,
                        AlignX(content.Width, width, align),
                        -middle,
                        AlignX(captionWidth, width, align),
                        captionY - middle);
                }

                case CounterTextLayoutKind.LabelLeft:
                case CounterTextLayoutKind.LabelRight:
                {
                    var width = captionWidth + GapPerValueEm * valueEm + content.Width;
                    var height = Math.Max(content.Height, captionHeight);
                    var captionFirst = layout == CounterTextLayoutKind.LabelLeft;
                    var leftX = -width * 0.5f;
                    var rightX = width * 0.5f;
                    var captionX = captionFirst ? leftX + captionWidth * 0.5f : rightX - captionWidth * 0.5f;
                    var contentX = captionFirst ? rightX - content.Width * 0.5f : leftX + content.Width * 0.5f;
                    return new CounterBlockArrangement(width, height, contentX, 0f, captionX, 0f);
                }

                default:
                    return new CounterBlockArrangement(content.Width, content.Height, 0f, 0f, 0f, 0f);
            }
        }

        public static CounterDetailStack StackDetail(
            float valueWidth,
            float valueHeight,
            int valueLines,
            float detailWidth,
            float detailHeight,
            float valueEm,
            float detailEm,
            float stackSpacing,
            TextAlign align)
        {
            var valueLine = valueHeight / Math.Max(1, valueLines);
            var valueFirst = (valueHeight - valueLine) * 0.5f;
            var detailY = -valueFirst - (stackSpacing * (valueEm + detailEm) * 0.5f);
            var top = valueHeight * 0.5f;
            var bottom = Math.Min(-valueHeight * 0.5f, detailY - (detailHeight * 0.5f));
            var middle = (top + bottom) * 0.5f;
            var width = Math.Max(valueWidth, detailWidth);
            var box = new CounterContentBox(width, top - bottom, valueFirst - middle, detailY - middle);
            return new CounterDetailStack(box, AlignX(valueWidth, width, align), -middle, AlignX(detailWidth, width, align), detailY - middle);
        }

        public static float AlignX(float width, float blockWidth, TextAlign align)
        {
            switch (align)
            {
                case TextAlign.Left:
                    return (width - blockWidth) * 0.5f;
                case TextAlign.Right:
                    return (blockWidth - width) * 0.5f;
                default:
                    return 0f;
            }
        }
    }
}
