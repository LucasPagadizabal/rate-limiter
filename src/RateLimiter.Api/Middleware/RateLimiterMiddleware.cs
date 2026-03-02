using System.Globalization;
using RateLimiter.Core;

namespace RateLimiter.Api.Middleware;

/// <summary>
/// Extracts a client identifier from an HTTP request.
/// Default implementation uses the remote IP address.
/// </summary>
public interface IClientKeyResolver
{
    string Resolve(HttpContext context);
}

public sealed class IpClientKeyResolver : IClientKeyResolver
{
    public string Resolve(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

/// <summary>
/// Middleware that applies rate limiting to incoming HTTP requests.
/// Adds standard rate-limit headers (RateLimit-Limit, RateLimit-Remaining, Retry-After)
/// and returns 429 when the client exceeds the limit.
/// </summary>
public sealed class RateLimiterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRateLimiter _limiter;
    private readonly IClientKeyResolver _keyResolver;
    private readonly int _statusCode;

    public RateLimiterMiddleware(
        RequestDelegate next,
        IRateLimiter limiter,
        IClientKeyResolver keyResolver,
        int statusCode = 429)
    {
        _next = next;
        _limiter = limiter;
        _keyResolver = keyResolver;
        _statusCode = statusCode;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientKey = _keyResolver.Resolve(context);
        var result = _limiter.Acquire(clientKey);

        context.Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString(CultureInfo.InvariantCulture);

        if (result.IsAllowed)
        {
            await _next(context);
            return;
        }

        if (result.RetryAfter.HasValue)
        {
            var seconds = (int)Math.Ceiling(result.RetryAfter.Value.TotalSeconds);
            context.Response.Headers["Retry-After"] = seconds.ToString(CultureInfo.InvariantCulture);
        }

        context.Response.StatusCode = _statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("""{"error":"Rate limit exceeded. Try again later."}""");
    }
}

/// <summary>Extension methods for registering the rate limiter in the ASP.NET Core pipeline.</summary>
public static class RateLimiterMiddlewareExtensions
{
    public static IServiceCollection AddRateLimiter(
        this IServiceCollection services,
        Action<RateLimiter.Core.Configuration.RateLimiterOptions> configure)
    {
        var options = new RateLimiter.Core.Configuration.RateLimiterOptions();
        configure(options);

        var limiter = RateLimiterFactory.Create(options);
        services.AddSingleton<IRateLimiter>(limiter);
        services.AddSingleton<IClientKeyResolver, IpClientKeyResolver>();

        return services;
    }

    public static IApplicationBuilder UseCustomRateLimiter(this IApplicationBuilder app)
    {
        var limiter = app.ApplicationServices.GetRequiredService<IRateLimiter>();
        var keyResolver = app.ApplicationServices.GetRequiredService<IClientKeyResolver>();

        return app.UseMiddleware<RateLimiterMiddleware>(limiter, keyResolver);
    }
}
