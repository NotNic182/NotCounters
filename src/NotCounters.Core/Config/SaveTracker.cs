namespace NotCounters.Core.Config
{
    public sealed class SaveTracker
    {
        public long LastWritten { get; private set; }

        public long LastFailed { get; private set; }

        public bool IsFailing => LastFailed > LastWritten;

        public bool IsWritten(long sequence) => sequence <= LastWritten;

        public void Written(long sequence)
        {
            if (sequence > LastWritten)
            {
                LastWritten = sequence;
            }
        }

        public void Failed(long sequence)
        {
            if (sequence > LastFailed)
            {
                LastFailed = sequence;
            }
        }
    }
}
