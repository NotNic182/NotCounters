using NotCounters.Core.Config;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal readonly struct HandLook
    {
        private readonly bool _letters;
        private readonly string? _leftColor;
        private readonly string? _rightColor;

        private HandLook(bool letters, string? leftColor, string? rightColor)
        {
            _letters = letters;
            _leftColor = leftColor;
            _rightColor = rightColor;
        }

        public bool Colored => _leftColor != null;

        public static HandLook For(StatsSnapshot stats, CounterSettings settings)
        {
            var letters = CounterOptions.GetBool(settings, CounterOptionKeys.HandLetters);
            ColorValue? left = null;
            ColorValue? right = null;
            switch (CounterOptions.GetChoice(settings, CounterOptionKeys.HandColors))
            {
                case CounterChoices.HandColorsSabers:
                    left = stats.Left.SaberColor;
                    right = stats.Right.SaberColor;
                    break;
                case CounterChoices.HandColorsCustom:
                    left = CounterOptions.GetColor(settings, CounterOptionKeys.LeftColor);
                    right = CounterOptions.GetColor(settings, CounterOptionKeys.RightColor);
                    break;
            }

            if (!left.HasValue || !right.HasValue)
            {
                return new HandLook(letters, null, null);
            }

            return new HandLook(letters, left.Value.ToHex(), right.Value.ToHex());
        }

        public string Join(string left, string right, bool separateLines) =>
            Hand("L ", left, _leftColor) + (separateLines ? "\n" : " | ") + Hand("R ", right, _rightColor);

        private string Hand(string letter, string value, string? color)
        {
            var text = _letters ? letter + value : value;
            return color == null ? text : "<color=" + color + ">" + text + "</color>";
        }
    }
}
