using System.Collections.Concurrent;

namespace RateLimiter.Core.Algorithms;

/// <summary>
/// Token Bucket rate limiter.
///
/// Each client gets a bucket that holds up to <c>maxTokens</c> tokens.
/// Tokens are replenished at a steady rate: one token every <c>refillInterval / maxTokens</c>.
/// A request consumes one token. If the bucket is empty, the request is rejected.
///
/// This algorithm allows short bursts (up to the bucket capacity) while enforcing
/// a long-term average rate.
/// </summary>
public sealed class TokenBucketLimiter : IRateLimiter
{
    private readonly int _maxTokens;
    private readonly TimeSpan _refillInterval;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();

    public TokenBucketLimiter(int maxTokens, TimeSpan refillInterval, TimeProvider? clock = null)
    {
        if (maxTokens <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "Must be positive.");
        if (refillInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(refillInterval), "Must be positive.");

        _maxTokens = maxTokens;
        _refillInterval = refillInterval;
        _clock = clock ?? TimeProvider.System;
    }

    public RateLimitResult Acquire(string clientKey)
    {
        var bucket = _buckets.GetOrAdd(clientKey, _ => new Bucket(_maxTokens, _clock.GetUtcNow()));

        lock (bucket)
        {
            Refill(bucket);

            if (bucket.Tokens >= 1)
            {
                bucket.Tokens -= 1;
                return RateLimitResult.Allowed(_maxTokens, (int)bucket.Tokens);
            }

            var timePerToken = _refillInterval / _maxTokens;
            return RateLimitResult.Rejected(_maxTokens, timePerToken);
        }
    }

    private void Refill(Bucket bucket)
    {
        var now = _clock.GetUtcNow();
        var elapsed = now - bucket.LastRefill;

        if (elapsed <= TimeSpan.Zero)
            return;

        // Tokens to add: proportional to elapsed time within the refill interval.
        var tokensToAdd = elapsed / _refillInterval * _maxTokens;
        bucket.Tokens = Math.Min(_maxTokens, bucket.Tokens + tokensToAdd);
        bucket.LastRefill = now;
    }

    /// <summary>Mutable state for a single client's bucket. Guarded by lock.</summary>
    private sealed class Bucket(double tokens, DateTimeOffset lastRefill)
    {
        public double Tokens = tokens;
        public DateTimeOffset LastRefill = lastRefill;
    }
}
