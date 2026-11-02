using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NotCounters.Core.Leaderboards;
using NotCounters.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SiraUtil.Logging;

namespace NotCounters.Services
{
    internal sealed class LeaderboardDiskCache
    {
        public const int FormatVersion = 1;

        public const int MaxEntries = 400;

        public const int MaxBodyLength = 512 * 1024;

        public const double SaveDelaySeconds = 5.0;

        private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

        private readonly string _path;
        private readonly SiraLog _log;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        private readonly object _fileGate = new object();
        private long _snapshotSequence;
        private long _lastWrittenSequence;

        private Task<Dictionary<string, Entry>>? _loadTask;
        private bool _loaded;
        private bool _dirty;
        private double _saveDueAt;

        public LeaderboardDiskCache(string path, SiraLog log)
        {
            _path = path;
            _log = log;
        }

        public void BeginLoad()
        {
            if (_loadTask == null && !_loaded)
            {
                _loadTask = Task.Run(() => ReadFile(_path, _log));
            }
        }

        public bool TryGet(string key, TimeSpan maxAge, out string body)
        {
            body = string.Empty;
            MergeLoadedFile();
            if (!_entries.TryGetValue(key, out Entry entry))
            {
                return false;
            }

            if (!IsFresh(entry, maxAge, DateTimeOffset.UtcNow))
            {
                return false;
            }

            body = entry.Body;
            return true;
        }

        public void Put(string key, string? body)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(body) || body!.Length > MaxBodyLength)
            {
                return;
            }

            _entries[key] = new Entry(body, DateTimeOffset.UtcNow);
            if (!_dirty)
            {
                _dirty = true;
                _saveDueAt = MonotonicClock.Now + SaveDelaySeconds;
            }
        }

        public void Tick()
        {
            MergeLoadedFile();

            if (!_dirty || _loadTask != null || MonotonicClock.Now < _saveDueAt)
            {
                return;
            }

            BeginWrite();
        }

        private void BeginWrite()
        {
            List<KeyValuePair<string, Entry>> snapshot = PruneAndSnapshot();
            long sequence = ++_snapshotSequence;
            _dirty = false;

            Task.Run(() => Safe.Invoke("Leaderboards.DiskCacheWrite", () => Write(ToJson(snapshot), sequence)));
        }

        public void Flush()
        {
            try
            {
                if (!_dirty)
                {
                    return;
                }

                _loadTask?.Wait(TimeSpan.FromSeconds(2));
                MergeLoadedFile();
                if (_loadTask != null)
                {
                    return;
                }

                List<KeyValuePair<string, Entry>> snapshot = PruneAndSnapshot();
                _dirty = false;
                Write(ToJson(snapshot), ++_snapshotSequence);
            }
            catch (Exception ex)
            {
                Safe.Report("Leaderboards.DiskCacheFlush", ex);
            }
        }

        private void MergeLoadedFile()
        {
            if (_loaded || _loadTask == null || !_loadTask.IsCompleted)
            {
                return;
            }

            _loaded = true;
            Task<Dictionary<string, Entry>> task = _loadTask;
            _loadTask = null;
            if (task.Status != TaskStatus.RanToCompletion)
            {
                return;
            }

            foreach (KeyValuePair<string, Entry> loaded in task.Result)
            {
                if (!_entries.ContainsKey(loaded.Key))
                {
                    _entries[loaded.Key] = loaded.Value;
                }
            }

            _log.Debug($"Leaderboard disk cache: {task.Result.Count} entries loaded from {_path}.");
        }

        private List<KeyValuePair<string, Entry>> PruneAndSnapshot()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            TimeSpan cap = LeaderboardCache<string, string>.PersistedMaxLifetime;
            List<KeyValuePair<string, Entry>> keep = _entries
                .Where(e => IsFresh(e.Value, cap, now))
                .OrderByDescending(e => e.Value.SavedAt)
                .Take(MaxEntries)
                .ToList();

            if (keep.Count != _entries.Count)
            {
                _entries.Clear();
                foreach (KeyValuePair<string, Entry> e in keep)
                {
                    _entries[e.Key] = e.Value;
                }
            }

            return keep;
        }

        private static string ToJson(List<KeyValuePair<string, Entry>> snapshot)
        {
            var entries = new JArray();
            foreach (KeyValuePair<string, Entry> e in snapshot)
            {
                entries.Add(new JObject
                {
                    ["key"] = e.Key,
                    ["savedUtc"] = e.Value.SavedAt.ToString("o", CultureInfo.InvariantCulture),
                    ["body"] = e.Value.Body,
                });
            }

            var root = new JObject
            {
                ["version"] = FormatVersion,
                ["entries"] = entries,
            };
            return root.ToString(Formatting.None);
        }

        private void Write(string json, long sequence)
        {
            lock (_fileGate)
            {
                if (sequence <= _lastWrittenSequence)
                {
                    return;
                }

                _lastWrittenSequence = sequence;
                if (!AtomicFileWriter.TryWriteAllText(_path, json, out string? error))
                {
                    _log.Warn($"Could not write {_path}: {error}");
                }
            }
        }

        private static bool IsFresh(Entry entry, TimeSpan maxAge, DateTimeOffset now)
        {
            if (maxAge <= TimeSpan.Zero || entry.SavedAt > now + FutureTolerance)
            {
                return false;
            }

            return now - entry.SavedAt < maxAge;
        }

        private static Dictionary<string, Entry> ReadFile(string path, SiraLog log)
        {
            var result = new Dictionary<string, Entry>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(path))
                {
                    return result;
                }

                JObject root;
                using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))) { DateParseHandling = DateParseHandling.None })
                {
                    root = JObject.Load(reader);
                }

                if ((int?)root["version"] != FormatVersion || !(root["entries"] is JArray entries))
                {
                    log.Warn($"Ignoring {path}: unknown format.");
                    return result;
                }

                foreach (JToken item in entries)
                {
                    if (item is JObject entry
                        && entry["key"]?.Type == JTokenType.String
                        && entry["body"]?.Type == JTokenType.String
                        && DateTimeOffset.TryParse((string?)entry["savedUtc"], CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset savedAt))
                    {
                        string key = (string)entry["key"]!;
                        string body = (string)entry["body"]!;
                        if (key.Length > 0 && body.Length > 0)
                        {
                            result[key] = new Entry(body, savedAt);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Warn($"Ignoring unreadable {path} ({ex.GetType().Name}: {ex.Message}); it will be rewritten.");
                result.Clear();
            }

            return result;
        }

        private readonly struct Entry
        {
            public Entry(string body, DateTimeOffset savedAt)
            {
                Body = body;
                SavedAt = savedAt;
            }

            public string Body { get; }

            public DateTimeOffset SavedAt { get; }
        }
    }
}
