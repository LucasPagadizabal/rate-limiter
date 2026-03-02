using RateLimiter.Api.Middleware;
using RateLimiter.Core.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRateLimiter(options =>
{
    options.Algorithm = Algorithm.TokenBucket;
    options.MaxRequests = 10;
    options.Window = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

app.UseCustomRateLimiter();

app.MapGet("/", () => Results.Ok(new { message = "Hello! You're within the rate limit." }));

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
