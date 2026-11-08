using System;
using System.Collections.Generic;

namespace NotCounters.Core.Stats
{
    // one accuracy value per song second, value i is the accuracy so far at second i
    public sealed class AccuracyLine
    {
        private const double MatchTolerance = 0.000001;

        private readonly double[] _values;

        private AccuracyLine(double[] values, double shift)
        {
            _values = values;
            Shift = shift;
        }

        public int Count => _values.Length;

        public double Shift { get; }

        public double Final => _values[_values.Length - 1];

        public IReadOnlyList<double> Values => _values;

        public static AccuracyLine? From(IEnumerable<double>? perSecond, double shift = 0d)
        {
            if (perSecond == null)
            {
                return null;
            }

            var values = new List<double>();
            foreach (double value in perSecond)
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    return null;
                }

                values.Add(value < 0d ? 0d : value > 1d ? 1d : value);
            }

            bool shiftOk = !double.IsNaN(shift) && !double.IsInfinity(shift);
            return values.Count >= 2 ? new AccuracyLine(values.ToArray(), shiftOk ? shift : 0d) : null;
        }

        public double? At(double songTime)
        {
            if (double.IsNaN(songTime) || double.IsInfinity(songTime))
            {
                return null;
            }

            double position = songTime - Shift;
            if (position <= 0d)
            {
                return _values[0];
            }

            int index = (int)Math.Floor(position);
            if (index >= _values.Length - 1)
            {
                return Final;
            }

            double from = _values[index];
            return from + (_values[index + 1] - from) * (position - index);
        }

        public bool EndsAt(double accuracy) =>
            !double.IsNaN(accuracy) && Math.Abs(Final - accuracy) <= MatchTolerance;
    }
}
