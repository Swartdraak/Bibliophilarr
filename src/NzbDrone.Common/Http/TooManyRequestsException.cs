using System;

namespace NzbDrone.Common.Http
{
    public class TooManyRequestsException : HttpException
    {
        public TimeSpan RetryAfter { get; private set; }

        public TooManyRequestsException(HttpRequest request, HttpResponse response)
            : base(request, response)
        {
            RetryAfter = ParseRetryAfter(response.Headers["Retry-After"]);
        }

        public TooManyRequestsException(string retryAfter)
            : base(null, null, "HTTP request failed: [429:TooManyRequests]")
        {
            RetryAfter = ParseRetryAfter(retryAfter);
        }

        private static TimeSpan ParseRetryAfter(string retryAfter)
        {
            if (int.TryParse(retryAfter, out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }

            if (DateTime.TryParse(retryAfter, out var date))
            {
                return date.ToUniversalTime() - DateTime.UtcNow;
            }

            return TimeSpan.Zero;
        }

        internal TooManyRequestsException(HttpRequest request, HttpResponse response, TimeSpan retryAfter)
            : base(request, response)
        {
            RetryAfter = retryAfter;
        }
    }
}
