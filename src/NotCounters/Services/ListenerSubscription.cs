using System;
using System.Collections.Generic;

namespace NotCounters.Services
{
    internal sealed class ListenerSubscription<T> : IDisposable
    {
        private List<Action<T>>? _listeners;
        private Action<T>? _listener;

        public ListenerSubscription(List<Action<T>> listeners, Action<T> listener)
        {
            _listeners = listeners;
            _listener = listener;
        }

        public void Dispose()
        {
            List<Action<T>>? listeners = _listeners;
            Action<T>? listener = _listener;

            _listeners = null;
            _listener = null;
            if (listeners != null && listener != null)
            {
                listeners.Remove(listener);
            }
        }
    }

    internal static class ListenerSubscription
    {
        public static readonly IDisposable None = new EmptySubscription();

        private sealed class EmptySubscription : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
