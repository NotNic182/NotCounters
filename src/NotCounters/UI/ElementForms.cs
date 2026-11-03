using System;
using System.Collections.Generic;
using System.Globalization;
using NotCounters.Core.Config;
using NotCounters.Core.Counters;
using NotCounters.UI.Forms;
using Newtonsoft.Json.Linq;

namespace NotCounters.UI
{
    internal enum AdjustStep
    {
        Fine,
        Medium,
        Coarse,
    }

    internal sealed class ElementFormContext
    {
        private readonly Func<AdjustStep> _getStep;
        private readonly Action<AdjustStep> _setStep;
        private readonly Action<CounterSettings, CounterAttachment>? _reattach;
        private readonly Action? _resetPlacement;
        private readonly Func<bool>? _canResetPlacement;
        private readonly Action<VanillaElementSettings, bool>? _setMoveWithHud;

        public ElementFormContext(
            Func<AdjustStep> getStep,
            Action<AdjustStep> setStep,
            Action<CounterSettings, CounterAttachment>? reattach = null,
            Action? resetPlacement = null,
            Func<bool>? canResetPlacement = null,
            Action<VanillaElementSettings, bool>? setMoveWithHud = null)
        {
            _getStep = getStep;
            _setStep = setStep;
            _reattach = reattach;
            _resetPlacement = resetPlacement;
            _canResetPlacement = canResetPlacement;
            _setMoveWithHud = setMoveWithHud;
        }

        public AdjustStep Step
        {
            get => _getStep();
            set => _setStep(value);
        }

        public bool CanResetPlacement => _resetPlacement != null && (_canResetPlacement == null || _canResetPlacement());

        public void ResetPlacement()
        {
            if (CanResetPlacement)
            {
                _resetPlacement!();
            }
        }

        public void SetAttachment(CounterSettings counter, CounterAttachment attachment)
        {
            if (counter.Attachment == attachment)
            {
                return;
            }

            if (_reattach != null)
            {
                _reattach(counter, attachment);
            }
            else
            {
                counter.Attachment = attachment;
            }
        }

        public void SetMoveWithHud(VanillaElementSettings element, bool moveWithHud)
        {
            if (element.MoveWithHud == moveWithHud)
            {
                return;
            }

            if (_setMoveWithHud != null)
            {
                _setMoveWithHud(element, moveWithHud);
            }
            else
            {
                element.MoveWithHud = moveWithHud;
            }
        }
    }

    internal static class ElementForms
    {
        internal const string DegreeSign = "°";

        private const double RotationLimit = 360;

        private const double UnboundedLimit = 1_000_000;

        private const int MaxSliderSteps = 100;

        private const string VanillaDecimalsKey = "vanilla";

        private static readonly IReadOnlyList<ChoiceItem> StepChoices = new[]
        {
            new ChoiceItem(nameof(AdjustStep.Fine), "Fine (0.01 m, 1" + DegreeSign + ")"),
            new ChoiceItem(nameof(AdjustStep.Medium), "Medium (0.05 m, 5" + DegreeSign + ")"),
            new ChoiceItem(nameof(AdjustStep.Coarse), "Coarse (0.1 m, 15" + DegreeSign + ")"),
        };

        private static readonly IReadOnlyList<ChoiceItem> VanillaDecimalsChoices = BuildVanillaDecimalsChoices();

        private static readonly IReadOnlyList<ChoiceItem> NumberGroupingChoices = new[]
        {
            new ChoiceItem(nameof(NumberGrouping.None), "Vanilla (1 234 567)"),
            new ChoiceItem(nameof(NumberGrouping.Comma), "Comma (1,234,567)"),
            new ChoiceItem(nameof(NumberGrouping.Dot), "Dot (1.234.567)"),
        };

        private static readonly IReadOnlyList<ChoiceItem> TimeModeChoices = new[]
        {
            new ChoiceItem(nameof(TimeDisplayMode.Default), "Vanilla (elapsed / total)"),
            new ChoiceItem(nameof(TimeDisplayMode.Elapsed), "Elapsed"),
            new ChoiceItem(nameof(TimeDisplayMode.Remaining), "Remaining"),
        };

