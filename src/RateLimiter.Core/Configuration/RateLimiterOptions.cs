namespace RateLimiter.Core.Configuration;

public enum Algorithm
{
    TokenBucket,
    SlidingWindowLog,
    FixedWindowCounter
}

public sealed class RateLimiterOptions
{
    /// <summary>Maximum number of requests (or bucket capacity for token bucket).</summary>
    public int MaxRequests { get; set; } = 100;

    /// <summary>Time window for the rate limit.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Which algorithm to use.</summary>
    public Algorithm Algorithm { get; set; } = Algorithm.TokenBucket;

    /// <summary>HTTP status code returned when rate limited. Defaults to 429.</summary>
    public int StatusCode { get; set; } = 429;
}
