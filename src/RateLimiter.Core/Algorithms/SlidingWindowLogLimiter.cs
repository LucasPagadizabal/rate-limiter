using System.Collections.Concurrent;

namespace RateLimiter.Core.Algorithms;

/// <summary>
/// Sliding Window Log rate limiter.
///
/// Keeps a log of timestamps for each request within the current window.
/// The window slides continuously with real time, so there are no boundary issues
/// like with fixed-window counters.
///
/// Trade-off: most accurate rate limiting, but uses more memory (one timestamp per request).
/// </summary>
public sealed class SlidingWindowLogLimiter : IRateLimiter
{
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, ClientLog> _logs = new();

    public SlidingWindowLogLimiter(int maxRequests, TimeSpan window, TimeProvider? clock = null)
    {
        if (maxRequests <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRequests), "Must be positive.");
        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window), "Must be positive.");

        _maxRequests = maxRequests;
        _window = window;
        _clock = clock ?? TimeProvider.System;
    }

    public RateLimitResult Acquire(string clientKey)
    {
        var log = _logs.GetOrAdd(clientKey, _ => new ClientLog());

        lock (log)
        {
            var now = _clock.GetUtcNow();
            var windowStart = now - _window;

            // Evict expired entries from the front of the list.
            while (log.Timestamps.Count > 0 && log.Timestamps.First!.Value <= windowStart)
                log.Timestamps.RemoveFirst();

            if (log.Timestamps.Count < _maxRequests)
            {
                log.Timestamps.AddLast(now);
                int remaining = _maxRequests - log.Timestamps.Count;
                return RateLimitResult.Allowed(_maxRequests, remaining);
            }

            // Rejected: retry after the oldest entry expires from the window.
            var oldestInWindow = log.Timestamps.First!.Value;
            var retryAfter = oldestInWindow + _window - now;
            return RateLimitResult.Rejected(_maxRequests, retryAfter);
        }
    }

    /// <summary>Per-client timestamp log. Guarded by lock.</summary>
    private sealed class ClientLog
    {
        public readonly LinkedList<DateTimeOffset> Timestamps = new();
    }
}
