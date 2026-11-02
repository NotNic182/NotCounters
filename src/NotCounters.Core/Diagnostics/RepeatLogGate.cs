using System;
using System.Collections.Generic;

namespace NotCounters.Core.Diagnostics
{
    public sealed class RepeatLogGate
    {
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);

        public int Count(string key)
        {
            _counts.TryGetValue(key, out var count);
            if (count < int.MaxValue)
            {
                count++;
            }

            _counts[key] = count;
            return count;
        }

        public static bool IsLoggedRepeat(int occurrence) => occurrence >= 2 && (occurrence & (occurrence - 1)) == 0;

        public void Clear() => _counts.Clear();
    }
}