        private static readonly IReadOnlyList<ChoiceItem> LabelPlacementChoices = new[]
        {
            new ChoiceItem(nameof(LabelPlacement.Above), "Above value"),
            new ChoiceItem(nameof(LabelPlacement.Below), "Below value"),
            new ChoiceItem(nameof(LabelPlacement.Left), "Left of value"),
            new ChoiceItem(nameof(LabelPlacement.Right), "Right of value"),
            new ChoiceItem(nameof(LabelPlacement.Inline), "Inline (Label: value)"),
        };

        private static readonly IReadOnlyList<ChoiceItem> AlignmentChoices = new[]
        {
            new ChoiceItem(nameof(TextAlign.Left), "Left"),
            new ChoiceItem(nameof(TextAlign.Center), "Center"),
            new ChoiceItem(nameof(TextAlign.Right), "Right"),
        };

        private static readonly IReadOnlyList<ChoiceItem> AttachmentChoices = new[]
        {
            new ChoiceItem(nameof(CounterAttachment.Hud), "On"),
            new ChoiceItem(nameof(CounterAttachment.None), "Off (stays put)"),
        };

        private static readonly IReadOnlyList<ChoiceItem> MoveWithHudChoices = new[]
        {
            new ChoiceItem(bool.TrueString, "On"),
            new ChoiceItem(bool.FalseString, "Off (stays put)"),
        };

        public static double PositionStep(AdjustStep step) => step switch
        {
            AdjustStep.Fine => 0.01,
            AdjustStep.Medium => 0.05,
            _ => 0.1,
        };

        public static double RotationStep(AdjustStep step) => step switch
        {
            AdjustStep.Fine => 1,
            AdjustStep.Medium => 5,
            _ => 15,
        };

        public static double ScaleStep(AdjustStep step) => step switch
        {
            AdjustStep.Fine => 0.01,
            AdjustStep.Medium => 0.05,
            _ => 0.1,
        };

        public static List<FormField> ForVanilla(ElementFormContext ctx, VanillaElementSettings s, VanillaElementDescriptor? d)
        {
            var fields = new List<FormField>();

            if (d == null || d.SupportsPose)
            {
                var follow = s.MoveWithHud;
                fields.Add(new SectionField(follow ? "Placement (offset from vanilla)" : "Placement"));
                fields.Add(StepField(ctx));
                fields.Add(new ChoiceField(
                    "Move with the HUD",
                    MoveWithHudChoices,
                    () => s.MoveWithHud.ToString(),
                    key => ctx.SetMoveWithHud(s, key == bool.TrueString))
                {
                    HoverHint = "On: moves with the game's HUD in each environment, like vanilla. Off: stays in the same place in every environment.",
                });
                fields.AddRange(follow
                    ? PositionFields("Offset", () => s.PositionOffset, v => s.PositionOffset = v, ctx)
                    : PositionFields("Position", () => s.PositionOffset, v => s.PositionOffset = v, ctx, "From the center of your play area, at floor level."));
                fields.AddRange(RotationFields(() => s.RotationOffset, v => s.RotationOffset = v, ctx));
                fields.Add(ScaleField(() => s.Scale, v => s.Scale = v, ctx));
                fields.Add(ResetPlacementField(
                    "Reset placement",
                    "Back to where the game puts it: Move with the HUD on, no offset or rotation, scale 1.00x.",
                    ctx));
            }

            if (d == null)
            {
                return fields;
            }

            var style = new List<FormField>();
            if (d.SupportsPrimaryColor)
            {
                style.AddRange(OptionalColorFields(
                    d.PrimaryColorLabel,
                    () => s.PrimaryColor,
                    v => s.PrimaryColor = v));
            }

            if (d.SupportsSecondaryColor)
            {
                style.AddRange(OptionalColorFields(
                    d.SecondaryColorLabel,
                    () => s.SecondaryColor,
                    v => s.SecondaryColor = v));
            }

            if (d.SupportsFontSize)
            {
                style.Add(new NumberField("Font size", ConfigValidator.MinFontSizeMultiplier, ConfigValidator.MaxFontSizeMultiplier, 0.05, () => s.FontSizeMultiplier, v => s.FontSizeMultiplier = Round(v))
                {
                    Style = NumberStyle.Slider,
                    Format = v => v.ToString("0.00", CultureInfo.InvariantCulture) + "x",
                    HoverHint = "Multiplier on the vanilla text size.",
                });
            }

            if (d.SupportsLabelText)
            {
                style.AddRange(CaptionFields(() => s.LabelText, v => s.LabelText = v));
            }

            if (d.SupportsDecimals)
            {
                style.Add(new ChoiceField(
                    "Decimals",
                    VanillaDecimalsChoices,
                    () => s.Decimals.HasValue ? Clamp(s.Decimals.Value, ConfigValidator.MinDecimals, ConfigValidator.MaxDecimals).ToString(CultureInfo.InvariantCulture) : VanillaDecimalsKey,
                    key =>
                    {
                        s.Decimals = int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var decimals) ? decimals : (int?)null;
                    }));
            }

