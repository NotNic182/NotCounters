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
            ColorValue? left = stats.Left.SaberColor;
            ColorValue? right = stats.Right.SaberColor;
            if (!left.HasValue || !right.HasValue || !CounterOptions.GetBool(settings, CounterOptionKeys.SaberColors))
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
