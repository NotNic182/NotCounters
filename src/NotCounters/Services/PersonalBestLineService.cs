using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Core.Leaderboards;
using NotCounters.Core.Stats;
using NotCounters.Util;
using SiraUtil.Logging;
using SiraUtil.Services;
using Zenject;

namespace NotCounters.Services
{
    internal sealed class PersonalBestLineService : IPersonalBestLineService, IInitializable, IDisposable
    {
        private readonly IConfigService _config;
        private readonly LeaderboardRequestScheduler _scheduler;
        private readonly SiraLog _log;
        private readonly ILevelFinisher? _levelFinisher;

        private readonly Dictionary<long, AccuracyLine?> _beatLeaderLines = new Dictionary<long, AccuracyLine?>();
        private readonly Dictionary<long, List<Action<AccuracyLine?>>> _inFlight = new Dictionary<long, List<Action<AccuracyLine?>>>();

        private string? _recordingKey;
        private AccuracyLineRecorder? _recorder;
        private bool _disposed;

        public PersonalBestLineService(IConfigService config, LeaderboardRequestScheduler scheduler, SiraLog log, [InjectOptional] ILevelFinisher? levelFinisher)
        {
            _config = config;
            _scheduler = scheduler;
            _log = log;
            _levelFinisher = levelFinisher;
        }

        public void Initialize()
        {
            if (_levelFinisher != null)
            {
                _levelFinisher.StandardLevelFinished += OnStandardLevelFinished;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_levelFinisher != null)
            {
                _levelFinisher.StandardLevelFinished -= OnStandardLevelFinished;
            }

            _inFlight.Clear();
            _beatLeaderLines.Clear();
            _recorder = null;
        }

        public IDisposable RequestBeatLeader(long scoreId, Action<AccuracyLine?> onResult) =>
            Safe.Invoke("PbLines.Request", () => RequestBeatLeaderCore(scoreId, onResult), ListenerSubscription.None);

        public SavedAccuracyLine? LoadSaved(string mapKey) =>
            Safe.Invoke<SavedAccuracyLine?>("PbLines.Load", () =>
            {
                string path = PathFor(mapKey);
                return File.Exists(path) ? SavedAccuracyLine.FromJson(File.ReadAllText(path)) : null;
            }, null);

        public void StartRecording(string mapKey, AccuracyLineRecorder recorder)
        {
            _recordingKey = mapKey;
            _recorder = recorder;
        }

        internal static string FileName(string mapKey)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = (mapKey ?? string.Empty).ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == ' ' || Array.IndexOf(invalid, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }

            return chars.Length > 0 ? new string(chars) : "_";
        }

        private IDisposable RequestBeatLeaderCore(long scoreId, Action<AccuracyLine?>? onResult)
        {
            if (onResult == null || _disposed)
            {
                return ListenerSubscription.None;
            }

            if (_beatLeaderLines.TryGetValue(scoreId, out AccuracyLine? cached))
            {
                Deliver(onResult, cached);
                return ListenerSubscription.None;
            }

            if (_inFlight.TryGetValue(scoreId, out List<Action<AccuracyLine?>>? waiting))
            {
                waiting.Add(onResult);
                return new ListenerSubscription<AccuracyLine?>(waiting, onResult);
            }

            GlobalSettings settings = _config.Config.Global;
            string? url = settings.EnableBeatLeader ? new BeatLeaderApi(settings.BeatLeaderApiBaseUrl).ScoreStatisticUrl(scoreId) : null;
            if (url == null)
            {
                Deliver(onResult, null);
                return ListenerSubscription.None;
            }

            var listeners = new List<Action<AccuracyLine?>> { onResult };
            _inFlight[scoreId] = listeners;
            _scheduler.Enqueue(LeaderboardService.BeatLeader, url, response => OnStatistic(scoreId, listeners, response));
            return new ListenerSubscription<AccuracyLine?>(listeners, onResult);
        }

        private void OnStatistic(long scoreId, List<Action<AccuracyLine?>> listeners, LeaderboardResponse response)
        {
            if (_disposed)
            {
                return;
            }

            if (_inFlight.TryGetValue(scoreId, out List<Action<AccuracyLine?>>? current) && ReferenceEquals(current, listeners))
            {
                _inFlight.Remove(scoreId);
            }

            AccuracyLine? line = null;
            if (response.Outcome == LeaderboardRequestOutcome.Success)
            {
                line = Safe.Invoke<AccuracyLine?>("PbLines.Parse", () => AccuracyLineParser.FromBeatLeaderStatistic(response.Http.Body), null);
                _beatLeaderLines[scoreId] = line;
            }
            else if (response.Outcome == LeaderboardRequestOutcome.NotFound)
            {
                _beatLeaderLines[scoreId] = null;
            }

            _log.Debug(line != null
                ? $"BeatLeader accuracy line for score {scoreId}: {line.Count} points, ends at {line.Final.ToString("P2", CultureInfo.InvariantCulture)}."
                : $"No BeatLeader accuracy line for score {scoreId} ({response.Outcome}).");

            Action<AccuracyLine?>[] copy = listeners.ToArray();
            listeners.Clear();
            foreach (Action<AccuracyLine?> listener in copy)
            {
                Deliver(listener, line);
            }
        }

        private void OnStandardLevelFinished(LevelCompletionResults results) =>
            Safe.Invoke("PbLines.LevelFinished", () =>
            {
                string? key = _recordingKey;
                AccuracyLineRecorder? recorder = _recorder;
                _recordingKey = null;
                _recorder = null;
                if (_disposed || key == null || recorder == null || results == null
                    || results.levelEndStateType != LevelCompletionResults.LevelEndStateType.Cleared)
                {
                    return;
                }

                AccuracyLine? line = recorder.Finish(results.multipliedScore);
                if (line == null)
                {
                    return;
                }

                var played = new SavedAccuracyLine(line, recorder.LastScore);
                if (!played.IsBetterThan(LoadSaved(key)))
                {
                    return;
                }

                string path = PathFor(key);
                Directory.CreateDirectory(StoragePaths.AccuracyLinesDirectory);
                string temp = path + ".tmp";
                File.WriteAllText(temp, played.ToJson());
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temp, path);
                _log.Debug($"Saved the accuracy line for {key}: {line.Final.ToString("P2", CultureInfo.InvariantCulture)}, {line.Count} points.");
            });

        private static string PathFor(string mapKey) => Path.Combine(StoragePaths.AccuracyLinesDirectory, FileName(mapKey) + ".json");

        private static void Deliver(Action<AccuracyLine?> listener, AccuracyLine? line) =>
            Safe.Invoke("PbLines.Callback", () => listener(line));
    }
}