            if (d.SupportsNumberGrouping)
            {
                style.Add(EnumChoice("Digit grouping", NumberGroupingChoices, () => s.NumberGrouping, v => s.NumberGrouping = v));
            }

            if (d.SupportsTimeMode)
            {
                style.Add(EnumChoice("Time display", TimeModeChoices, () => s.TimeMode, v => s.TimeMode = v));
            }

            if (d.SupportsHideFullComboLines)
            {
                style.Add(new ToggleField("Hide full-combo lines", () => s.HideFullComboLines, v => s.HideFullComboLines = v)
                {
                    HoverHint = "The lines above and below the combo. The game removes them when your full combo breaks.",
                });
            }

            if (d.SupportsOutline)
            {
                style.AddRange(OutlineFields(
                    () => s.OutlineWidth,
                    v => s.OutlineWidth = v,
                    () => s.OutlineColor,
                    v => s.OutlineColor = v));
            }

            if (style.Count > 0)
            {
                fields.Add(new SectionField("Style"));
                fields.AddRange(style);
            }

            return fields;
        }

        public static List<FormField> ForCounter(ElementFormContext ctx, CounterSettings s, CounterTypeDescriptor? d)
        {
            var typeName = LayoutEditing.TypeDisplayName(s.Type);
            var fields = new List<FormField>
            {
                new SectionField("Counter"),
                new TextField("Name", () => s.Name, v => s.Name = NullIfBlank(v), typeName)
                {
                    HoverHint = d == null
                        ? "Shown only in the editor; empty uses the counter type."
                        : d.UsesRowLogos
                            ? "Shown only in the editor; empty uses the counter type. For a title in play, see \"Title\"."
                            : "Shown only in the editor; empty uses the counter type. For the label in play, see \"Label text\".",
                },
                new SectionField("Placement"),
                StepField(ctx),
                EnumChoice<CounterAttachment>(
                    "Move with the HUD",
                    AttachmentChoices,
                    () => s.Attachment,
                    v => ctx.SetAttachment(s, v),
                    hoverHint: "On: follows the game's HUD in each environment, clear of a raised floor. Position is its place in The First."),
            };

            fields.AddRange(PositionFields("Position", () => s.Position, v => s.Position = v, ctx, "From the center of your play area, at floor level."));
            fields.AddRange(RotationFields(() => s.Rotation, v => s.Rotation = v, ctx));
            fields.Add(ScaleField(() => s.Scale, v => s.Scale = v, ctx));
            fields.Add(ResetPlacementField(
                "Reset rotation and scale",
                "Rotation 0 and scale 1.00x; the counter keeps its position.",
                ctx));

            if (d == null)
            {
                fields.Add(new NoteField(
                    "This counter type is not supported by this version of NotCounters. It stays in your config but is not shown on the HUD.",
                    12f));
                return fields;
            }

            fields.Add(new SectionField("Text"));
            fields.AddRange(TextStyleFields(s, d));

            if (d.Options.Count > 0)
            {
                fields.Add(new SectionField(d.DisplayName + " options"));
                foreach (var option in d.Options)
                {
                    var field = OptionField(s, option);
                    if (field != null)
                    {
                        fields.Add(field);
                    }
                }
            }

            return fields;
        }

