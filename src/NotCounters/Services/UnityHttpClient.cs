using System;
using System.Collections.Generic;
using System.Reflection;
using NotCounters.Util;
using IPA.Utilities;
using IPA.Utilities.Async;
using SiraUtil.Logging;
using UnityEngine;
using UnityEngine.Networking;

namespace NotCounters.Services
{
    internal sealed class UnityHttpClient : IHttpClient, IDisposable
    {
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

        private const ulong MaxBodyBytes = 8UL * 1024 * 1024;

        private readonly SiraLog _log;
        private readonly HashSet<RequestHandle> _active = new HashSet<RequestHandle>();
        private bool _disposed;

        public UnityHttpClient(SiraLog log)
        {
            _log = log;
        }

        public static string UserAgent { get; } = "NotCounters/" + ReadModVersion() + " (Beat Saber " + ReadGameVersion() + ")";

        public IHttpRequest Get(string url, Action<HttpResult> onComplete, TimeSpan? timeout = null)
        {
            var handle = new RequestHandle(this, url, onComplete, timeout ?? DefaultTimeout);
            if (_disposed)
            {
                handle.Cancel();
                return handle;
            }

            if (UnityGame.OnMainThread)
            {
                handle.Start();
            }
            else
            {
                // UnityWebRequest has to be created on the main thread
                UnityMainThreadTaskScheduler.Factory.StartNew(() => Safe.Invoke("Http.StartOnMainThread", handle.Start));
            }

            return handle;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (RequestHandle handle in new List<RequestHandle>(_active))
            {
                handle.Cancel();
            }

            _active.Clear();
        }

        private static string ReadModVersion()
        {
            try
            {
                Assembly assembly = typeof(UnityHttpClient).Assembly;
                string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(informational))
                {
                    int plus = informational!.IndexOf('+');
                    return plus > 0 ? informational.Substring(0, plus) : informational;
                }

                Version? version = assembly.GetName().Version;
                return version != null ? version.ToString(3) : "0.0.0";
            }
            catch (Exception)
            {
                return "0.0.0";
            }
        }

        private static string ReadGameVersion()
        {
            try
            {
                string version = UnityGame.GameVersion.ToString();
                return string.IsNullOrWhiteSpace(version) ? "1.44.1" : version;
            }
            catch (Exception)
            {
                return "1.44.1";
            }
        }

        private sealed class RequestHandle : IHttpRequest
        {
            private readonly UnityHttpClient _owner;
            private readonly string _url;
            private readonly TimeSpan _timeout;
            private Action<HttpResult>? _onComplete;
            private UnityWebRequest? _request;
            private double _startedAt;

            public RequestHandle(UnityHttpClient owner, string url, Action<HttpResult> onComplete, TimeSpan timeout)
            {
                _owner = owner;
                _url = url ?? string.Empty;
                _onComplete = onComplete;
                _timeout = timeout;
            }

            public bool IsDone { get; private set; }

            public void Start()
            {
                if (IsDone)
                {
                    return;
                }

                if (_owner._disposed)
                {
                    Cancel();
                    return;
                }

                try
                {
                    if (!Uri.TryCreate(_url, UriKind.Absolute, out Uri? uri)
                        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    {
                        Complete(HttpResult.NetworkFailure("Not an absolute http(s) URL: " + _url));
                        return;
                    }

                    // string overload so the url doesn't get escaped twice
                    _request = UnityWebRequest.Get(_url);
                    _request.timeout = Math.Max(1, (int)Math.Ceiling(_timeout.TotalSeconds));
                    _request.SetRequestHeader("User-Agent", UserAgent);
                    _request.SetRequestHeader("Accept", "application/json");
                    _owner._active.Add(this);
                    _startedAt = MonotonicClock.Now;

                    UnityWebRequestAsyncOperation operation = _request.SendWebRequest();

                    operation.completed += OnOperationCompleted;
                }
                catch (Exception ex)
                {
                    Complete(HttpResult.NetworkFailure("Could not start the request: " + ex.Message));
                }
            }

            public void Cancel()
            {
                if (IsDone)
                {
                    return;
                }

                IsDone = true;
                _onComplete = null;
                try
                {
                    _request?.Abort();
                }
                catch (Exception)
                {
                }

                Release();
            }

            private void OnOperationCompleted(AsyncOperation operation)
            {
                if (IsDone)
                {
                    return;
                }

                HttpResult result = Safe.Invoke("Http.ReadResult", ReadResult,
                    HttpResult.NetworkFailure("Could not read the response."));
                Complete(result);
            }

            private HttpResult ReadResult()
            {
                UnityWebRequest? request = _request;
                if (request == null)
                {
                    return HttpResult.NetworkFailure("The request was released.");
                }

                UnityWebRequest.Result outcome = request.result;
                int status = outcome == UnityWebRequest.Result.ConnectionError ? 0 : (int)request.responseCode;
                string? error = outcome == UnityWebRequest.Result.Success ? null : request.error ?? outcome.ToString();

                string? body = null;
                if (request.downloadedBytes > MaxBodyBytes)
                {
                    error = $"Response too large ({request.downloadedBytes} bytes).";
                }
                else
                {
                    try
                    {
                        body = request.downloadHandler?.text;
                    }
                    catch (Exception ex)
                    {
                        error ??= "Could not decode the response: " + ex.Message;
                    }
                }

                Dictionary<string, string>? headers = null;
                try
                {
                    headers = request.GetResponseHeaders();
                }
                catch (Exception)
                {
                }

                return new HttpResult(status, body, headers, error);
            }

            private void Complete(HttpResult result)
            {
                if (IsDone)
                {
                    return;
                }

                IsDone = true;
                Action<HttpResult>? callback = _onComplete;
                _onComplete = null;
                double elapsedMs = _startedAt > 0 ? (MonotonicClock.Now - _startedAt) * 1000.0 : 0;
                Release();

                _owner._log.Debug($"GET {_url} -> {result.Status}{(result.Error != null ? " (" + result.Error + ")" : string.Empty)} in {elapsedMs:0} ms");
                if (callback != null)
                {
                    Safe.Invoke("Http.Callback", () => callback(result));
                }
            }

            private void Release()
            {
                _owner._active.Remove(this);
                UnityWebRequest? request = _request;
                _request = null;
                try
                {
                    request?.Dispose();
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
