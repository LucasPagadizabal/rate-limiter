using RateLimiter.Core.Algorithms;
using RateLimiter.Core.Configuration;

namespace RateLimiter.Core;

public static class RateLimiterFactory
{
    public static IRateLimiter Create(RateLimiterOptions options, TimeProvider? clock = null)
    {
        return options.Algorithm switch
        {
            Algorithm.TokenBucket =>
                new TokenBucketLimiter(options.MaxRequests, options.Window, clock),
            Algorithm.SlidingWindowLog =>
                new SlidingWindowLogLimiter(options.MaxRequests, options.Window, clock),
            Algorithm.FixedWindowCounter =>
                new FixedWindowCounterLimiter(options.MaxRequests, options.Window, clock),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Algorithm))
        };
    }
}
