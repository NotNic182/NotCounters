using System;
using System.Collections.Generic;
using NotCounters.Core.Leaderboards;
using NotCounters.Util;
using SiraUtil.Logging;
using Zenject;

namespace NotCounters.Services
{
    internal enum LeaderboardRequestOutcome
    {
        Success,
        NotFound,
        Failed,
        Dropped,
    }

    internal sealed class LeaderboardResponse
    {
        public LeaderboardResponse(LeaderboardRequestOutcome outcome, HttpResult http, int attempts, string reason)
        {
            Outcome = outcome;
            Http = http;
            Attempts = attempts;
            Reason = reason;
        }

        public LeaderboardRequestOutcome Outcome { get; }

        public HttpResult Http { get; }

        public int Attempts { get; }

        public string Reason { get; }
    }

    internal sealed class LeaderboardRequestScheduler : ITickable, IDisposable
    {
        public const double MinimumSpacingSeconds = 1.0;

        public const int MaxWaitingPerService = 24;

        private readonly IHttpClient _http;
        private readonly SiraLog _log;
        private readonly RetryPolicy _policy = RetryPolicy.Default;
        private readonly ServiceQueue[] _queues =
        {
            new ServiceQueue(LeaderboardService.BeatLeader),
            new ServiceQueue(LeaderboardService.ScoreSaber),
        };

        private bool _disposed;

        public LeaderboardRequestScheduler(IHttpClient http, SiraLog log)
        {
            _http = http;
            _log = log;
        }

        public void Enqueue(LeaderboardService service, string url, Action<LeaderboardResponse> onComplete, double delaySeconds = 0)
        {
            if (_disposed || onComplete == null)
            {
                return;
            }

            Safe.Invoke("Leaderboards.Enqueue", () =>
            {
                ServiceQueue queue = QueueFor(service);
                if (queue.Waiting.Count >= MaxWaitingPerService)
                {
                    ScheduledRequest oldest = queue.Waiting[queue.Waiting.Count - 1];
                    queue.Waiting.RemoveAt(queue.Waiting.Count - 1);
                    Finish(oldest, LeaderboardRequestOutcome.Dropped, HttpResult.NetworkFailure("Dropped from a full queue."), "queue full");
                }

                var request = new ScheduledRequest(url, onComplete);
                if (delaySeconds > 0 && !double.IsInfinity(delaySeconds))
                {
                    request.NotBefore = MonotonicClock.Now + delaySeconds;
                }

                queue.Waiting.Insert(0, request);
                Pump(queue);
            });
        }

        public void Tick()
        {
            if (_disposed)
            {
                return;
            }

            double now = MonotonicClock.Now;
            foreach (ServiceQueue queue in _queues)
            {
                if (queue.InFlight == null && now >= queue.NextStartAt && FirstDueIndex(queue.Waiting, now) >= 0)
                {
                    PumpSafely(queue);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (ServiceQueue queue in _queues)
            {
                queue.InFlight?.Http?.Cancel();
                queue.InFlight = null;
                queue.Waiting.Clear();
            }
        }

        private ServiceQueue QueueFor(LeaderboardService service) =>
            service == LeaderboardService.BeatLeader ? _queues[0] : _queues[1];

        private static int FirstDueIndex(List<ScheduledRequest> waiting, double now)
        {
            for (int i = 0; i < waiting.Count; i++)
            {
                if (waiting[i].NotBefore <= now)
                {
                    return i;
                }
            }

            return -1;
        }

        private void PumpSafely(ServiceQueue queue) => Safe.Invoke("Leaderboards.Pump", () => Pump(queue));

        private void Pump(ServiceQueue queue)
        {
            if (_disposed || queue.InFlight != null)
            {
                return;
            }

            double now = MonotonicClock.Now;
            if (now < queue.NextStartAt)
            {
                return;
            }

            int index = FirstDueIndex(queue.Waiting, now);
            if (index < 0)
            {
                return;
            }

            ScheduledRequest request = queue.Waiting[index];
            queue.Waiting.RemoveAt(index);
            queue.InFlight = request;
            queue.NextStartAt = now + MinimumSpacingSeconds;
            request.Attempts++;
            request.Http = _http.Get(request.Url, result => OnHttpCompleted(queue, request, result), _policy.RequestTimeout);
        }

        private void OnHttpCompleted(ServiceQueue queue, ScheduledRequest request, HttpResult result)
        {
            if (ReferenceEquals(queue.InFlight, request))
            {
                queue.InFlight = null;
            }

            if (_disposed)
            {
                return;
            }

            RetryDecision decision = _policy.Decide(queue.Service, request.Attempts,
                result.Status == 0 ? (int?)null : result.Status, result.Headers, DateTimeOffset.UtcNow);

            switch (decision.Action)
            {
                case RetryAction.Success:
                    Finish(request, LeaderboardRequestOutcome.Success, result, decision.Reason);
                    break;
                case RetryAction.CacheNotFound:
                    Finish(request, LeaderboardRequestOutcome.NotFound, result, decision.Reason);
                    break;
                case RetryAction.Retry:
                    double retryAt = MonotonicClock.Now + decision.Delay.TotalSeconds;
                    request.NotBefore = retryAt;
                    if (result.Status == 429)
                    {
                        queue.NextStartAt = Math.Max(queue.NextStartAt, retryAt);
                    }

                    queue.Waiting.Insert(0, request);
                    _log.Debug($"{queue.Service}: {decision} for {request.Url}");
                    break;
                default:
                    _log.Warn($"{queue.Service} request failed ({decision.Reason}): {request.Url}");
                    Finish(request, LeaderboardRequestOutcome.Failed, result, decision.Reason);
                    break;
            }

            Pump(queue);
        }

        private static void Finish(ScheduledRequest request, LeaderboardRequestOutcome outcome, HttpResult result, string reason)
        {
            var response = new LeaderboardResponse(outcome, result, request.Attempts, reason);
            Action<LeaderboardResponse> callback = request.Callback;
            Safe.Invoke("Leaderboards.ResponseCallback", () => callback(response));
        }

        private sealed class ServiceQueue
        {
            public ServiceQueue(LeaderboardService service)
            {
                Service = service;
            }

            public LeaderboardService Service { get; }

            public List<ScheduledRequest> Waiting { get; } = new List<ScheduledRequest>();

            public ScheduledRequest? InFlight { get; set; }

            public double NextStartAt { get; set; }
        }

        private sealed class ScheduledRequest
        {
            public ScheduledRequest(string url, Action<LeaderboardResponse> callback)
            {
                Url = url;
                Callback = callback;
            }

            public string Url { get; }

            public Action<LeaderboardResponse> Callback { get; }

            public int Attempts { get; set; }

            public double NotBefore { get; set; }

            public IHttpRequest? Http { get; set; }
        }
    }
}
