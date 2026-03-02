namespace RateLimiter.Core;

/// <summary>
/// Evaluates whether a request identified by <paramref name="clientKey"/> should be allowed.
/// Implementations must be thread-safe.
/// </summary>
public interface IRateLimiter
{
    RateLimitResult Acquire(string clientKey);
}
