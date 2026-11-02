using System;

namespace NotCounters.Core.Editing
{
    public sealed class EditFrameLabel
    {
        public const string CounterKind = "counter";

        public const string HiddenState = "hidden";

        public const string HiddenInPlayState = "hidden in play: Advanced HUD off";

        private string? _name;
        private bool _counter;
        private bool _visible;
        private bool _hiddenInPlay;
        private string? _text;

        public static string Compose(string name, bool counter, bool visible, bool hiddenInPlay)
        {
            var state = !visible ? HiddenState : hiddenInPlay ? HiddenInPlayState : null;
            if (counter)
            {
                state = state == null ? CounterKind : CounterKind + ", " + state;
            }

            return state == null ? name : name + " (" + state + ")";
        }

        public string Text(string name, bool counter, bool visible, bool hiddenInPlay)
        {
            if (_text == null
                || counter != _counter
                || visible != _visible
                || hiddenInPlay != _hiddenInPlay
                || !string.Equals(name, _name, StringComparison.Ordinal))
            {
                _text = Compose(name, counter, visible, hiddenInPlay);
                _name = name;
                _counter = counter;
                _visible = visible;
                _hiddenInPlay = hiddenInPlay;
            }

            return _text;
        }
    }
}
