namespace NotCounters.Core.Scoring
{
    public sealed class MultiplierState
    {
        public const int MaxMultiplier = 8;

        private const int InitialMaxProgress = 2;

        public MultiplierState()
        {
            Reset();
        }

        public int Multiplier { get; private set; }

        public int Progress { get; private set; }

        public int MaxProgress { get; private set; }

        public float NormalizedProgress => (float)Progress / MaxProgress;

        public void ProcessHit()
        {
            if (Multiplier >= MaxMultiplier) return;
            if (Progress < MaxProgress) Progress++;
            if (Progress >= MaxProgress)
            {
                Multiplier *= 2;
                Progress = 0;
                MaxProgress = Multiplier * 2;
            }
        }

        public void ProcessBreak()
        {
            Progress = 0;
            if (Multiplier > 1)
            {
                Multiplier /= 2;
                MaxProgress = Multiplier * 2;
            }
        }

        public void Reset()
        {
            Multiplier = 1;
            Progress = 0;
            MaxProgress = InitialMaxProgress;
        }

        public MultiplierState Clone()
        {
            var copy = new MultiplierState();
            copy.CopyFrom(this);
            return copy;
        }

        internal void CopyFrom(MultiplierState other)
        {
            Multiplier = other.Multiplier;
            Progress = other.Progress;
            MaxProgress = other.MaxProgress;
        }
    }
}
