using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NotCounters.Core.Config;

namespace NotCounters.Core.Counters.Presenters
{
    internal static class OptionBuilder
    {
        public static CounterOptionDescriptor Bool(string key, string displayName, bool defaultValue, string help) =>
            new CounterOptionDescriptor(key, displayName, CounterOptionKind.Bool, new JValue(defaultValue)) { HelpText = help };

        public static CounterOptionDescriptor Int(string key, string displayName, int defaultValue, int min, int max, int step, string help) =>
            new CounterOptionDescriptor(key, displayName, CounterOptionKind.Int, new JValue((long)defaultValue))
            {
                HelpText = help,
                Min = min,
                Max = max,
                Step = step,
            };

        public static CounterOptionDescriptor Text(string key, string displayName, string defaultValue, string help) =>
            new CounterOptionDescriptor(key, displayName, CounterOptionKind.Text, new JValue(defaultValue)) { HelpText = help };

        public static CounterOptionDescriptor Choice(string key, string displayName, string defaultValue, string help, params KeyValuePair<string, string>[] choices) =>
            new CounterOptionDescriptor(key, displayName, CounterOptionKind.Choice, new JValue(defaultValue))
            {
                HelpText = help,
                Choices = choices,
            };

        public static CounterOptionDescriptor Color(string key, string displayName, ColorValue defaultValue, string help) =>
            new CounterOptionDescriptor(key, displayName, CounterOptionKind.Color, new JValue(defaultValue.ToHex())) { HelpText = help };

        public static KeyValuePair<string, string> Item(string key, string label) => new KeyValuePair<string, string>(key, label);

        public static CounterOptionDescriptor Grouping(string defaultValue) =>
            Choice(
                CounterOptionKeys.Grouping,
                "Digit grouping",
                defaultValue,
                "How thousands are separated in scores.",
                Item(CounterChoices.GroupingNone, "None (1234567)"),
                Item(CounterChoices.GroupingSpace, "Vanilla (1 234 567)"),
                Item(CounterChoices.GroupingComma, "Comma (1,234,567)"),
                Item(CounterChoices.GroupingDot, "Dot (1.234.567)"));

        public static CounterOptionDescriptor PerHand(string help) =>
            Bool(CounterOptionKeys.PerHand, "Per hand", false, help);

        public static CounterOptionDescriptor SeparateLines() =>
            Bool(CounterOptionKeys.SeparateLines, "Hands on separate lines", false, "With \"Per hand\" on, show the left and right values on two lines instead of \"L x | R y\".");

        public static CounterOptionDescriptor HandLetters() =>
            Bool(CounterOptionKeys.HandLetters, "Show L and R", true, "With \"Per hand\" on, put L and R in front of the left and right values.");

        public static CounterOptionDescriptor HandColors() =>
            Choice(
                CounterOptionKeys.HandColors,
                "Hand colors",
                CounterChoices.HandColorsOff,
                "Color the left and right values. Sabers: like your sabers in that map. Custom: the two colors below.",
                Item(CounterChoices.HandColorsOff, "Off"),
                Item(CounterChoices.HandColorsSabers, "Sabers"),
                Item(CounterChoices.HandColorsCustom, "Custom"));

        public static CounterOptionDescriptor LeftColor() =>
            Color(CounterOptionKeys.LeftColor, "Left color", CounterColors.DefaultLeftSaber, "Color of the left values when \"Hand colors\" is Custom.");

        public static CounterOptionDescriptor RightColor() =>
            Color(CounterOptionKeys.RightColor, "Right color", CounterColors.DefaultRightSaber, "Color of the right values when \"Hand colors\" is Custom.");

        public static CounterOptionDescriptor ShowUnit(string unit) =>
            Bool(CounterOptionKeys.ShowUnit, "Show unit", true, "Append \"" + unit + "\" to the value.");
    }
}
