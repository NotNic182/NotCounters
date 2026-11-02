using System;

namespace NotCounters.Core.Editing
{
    public sealed class PreviewClock
    {
        private float _previous = float.NaN;
        private float _published = float.NaN;
        private int _publishedSecond = int.MinValue;

        public bool Update(float songTime)
        {
            if (float.IsNaN(songTime) || float.IsInfinity(songTime))
            {
                return false;
            }

            var stopped = songTime == _previous;
            _previous = songTime;
            return WholeSecond(songTime) != _publishedSecond || (stopped && songTime != _published);
        }

        public void MarkPublished(float songTime)
        {
            _published = songTime;
            _publishedSecond = WholeSecond(songTime);
        }

        private static int WholeSecond(float songTime) =>
            float.IsNaN(songTime) || float.IsInfinity(songTime) ? int.MinValue : (int)Math.Floor(songTime);
    }
}