        private static IEnumerable<FormField> TextStyleFields(CounterSettings s, CounterTypeDescriptor d)
        {
            TextStyle Style() => s.Style ??= new TextStyle();
            bool LabelShown() => Style().ShowLabel;
            var logos = d.UsesRowLogos;

            yield return new NumberField("Font size", ConfigValidator.MinFontSize, ConfigValidator.MaxFontSize, 0.5, () => Style().FontSize, v => Style().FontSize = Round(v))
            {
                Format = v => v.ToString("0.0", CultureInfo.InvariantCulture),
                HoverHint = "Size of the value text. 20 matches the vanilla combo number.",
            };
            yield return new ColorField(
                "Value color",
                () => ColorConversion.ToUnity(Style().ValueColor),
                c => Style().ValueColor = ColorConversion.WithAlphaOf(c, Style().ValueColor));
            yield return new ToggleField("Color by value", () => Style().ColorByValue, v => Style().ColorByValue = v)
            {
                HoverHint = "Uses rank colors, green/red differences and low-energy red, where a counter has them.",
            };
            yield return new ToggleField(logos ? "Show logos" : "Show label", () => Style().ShowLabel, v => Style().ShowLabel = v)
            {
                HoverHint = logos ? "The BeatLeader and ScoreSaber logos beside the lines. Off also hides the title." : null,
            };
            yield return new TextField(
                logos ? "Title" : "Label text",
                () => Style().LabelText,
                v => Style().LabelText = NullIfBlank(v),
                logos ? "(none)" : d.DefaultLabel)
            {
                EnabledWhen = LabelShown,
                HoverHint = logos ? "Optional title shown with the lines." : "Empty uses the default label.",
            };
            yield return new ColorField(
                logos ? "Title color" : "Label color",
                () => ColorConversion.ToUnity(Style().LabelColor),
                c => Style().LabelColor = ColorConversion.WithAlphaOf(c, Style().LabelColor))
            {
                EnabledWhen = LabelShown,
            };
            yield return EnumChoice(logos ? "Title position" : "Label position", LabelPlacementChoices, () => Style().LabelPlacement, v => Style().LabelPlacement = v, LabelShown);
            yield return new NumberField(logos ? "Logo size" : "Label size", ConfigValidator.MinLabelScale, ConfigValidator.MaxLabelScale, 0.05, () => Style().LabelScale, v => Style().LabelScale = Round(v))
            {
                Style = NumberStyle.Slider,
                EnabledWhen = LabelShown,
                Format = v => v.ToString("0.00", CultureInfo.InvariantCulture) + "x",
                HoverHint = logos ? "Size of the logos and the title, relative to the value text." : "Relative to the value text; 0.75x matches the vanilla \"COMBO\" label.",
            };
            yield return EnumChoice("Alignment", AlignmentChoices, () => Style().Alignment, v => Style().Alignment = v);

            if (d.UsesDecimals)
            {
                yield return new NumberField("Decimals", ConfigValidator.MinDecimals, ConfigValidator.MaxDecimals, 1, () => Style().Decimals, v => Style().Decimals = (int)Math.Round(v))
                {
                    IsInteger = true,
                    Style = NumberStyle.Slider,
                };
            }

            yield return new ToggleField("Bold", () => Style().Bold, v => Style().Bold = v);
            yield return new ToggleField("Italic", () => Style().Italic, v => Style().Italic = v);

            foreach (var field in OutlineFields(
                () => Style().OutlineWidth,
                v => Style().OutlineWidth = v,
                () => Style().OutlineColor,
                v => Style().OutlineColor = v))
            {
                yield return field;
            }
        }

        private static FormField? OptionField(CounterSettings s, CounterOptionDescriptor option)
        {
            var key = option.Key;
            var label = string.IsNullOrEmpty(option.DisplayName) ? key : option.DisplayName;

            void Write(JToken value)
            {
                CounterOptions.Set(s, key, value);
            }

            switch (option.Kind)
            {
                case CounterOptionKind.Bool:
                    return new ToggleField(label, () => CounterOptions.GetBool(s, key), v => Write(new JValue(v))) { HoverHint = option.HelpText };

                case CounterOptionKind.Int:
                case CounterOptionKind.Float:
                    return NumericOptionField(option, label, s, Write);

                case CounterOptionKind.Choice:
                    var items = new List<ChoiceItem>(option.Choices.Count);
                    foreach (var choice in option.Choices)
                    {
                        items.Add(new ChoiceItem(choice.Key, string.IsNullOrEmpty(choice.Value) ? choice.Key : choice.Value));
                    }

                    return items.Count == 0
                        ? null
                        : new ChoiceField(label, items, () => CounterOptions.GetChoice(s, key), v => Write(new JValue(v))) { HoverHint = option.HelpText };

                case CounterOptionKind.Text:
                    return new TextField(label, () => CounterOptions.GetString(s, key), v => Write(new JValue(v ?? string.Empty)), "(empty)")
                    {
                        HoverHint = option.HelpText,
                    };

                case CounterOptionKind.Color:
                    return new ColorField(
                        label,
                        () => ColorConversion.ToUnity(CounterOptions.GetColor(s, key)),
                        c => Write(new JValue(ColorConversion.WithAlphaOf(c, CounterOptions.GetColor(s, key)).ToHex())))
                    {
                        HoverHint = option.HelpText,
                    };

                default:
                    return null;
            }
        }

