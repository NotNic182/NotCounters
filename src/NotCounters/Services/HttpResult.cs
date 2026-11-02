using System;
using System.Collections.Generic;

namespace NotCounters.Services
{
    internal sealed class HttpResult
    {
        public HttpResult(int status, string? body, IEnumerable<KeyValuePair<string, string>>? headers, string? error)
        {
            Status = status;
            Body = body;
            Error = error;

            var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers)
                {
                    if (header.Key != null)
                    {
                        copy[header.Key] = header.Value ?? string.Empty;
                    }
                }
            }

            Headers = copy;
        }

        public int Status { get; }

        public string? Body { get; }

        public IReadOnlyDictionary<string, string> Headers { get; }

        public string? Error { get; }

        public bool IsSuccess => Status >= 200 && Status < 300;

        public static HttpResult NetworkFailure(string error) => new HttpResult(0, null, null, error);
    }

    internal interface IHttpRequest
    {
        bool IsDone { get; }

        void Cancel();
    }

    internal interface IHttpClient
    {
        IHttpRequest Get(string url, Action<HttpResult> onComplete, TimeSpan? timeout = null);
    }
}
