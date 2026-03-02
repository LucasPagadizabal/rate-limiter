using System.Collections.Concurrent;

namespace RateLimiter.Core.Algorithms;

/// <summary>
/// Fixed Window Counter rate limiter.
///
/// Divides time into discrete windows of fixed duration and counts requests per window.
/// Simple and memory-efficient (one counter per client), but has a known boundary problem:
/// a client can send up to 2× the limit across a window boundary.
///
/// Good for: scenarios where simplicity matters and approximate limiting is acceptable.
/// </summary>
public sealed class FixedWindowCounterLimiter : IRateLimiter
{
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, WindowCounter> _counters = new();

    public FixedWindowCounterLimiter(int maxRequests, TimeSpan window, TimeProvider? clock = null)
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
        var counter = _counters.GetOrAdd(clientKey, _ => new WindowCounter());

        lock (counter)
        {
            var now = _clock.GetUtcNow();
            long currentWindow = GetWindowId(now);

            if (counter.WindowId != currentWindow)
            {
                counter.WindowId = currentWindow;
                counter.Count = 0;
            }

            if (counter.Count < _maxRequests)
            {
                counter.Count++;
                return RateLimitResult.Allowed(_maxRequests, _maxRequests - counter.Count);
            }

            var windowEnd = GetWindowEnd(currentWindow);
            return RateLimitResult.Rejected(_maxRequests, windowEnd - now);
        }
    }

    /// <summary>Maps a point in time to a window identifier (window index since epoch).</summary>
    private long GetWindowId(DateTimeOffset time) =>
        time.ToUnixTimeMilliseconds() / (long)_window.TotalMilliseconds;

    /// <summary>Returns the end time of the given window.</summary>
    private DateTimeOffset GetWindowEnd(long windowId)
    {
        long endMs = (windowId + 1) * (long)_window.TotalMilliseconds;
        return DateTimeOffset.FromUnixTimeMilliseconds(endMs);
    }

    /// <summary>Mutable state for a single client's counter. Guarded by lock.</summary>
    private sealed class WindowCounter
    {
        public long WindowId;
        public int Count;
    }
}