        private static NumberField NumericOptionField(CounterOptionDescriptor option, string label, CounterSettings s, Action<JToken> write)
        {
            var isInteger = option.Kind == CounterOptionKind.Int;
            var min = Math.Max(option.Min, -UnboundedLimit);
            var max = Math.Min(option.Max, UnboundedLimit);
            var step = option.Step > 0 ? option.Step : isInteger ? 1 : 0.1;
            if (isInteger)
            {
                step = Math.Max(1, Math.Round(step));
            }

            var bounded = option.Min > -UnboundedLimit && option.Max < UnboundedLimit && max > min;
            var useSlider = bounded && (max - min) / step <= MaxSliderSteps;

            Func<double> get = isInteger
                ? () => CounterOptions.GetInt(s, option.Key)
                : () => CounterOptions.GetFloat(s, option.Key);
            Action<double> set = isInteger
                ? v => write(new JValue((long)Math.Round(v)))
                : v => write(new JValue(Round(v, NumberField.DecimalsFor(step) + 2)));

            return new NumberField(label, min, max, step, get, set)
            {
                IsInteger = isInteger,
                Style = useSlider ? NumberStyle.Slider : NumberStyle.Increment,
                HoverHint = option.HelpText,
            };
        }

        private static ChoiceField StepField(ElementFormContext ctx) =>
            new ChoiceField(
                "Adjust step",
                StepChoices,
                () => ctx.Step.ToString(),
                key =>
                {
                    if (Enum.TryParse<AdjustStep>(key, out var step))
                    {
                        ctx.Step = step;
                    }
                })
            {
                HoverHint = "How far one press of the position, rotation and scale buttons moves.",
                IsConfigEdit = false,
            };

        private static IEnumerable<FormField> PositionFields(string prefix, Func<Vec3> get, Action<Vec3> set, ElementFormContext ctx, string? hint = null)
        {
            Func<double> step = () => PositionStep(ctx.Step);
            Func<double, string> format = v => v.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " m";

            yield return Axis(prefix + " X (right)", -ConfigValidator.MaxPositionMetres, ConfigValidator.MaxPositionMetres, step, format, () => get().X, v => set(new Vec3(v, get().Y, get().Z)), hint);
            yield return Axis(prefix + " Y (up)", -ConfigValidator.MaxPositionMetres, ConfigValidator.MaxPositionMetres, step, format, () => get().Y, v => set(new Vec3(get().X, v, get().Z)), hint);
            yield return Axis(prefix + " Z (forward)", -ConfigValidator.MaxPositionMetres, ConfigValidator.MaxPositionMetres, step, format, () => get().Z, v => set(new Vec3(get().X, get().Y, v)), hint);
        }

        private static IEnumerable<FormField> RotationFields(Func<Vec3> get, Action<Vec3> set, ElementFormContext ctx)
        {
            Func<double> step = () => RotationStep(ctx.Step);
            Func<double, string> format = v => v.ToString("0.#", CultureInfo.InvariantCulture) + DegreeSign;

            yield return Axis("Tilt (X)", -RotationLimit, RotationLimit, step, format, () => get().X, v => set(new Vec3(v, get().Y, get().Z)));
            yield return Axis("Turn (Y)", -RotationLimit, RotationLimit, step, format, () => get().Y, v => set(new Vec3(get().X, v, get().Z)));
            yield return Axis("Roll (Z)", -RotationLimit, RotationLimit, step, format, () => get().Z, v => set(new Vec3(get().X, get().Y, v)));
        }

        private static NumberField ScaleField(Func<float> get, Action<float> set, ElementFormContext ctx) =>
            new NumberField("Scale", ConfigValidator.MinScale, ConfigValidator.MaxScale, ScaleStep(AdjustStep.Fine), () => get(), v => set(Round(v)))
            {
                StepSource = () => ScaleStep(ctx.Step),
                Format = v => v.ToString("0.00", CultureInfo.InvariantCulture) + "x",
            };

        private static ButtonField ResetPlacementField(string label, string hint, ElementFormContext ctx) =>
            new ButtonField(label, ctx.ResetPlacement)
            {
                EnabledWhen = () => ctx.CanResetPlacement,
                HoverHint = hint,
                IsConfigEdit = false,
            };

