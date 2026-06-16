using System.Net;

namespace Lodestone.Infrastructure.Net;

/// <summary>
/// Retries transient HTTP failures (5xx, 408, 429) with exponential backoff + jitter, honouring a
/// <c>Retry-After</c> header when present (Decorator over the handler pipeline). Keeps the app resilient
/// to flaky networks and Modrinth rate-limits without pulling in a resilience framework.
/// </summary>
public sealed class RetryDelegatingHandler : DelegatingHandler
{
    private readonly int _maxRetries;

    public RetryDelegatingHandler(int maxRetries = 3) => _maxRetries = maxRetries;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                response?.Dispose();
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (!IsTransient(response.StatusCode) || attempt >= _maxRetries)
                {
                    return response;
                }
            }
            catch (HttpRequestException) when (attempt < _maxRetries)
            {
                // fall through to backoff and retry
            }

            TimeSpan delay = RetryAfter(response) ?? Backoff(attempt);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static TimeSpan? RetryAfter(HttpResponseMessage? response)
    {
        if (response?.Headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta is { } delta)
            {
                return delta;
            }

            if (retryAfter.Date is { } date)
            {
                TimeSpan until = date - DateTimeOffset.UtcNow;
                return until > TimeSpan.Zero ? until : TimeSpan.Zero;
            }
        }

        return null;
    }

    private static TimeSpan Backoff(int attempt)
    {
        double seconds = Math.Pow(2, attempt);
        double jitter = Random.Shared.NextDouble() * 0.5;
        return TimeSpan.FromSeconds(seconds + jitter);
    }
}
