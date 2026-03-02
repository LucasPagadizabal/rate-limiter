namespace RateLimiter.Core;

/// <summary>
/// Represents the outcome of a rate limit check.
/// Immutable value type to avoid allocations on the hot path.
/// </summary>
public readonly record struct RateLimitResult(
    bool IsAllowed,
    int Limit,
    int Remaining,
    TimeSpan? RetryAfter)
{
    public static RateLimitResult Allowed(int limit, int remaining) =>
        new(true, limit, remaining, null);

    public static RateLimitResult Rejected(int limit, TimeSpan retryAfter) =>
        new(false, limit, 0, retryAfter);
}