        private static NumberField Axis(string label, double min, double max, Func<double> step, Func<double, string> format, Func<float> get, Action<float> set, string? hint = null) =>
            new NumberField(label, min, max, step(), () => get(), v => set(Round(v)))
            {
                StepSource = step,
                Format = format,
                HoverHint = hint,
            };

        private static IEnumerable<FormField> OptionalColorFields(string label, Func<ColorValue?> get, Action<ColorValue?> set)
        {
            var name = string.IsNullOrEmpty(label) ? "Color" : label;

            var lastCustom = get() ?? ColorValue.White;

            yield return new ToggleField("Use vanilla " + LowerFirst(name), () => !get().HasValue, useVanilla =>
            {
                if (useVanilla)
                {
                    lastCustom = get() ?? lastCustom;
                    set(null);
                }
                else
                {
                    set(lastCustom);
                }
            });
            yield return new ColorField(name, () => ColorConversion.ToUnity(get() ?? lastCustom), c =>
            {
                var current = get();
                lastCustom = ColorConversion.WithAlphaOf(c, current ?? lastCustom);

                if (current.HasValue)
                {
                    set(lastCustom);
                }
            })
            {
                EnabledWhen = () => get().HasValue,
            };
        }

        private static IEnumerable<FormField> CaptionFields(Func<string?> get, Action<string?> set)
        {
            var lastShown = VanillaCaption.ShownText(get());
            bool Shown() => VanillaCaption.IsShown(get());

            yield return new ToggleField("Show label", Shown, show =>
            {
                if (show)
                {
                    set(lastShown);
                }
                else
                {
                    lastShown = VanillaCaption.ShownText(get());
                    set(VanillaCaption.Hidden);
                }
            })
            {
                HoverHint = "Off hides the \"COMBO\" label.",
            };
            yield return new TextField("Label text", () => Shown() ? get() : lastShown, v =>
            {
                var text = VanillaCaption.ShownText(v);

                if (Shown())
                {
                    set(text);
                }
                else
                {
                    lastShown = text;
                }
            }, "Vanilla")
            {
                EnabledWhen = Shown,
                HoverHint = "Replaces \"COMBO\"; empty uses the vanilla text.",
            };
        }

        private static IEnumerable<FormField> OutlineFields(Func<float> getWidth, Action<float> setWidth, Func<ColorValue> getColor, Action<ColorValue> setColor)
        {
            yield return new NumberField("Outline width", ConfigValidator.MinOutlineWidth, ConfigValidator.MaxOutlineWidth, 0.05, () => getWidth(), v => setWidth(Round(v)))
            {
                Style = NumberStyle.Slider,
                HoverHint = "0 turns the outline off.",
            };
            yield return new ColorField("Outline color", () => ColorConversion.ToUnity(getColor()), c => setColor(ColorConversion.WithAlphaOf(c, getColor())))
            {
                EnabledWhen = () => getWidth() > 0f,
            };
        }

        private static ChoiceField EnumChoice<TEnum>(
            string label,
            IReadOnlyList<ChoiceItem> items,
            Func<TEnum> get,
            Action<TEnum> set,
            Func<bool>? enabledWhen = null,
            string? hoverHint = null)
            where TEnum : struct
        {
            return new ChoiceField(
                label,
                items,
                () => get().ToString(),
                key =>
                {
                    if (Enum.TryParse<TEnum>(key, out var value))
                    {
                        set(value);
                    }
                })
            {
                EnabledWhen = enabledWhen,
                HoverHint = hoverHint,
            };
        }

        private static IReadOnlyList<ChoiceItem> BuildVanillaDecimalsChoices()
        {
            var items = new List<ChoiceItem> { new ChoiceItem(VanillaDecimalsKey, "Vanilla (95.6 %)") };
            for (var i = ConfigValidator.MinDecimals; i <= ConfigValidator.MaxDecimals; i++)
            {
                var text = i.ToString(CultureInfo.InvariantCulture);
                items.Add(new ChoiceItem(text, text));
            }

            return items;
        }

        private static float Round(double value, int decimals = 4) => (float)Math.Round(value, Math.Max(0, Math.Min(decimals, 15)));

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text!.Trim();

        private static string LowerFirst(string text) =>
            text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text.Substring(1);
    }
}
