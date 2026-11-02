using System;

namespace NotCounters.Core.Counters
{
    public sealed class MeasureCache<TSize>
        where TSize : struct
    {
        private bool _stored;
        private string? _text;
        private int _styleVersion;
        private TSize _size;

        public bool TryGet(string? text, int styleVersion, out TSize size)
        {
            if (_stored && styleVersion == _styleVersion && string.Equals(text, _text, StringComparison.Ordinal))
            {
                size = _size;
                return true;
            }

            size = default;
            return false;
        }

        public void Store(string? text, int styleVersion, TSize size)
        {
            _stored = true;
            _text = text;
            _styleVersion = styleVersion;
            _size = size;
        }
    }
}
