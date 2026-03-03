using RateLimiter.Core.Algorithms;

namespace RateLimiter.Core.Tests.Algorithms;

public class FixedWindowCounterLimiterTests
{
    private readonly FakeTimeProvider _clock = new();

    /// <summary>
    /// Helper: crea un limiter con valores por defecto razonables.
    /// max = requests permitidos por ventana, windowSeconds = duración de cada ventana.
    /// </summary>
    private FixedWindowCounterLimiter CreateLimiter(int max = 5, int windowSeconds = 10) =>
        new(max, TimeSpan.FromSeconds(windowSeconds), _clock);

    /// <summary>
    /// Caso básico: si el límite es 3 por ventana, los primeros 3 requests pasan.
    /// Simplemente incrementa un contador.
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
    /// Cuando el contador llega al límite dentro de la misma ventana, rechaza.
    /// </summary>
    [Fact]
    public void RejectsAfterMaxRequests()
    {
        var limiter = CreateLimiter(max: 2);

        limiter.Acquire("c1");
        limiter.Acquire("c1");

        var result = limiter.Acquire("c1");
        Assert.False(result.IsAllowed);
    }

    /// <summary>
    /// Al pasar a una nueva ventana de tiempo, el contador se resetea a 0.
    /// Ejemplo: ventana de 10s, límite 2.
    ///   - Ventana 1: 2 requests → lleno → rechaza
    ///   - Avanzamos 10s (nueva ventana)
    ///   - Ventana 2: el contador arranca de nuevo → permite
    /// </summary>
    [Fact]
    public void CounterResetsOnNewWindow()
    {
        var limiter = CreateLimiter(max: 2, windowSeconds: 10);

        limiter.Acquire("c1");
        limiter.Acquire("c1");
        Assert.False(limiter.Acquire("c1").IsAllowed);

        // Avanzamos a la siguiente ventana.
        _clock.Advance(TimeSpan.FromSeconds(10));

        Assert.True(limiter.Acquire("c1").IsAllowed);
    }

    /// <summary>
    /// El RetryAfter indica cuánto falta para que termine la ventana actual.
    /// Debe ser un valor positivo y menor o igual a la duración de la ventana.
    /// </summary>
    [Fact]
    public void RetryAfter_PointsToWindowEnd()
    {
        var limiter = CreateLimiter(max: 1, windowSeconds: 10);

        limiter.Acquire("c1"); // permitido
        var result = limiter.Acquire("c1"); // rechazado

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.RetryAfter);
        Assert.True(result.RetryAfter!.Value.TotalSeconds > 0);
        Assert.True(result.RetryAfter!.Value.TotalSeconds <= 10);
    }

    /// <summary>
    /// ⚠️ Este test DOCUMENTA el problema conocido del Fixed Window Counter.
    ///
    /// El "boundary spike": un cliente puede enviar el doble del límite
    /// si manda requests justo al final de una ventana y al inicio de la siguiente.
    ///
    /// Ejemplo con límite 5 y ventana de 10s:
    ///   - t=9s: envía 5 requests (fin de ventana 1) → todos pasan
    ///   - t=10s: envía 5 requests (inicio de ventana 2) → todos pasan
    ///   - Resultado: 10 requests en 1 segundo con límite de 5.
    ///
    /// Esto es una limitación aceptada del algoritmo (no un bug).
    /// Por eso existe Sliding Window como alternativa más precisa.
    /// </summary>
    [Fact]
    public void BoundarySpike_IsExpectedBehavior()
    {
        var limiter = CreateLimiter(max: 5, windowSeconds: 10);

        // Nos posicionamos cerca del final de la ventana actual.
        _clock.Advance(TimeSpan.FromSeconds(9));

        // Enviamos 5 requests al final de esta ventana.
        for (int i = 0; i < 5; i++)
            Assert.True(limiter.Acquire("c1").IsAllowed);

        // Cruzamos a la siguiente ventana (apenas 1 segundo después).
        _clock.Advance(TimeSpan.FromSeconds(1));

        // Podemos enviar 5 más inmediatamente — eso son 10 en ~1 segundo.
        for (int i = 0; i < 5; i++)
            Assert.True(limiter.Acquire("c1").IsAllowed);
    }

    /// <summary>
    /// Cada cliente tiene su propio contador independiente.
    /// Que "a" esté limitado no bloquea a "b".
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
    /// El Remaining baja de forma predecible con cada request.
    /// Límite 3: primero quedan 2, luego 1, luego 0.
    /// </summary>
    [Fact]
    public void RemainingCountIsAccurate()
    {
        var limiter = CreateLimiter(max: 3);

        Assert.Equal(2, limiter.Acquire("c1").Remaining);
        Assert.Equal(1, limiter.Acquire("c1").Remaining);
        Assert.Equal(0, limiter.Acquire("c1").Remaining);
    }

    /// <summary>
    /// Validación del constructor: valores sin sentido lanzan excepción.
    /// Previene errores silenciosos por mala configuración.
    /// </summary>
    [Fact]
    public void Constructor_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FixedWindowCounterLimiter(0, TimeSpan.FromSeconds(1)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FixedWindowCounterLimiter(10, TimeSpan.Zero));
    }

    /// <summary>
    /// Test de concurrencia: 200 threads, límite 100.
    /// Exactamente 100 deben pasar, ni uno más.
    /// Si el lock fallara, el contador podría corromperse y dejar pasar de más.
    /// </summary>
    [Fact]
    public void ConcurrentAccess_DoesNotExceedLimit()
    {
        var limiter = CreateLimiter(max: 100);
        int allowed = 0;

        Parallel.For(0, 200, _ =>
        {
            if (limiter.Acquire("c1").IsAllowed)
                Interlocked.Increment(ref allowed);
        });

        Assert.Equal(100, allowed);
    }
}
