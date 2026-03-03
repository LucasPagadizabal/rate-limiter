using RateLimiter.Core.Algorithms;

namespace RateLimiter.Core.Tests.Algorithms;

public class SlidingWindowLogLimiterTests
{
    private readonly FakeTimeProvider _clock = new();

    /// <summary>
    /// Helper: crea un limiter con valores por defecto razonables.
    /// max = cantidad máxima de requests, windowSeconds = duración de la ventana.
    /// </summary>
    private SlidingWindowLogLimiter CreateLimiter(int max = 5, int windowSeconds = 10) =>
        new(max, TimeSpan.FromSeconds(windowSeconds), _clock);

    /// <summary>
    /// Caso básico: si el límite es 3, los primeros 3 requests deben pasar.
    /// Cada request se anota en el log con su timestamp.
    /// </summary>
    [Fact]
    public void AllowsUpToMaxRequests()
    {
        var limiter = CreateLimiter(max: 3);

        Assert.True(limiter.Acquire("c1").IsAllowed);
        Assert.True(limiter.Acquire("c1").IsAllowed);
        Assert.True(limiter.Acquire("c1").IsAllowed);
    }

    /// <summary>
    /// Cuando el log tiene la cantidad máxima de entradas dentro de la ventana,
    /// el siguiente request se rechaza. El log está "lleno".
    /// </summary>
    [Fact]
    public void RejectsAfterMaxRequests()
    {
        var limiter = CreateLimiter(max: 2);

        limiter.Acquire("c1");
        limiter.Acquire("c1");

        var result = limiter.Acquire("c1");
        Assert.False(result.IsAllowed);
        Assert.NotNull(result.RetryAfter);
    }

    /// <summary>
    /// Cada request que pasa reduce el Remaining en 1.
    /// Con límite 3: primer request → quedan 2, segundo → queda 1, tercero → quedan 0.
    /// </summary>
    [Fact]
    public void RemainingCountDecrements()
    {
        var limiter = CreateLimiter(max: 3);

        Assert.Equal(2, limiter.Acquire("c1").Remaining);
        Assert.Equal(1, limiter.Acquire("c1").Remaining);
        Assert.Equal(0, limiter.Acquire("c1").Remaining);
    }

    /// <summary>
    /// Los requests viejos "expiran" cuando salen de la ventana de tiempo.
    /// Ejemplo: ventana de 10 segundos, límite de 2.
    ///   - t=0: request 1 (pasa)
    ///   - t=3: request 2 (pasa, log lleno)
    ///   - t=3: request 3 (rechazado, ya hay 2 en la ventana)
    ///   - t=11: request 4 (pasa! el request de t=0 ya expiró de la ventana)
    /// </summary>
    [Fact]
    public void OldRequestsExpireAndFreeCapacity()
    {
        var limiter = CreateLimiter(max: 2, windowSeconds: 10);

        limiter.Acquire("c1"); // t=0
        _clock.Advance(TimeSpan.FromSeconds(3));
        limiter.Acquire("c1"); // t=3

        Assert.False(limiter.Acquire("c1").IsAllowed); // lleno en t=3

        // Avanzamos a t=11: el request de t=0 ya quedó fuera de la ventana [1, 11].
        _clock.Advance(TimeSpan.FromSeconds(8));

        var result = limiter.Acquire("c1");
        Assert.True(result.IsAllowed); // se liberó un lugar
    }

    /// <summary>
    /// Este test demuestra la ventaja principal del Sliding Window sobre Fixed Window.
    /// Con Fixed Window, un cliente podría enviar 5 requests al final del minuto 1
    /// y 5 al inicio del minuto 2, logrando 10 en ~1 segundo.
    ///
    /// Con Sliding Window eso NO pasa: la ventana se mueve con el tiempo,
    /// así que los 5 requests siguen "dentro" de la ventana aunque crucemos un borde.
    /// </summary>
    [Fact]
    public void SlidingNature_NoBoundarySpike()
    {
        var limiter = CreateLimiter(max: 5, windowSeconds: 10);

        // Enviamos 5 requests cerca del "final" conceptual.
        _clock.Advance(TimeSpan.FromSeconds(8));
        for (int i = 0; i < 5; i++)
            limiter.Acquire("c1");

        // 2 segundos después (cruzamos un "borde"), debería seguir limitado
        // porque los 5 requests aún están dentro de la ventana deslizante de 10s.
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(limiter.Acquire("c1").IsAllowed);
    }

    /// <summary>
    /// El RetryAfter indica exactamente cuándo va a expirar la entrada más vieja.
    /// Ejemplo: ventana de 10s, límite 2.
    ///   - t=0: request 1
    ///   - t=3: request 2 (log lleno)
    ///   - t=3: request 3 rechazado
    ///
    /// La entrada más vieja (t=0) expira en t=10. Estamos en t=3.
    /// Entonces RetryAfter = 10 - 3 = 7 segundos.
    /// </summary>
    [Fact]
    public void RetryAfter_PointsToOldestEntryExpiry()
    {
        var limiter = CreateLimiter(max: 2, windowSeconds: 10);

        limiter.Acquire("c1"); // t=0
        _clock.Advance(TimeSpan.FromSeconds(3));
        limiter.Acquire("c1"); // t=3

        var result = limiter.Acquire("c1"); // rechazado en t=3
        Assert.False(result.IsAllowed);
        Assert.Equal(TimeSpan.FromSeconds(7), result.RetryAfter);
    }

    /// <summary>
    /// Cada cliente tiene su propio log independiente.
    /// Que el cliente "a" esté limitado no afecta al cliente "b".
    /// </summary>
    [Fact]
    public void ClientsAreIsolated()
    {
        var limiter = CreateLimiter(max: 1);

        limiter.Acquire("a");
        Assert.False(limiter.Acquire("a").IsAllowed);
        Assert.True(limiter.Acquire("b").IsAllowed);
    }

    /// <summary>
    /// Validación del constructor: no permite valores inválidos.
    /// Máximo de 0 requests o ventana de 0 segundos no tienen sentido lógico.
    /// </summary>
    [Fact]
    public void Constructor_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SlidingWindowLogLimiter(0, TimeSpan.FromSeconds(1)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SlidingWindowLogLimiter(10, TimeSpan.Zero));
    }

    /// <summary>
    /// Test de concurrencia: 100 threads intentan pasar al mismo tiempo con límite 50.
    /// Exactamente 50 deben ser permitidos.
    /// Verifica que el lock por cliente previene race conditions.
    /// </summary>
    [Fact]
    public void ConcurrentAccess_DoesNotExceedLimit()
    {
        var limiter = CreateLimiter(max: 50);
        int allowed = 0;

        Parallel.For(0, 100, _ =>
        {
            if (limiter.Acquire("c1").IsAllowed)
                Interlocked.Increment(ref allowed);
        });

        Assert.Equal(50, allowed);
    }
}
