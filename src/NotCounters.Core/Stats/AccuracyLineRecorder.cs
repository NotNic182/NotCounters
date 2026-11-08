using System;
using System.Collections.Generic;

namespace NotCounters.Core.Stats
{
    public sealed class AccuracyLineRecorder
    {
        private const int MaxSeconds = 3 * 60 * 60;

        private readonly List<double> _values = new List<double>();

        public double? LastAccuracy { get; private set; }

        public int LastScore { get; private set; }

        public bool Spoiled { get; set; }

        public int MaxScore { get; set; }

        public void Sample(double songTime, double? accuracy, int score)
        {
            if (double.IsNaN(songTime) || double.IsInfinity(songTime))
            {
                return;
            }

            if (accuracy.HasValue && !double.IsNaN(accuracy.Value) && !double.IsInfinity(accuracy.Value))
            {
                LastAccuracy = Math.Max(0d, Math.Min(1d, accuracy.Value));
                LastScore = score;
            }

            int second = (int)Math.Floor(Math.Min(songTime, MaxSeconds));
            while (_values.Count <= second)
            {
                _values.Add(LastAccuracy ?? 1d);
            }
        }

        public AccuracyLine? Finish(int? finalScore = null)
        {
            if (Spoiled)
            {
                return null;
            }

            if (finalScore.HasValue && finalScore.Value >= 0 && MaxScore > 0)
            {
                LastAccuracy = Math.Min(1d, finalScore.Value / (double)MaxScore);
                LastScore = finalScore.Value;
            }

            if (!LastAccuracy.HasValue)
            {
                return null;
            }

            var values = new List<double>(_values.Count + 1);
            values.AddRange(_values);
            values.Add(LastAccuracy.Value);
            return AccuracyLine.From(values);
        }
    }
}
