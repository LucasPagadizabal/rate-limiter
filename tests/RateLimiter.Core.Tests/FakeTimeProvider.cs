namespace RateLimiter.Core.Tests;

/// <summary>
/// Time provider controlable para tests determinísticos.
/// Permite avanzar el tiempo sin delays reales.
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;

    public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
}
