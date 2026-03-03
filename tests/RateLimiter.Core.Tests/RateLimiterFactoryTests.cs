using RateLimiter.Core.Algorithms;
using RateLimiter.Core.Configuration;

namespace RateLimiter.Core.Tests;

public class RateLimiterFactoryTests
{
    /// <summary>
    /// Verifica que el factory cree la implementación correcta según el algoritmo configurado.
    /// Usamos [Theory] con [InlineData] para testear los 3 algoritmos sin repetir código.
    /// </summary>
    [Theory]
    [InlineData(Algorithm.TokenBucket, typeof(TokenBucketLimiter))]
    [InlineData(Algorithm.SlidingWindowLog, typeof(SlidingWindowLogLimiter))]
    [InlineData(Algorithm.FixedWindowCounter, typeof(FixedWindowCounterLimiter))]
    public void Create_ReturnsCorrectImplementation(Algorithm algorithm, Type expectedType)
    {
        var options = new RateLimiterOptions
        {
            Algorithm = algorithm,
            MaxRequests = 10,
            Window = TimeSpan.FromSeconds(5)
        };

        var limiter = RateLimiterFactory.Create(options);

        Assert.IsType(expectedType, limiter);
    }

    /// <summary>
    /// Si alguien pasa un valor de enum que no existe, debe fallar con una excepción clara.
    /// Esto protege contra errores de configuración silenciosos.
    /// </summary>
    [Fact]
    public void Create_ThrowsForUnknownAlgorithm()
    {
        var options = new RateLimiterOptions { Algorithm = (Algorithm)999 };
        Assert.Throws<ArgumentOutOfRangeException>(() => RateLimiterFactory.Create(options));
    }
}
