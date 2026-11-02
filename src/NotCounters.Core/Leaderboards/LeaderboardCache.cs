using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace NotCounters.Core.Leaderboards
{
    public readonly record struct LeaderboardCacheKey(LeaderboardService Service, string Hash, string Characteristic, string DifficultyName)
    {
        public static LeaderboardCacheKey For(LeaderboardService service, LevelIdentity? level) =>
            new LeaderboardCacheKey(service, level?.Hash ?? string.Empty, level?.Characteristic ?? string.Empty,
                level?.DifficultyName ?? string.Empty);
    }

    public sealed class LeaderboardCache<TKey, TValue> where TKey : notnull
    {
        public static readonly TimeSpan SessionLifetime = TimeSpan.MaxValue;

        public static readonly TimeSpan PersistedMaxLifetime = TimeSpan.FromHours(24);

        private readonly Dictionary<TKey, Entry> _entries;
        private readonly Func<DateTimeOffset> _clock;
        private readonly object _gate = new object();

        public LeaderboardCache(TimeSpan defaultTimeToLive, Func<DateTimeOffset>? clock = null, IEqualityComparer<TKey>? comparer = null)
        {
            DefaultTimeToLive = defaultTimeToLive < TimeSpan.Zero ? TimeSpan.Zero : defaultTimeToLive;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _entries = new Dictionary<TKey, Entry>(comparer ?? EqualityComparer<TKey>.Default);
        }

        public TimeSpan DefaultTimeToLive { get; }

        public int Count
        {
            get
            {
                lock (_gate) return _entries.Count;
            }
        }

        public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            value = default!;
            if (key == null) return false;
            DateTimeOffset now = Now();
            lock (_gate)
            {
                if (!_entries.TryGetValue(key, out Entry entry)) return false;
                if (now >= entry.ExpiresAt)
                {
                    _entries.Remove(key);
                    return false;
                }

                value = entry.Value;
                return true;
            }
        }

        public void Set(TKey key, TValue value, TimeSpan? timeToLive = null)
        {
            if (key == null) return;
            TimeSpan ttl = timeToLive ?? DefaultTimeToLive;
            DateTimeOffset now = Now();
            DateTimeOffset expires = ttl <= TimeSpan.Zero ? now : ExpiryAfter(now, ttl);
            lock (_gate) _entries[key] = new Entry(value, expires);
        }

        private static DateTimeOffset ExpiryAfter(DateTimeOffset now, TimeSpan ttl)
        {
            if (DateTimeOffset.MaxValue - now <= ttl) return DateTimeOffset.MaxValue;
            try
            {
                return now + ttl;
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTimeOffset.MaxValue;
            }
        }

        public bool Remove(TKey key)
        {
            if (key == null) return false;
            lock (_gate) return _entries.Remove(key);
        }

        public void Clear()
        {
            lock (_gate) _entries.Clear();
        }

        public int PruneExpired()
        {
            DateTimeOffset now = Now();
            lock (_gate)
            {
                var expired = new List<TKey>();
                foreach (KeyValuePair<TKey, Entry> kv in _entries)
                {
                    if (now >= kv.Value.ExpiresAt) expired.Add(kv.Key);
                }

                foreach (TKey k in expired) _entries.Remove(k);
                return expired.Count;
            }
        }

        private DateTimeOffset Now()
        {
            try
            {
                return _clock();
            }
            catch (Exception)
            {
                return DateTimeOffset.UtcNow;
            }
        }

        private readonly struct Entry
        {
            public Entry(TValue value, DateTimeOffset expiresAt)
            {
                Value = value;
                ExpiresAt = expiresAt;
            }

            public TValue Value { get; }

            public DateTimeOffset ExpiresAt { get; }
        }
    }
}
