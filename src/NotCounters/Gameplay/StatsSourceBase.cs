using System;
using NotCounters.Contracts;
using NotCounters.Core.Stats;
using NotCounters.Util;

namespace NotCounters.Gameplay
{
    internal abstract class StatsSourceBase : IStatsSource
    {
        private readonly string _subscriberErrorKey;

        protected StatsSourceBase(string name)
        {
            _subscriberErrorKey = name + ".Updated";
        }

        public StatsSnapshot Current { get; } = new StatsSnapshot();

        public event Action? Updated;

        protected void Publish()
        {
            Current.Version++;
            Action? handlers = Updated;
            if (handlers == null) return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)handler)();
                }
                catch (Exception ex)
                {
                    Safe.Report(_subscriberErrorKey, ex);
                }
            }
        }
    }
}
