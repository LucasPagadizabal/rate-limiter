using RateLimiter.Core.Algorithms;

namespace RateLimiter.Core.Tests.Algorithms;

public class TokenBucketLimiterTests
{
    private readonly FakeTimeProvider _clock = new();

    /// <summary>
    /// Helper: crea un limiter con valores por defecto razonables para tests.
    /// Usamos el FakeTimeProvider para controlar el tiempo manualmente.
    /// </summary>
    private TokenBucketLimiter CreateLimiter(int maxTokens = 5, int windowSeconds = 10) =>
        new(maxTokens, TimeSpan.FromSeconds(windowSeconds), _clock);

    /// <summary>
    /// Caso más básico: el primer request de un cliente siempre debe pasar.
    /// El balde arranca lleno, así que tiene tokens disponibles.
    /// Verificamos también que el Limit y Remaining sean coherentes.
    /// </summary>
    [Fact]
    public void FirstRequest_IsAlwaysAllowed()
    {
        var limiter = CreateLimiter();
        var result = limiter.Acquire("client-1");

        Assert.True(result.IsAllowed);
        Assert.Equal(5, result.Limit);
        Assert.Equal(4, result.Remaining); // 5 tokens - 1 consumido = 4
    }

    /// <summary>
    /// Si el balde tiene capacidad 3, los primeros 3 requests deben pasar.
    /// Cada uno consume un token del balde.
    /// </summary>
    [Fact]
    public void AllowsUpToBucketCapacity()
    {
        var limiter = CreateLimiter(maxTokens: 3);

        Assert.True(limiter.Acquire("c1").IsAllowed);   // quedan 2
        Assert.True(limiter.Acquire("c1").IsAllowed);   // quedan 1
        Assert.True(limiter.Acquire("c1").IsAllowed);   // quedan 0
    }

    /// <summary>
    /// Cuando el balde se vacía, el siguiente request debe ser rechazado.
    /// Verificamos que IsAllowed sea false, Remaining sea 0,
    /// y que RetryAfter tenga un valor (le dice al cliente cuánto esperar).
    /// </summary>
    [Fact]
    public void RejectsWhenBucketIsEmpty()
    {
        var limiter = CreateLimiter(maxTokens: 2);

        limiter.Acquire("c1"); // quedan 1
        limiter.Acquire("c1"); // quedan 0

        var result = limiter.Acquire("c1");

        Assert.False(result.IsAllowed);
        Assert.Equal(0, result.Remaining);
        Assert.NotNull(result.RetryAfter);
    }

    /// <summary>
    /// Los tokens se recargan con el tiempo. Acá vaciamos el balde (10 tokens),
    /// avanzamos el reloj 5 segundos (la mitad de la ventana de 10s),
    /// y verificamos que se hayan recargado tokens suficientes para permitir un request.
    /// Esto prueba el mecanismo de "lazy refill": no hay timers, se recalcula al pedir.
    /// </summary>
    [Fact]
    public void TokensRefillOverTime()
    {
        var limiter = CreateLimiter(maxTokens: 10, windowSeconds: 10);

        // Vaciamos el balde completamente.
        for (int i = 0; i < 10; i++)
            limiter.Acquire("c1");

        Assert.False(limiter.Acquire("c1").IsAllowed);

        // Avanzamos la mitad de la ventana → se recargan ~5 tokens.
        _clock.Advance(TimeSpan.FromSeconds(5));

        var result = limiter.Acquire("c1");
        Assert.True(result.IsAllowed);
    }

    /// <summary>
    /// Aunque pase muchísimo tiempo, el balde nunca supera su capacidad máxima.
    /// Esperamos 10 minutos con un balde de 5 → sigue teniendo 5, no 600.
    /// Esto evita que un cliente acumule tokens infinitos y haga un mega-burst.
    /// </summary>
    [Fact]
    public void TokensDoNotExceedCapacity()
    {
        var limiter = CreateLimiter(maxTokens: 5, windowSeconds: 10);

        // Esperamos mucho más que una ventana completa.
        _clock.Advance(TimeSpan.FromMinutes(10));

        var result = limiter.Acquire("c1");
        Assert.True(result.IsAllowed);
        Assert.Equal(4, result.Remaining); // 5 máx - 1 consumido = 4
    }

    /// <summary>
    /// Cada cliente tiene su propio balde independiente.
    /// Si client-a agotó su balde, client-b no se ve afectado.
    /// Esto es clave: un usuario no puede perjudicar a otro.
    /// </summary>
    [Fact]
    public void ClientsAreIsolated()
    {
        var limiter = CreateLimiter(maxTokens: 1);

        limiter.Acquire("client-a");
        Assert.False(limiter.Acquire("client-a").IsAllowed);

        // Otro cliente distinto debe poder pasar sin problema.
        Assert.True(limiter.Acquire("client-b").IsAllowed);
    }

    /// <summary>
    /// Verificamos que el RetryAfter sea correcto.
    /// Con 10 tokens en 10 segundos → se recarga 1 token por segundo.
    /// Si el balde está vacío, el cliente debe esperar 1 segundo para el próximo token.
    /// </summary>
    [Fact]
    public void RetryAfterIsOneTokenInterval()
    {
        var limiter = CreateLimiter(maxTokens: 10, windowSeconds: 10);

        for (int i = 0; i < 10; i++)
            limiter.Acquire("c1");

        var result = limiter.Acquire("c1");
        Assert.False(result.IsAllowed);
        Assert.Equal(TimeSpan.FromSeconds(1), result.RetryAfter);
    }

    /// <summary>
    /// El constructor no debe aceptar valores inválidos.
    /// Capacidad 0 o negativa no tiene sentido, ventana de tiempo 0 tampoco.
    /// Es mejor fallar rápido con una excepción clara que tener bugs silenciosos.
    /// </summary>
    [Fact]
    public void Constructor_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TokenBucketLimiter(0, TimeSpan.FromSeconds(1)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TokenBucketLimiter(10, TimeSpan.Zero));
    }

    /// <summary>
    /// Test de concurrencia: lanzamos 200 requests en paralelo desde múltiples threads.
    /// Con un balde de 100, exactamente 100 deben ser permitidos y 100 rechazados.
    /// Si el lock per-client no funcionara bien, pasarían más de 100 (race condition).
    /// Usamos Interlocked.Increment para contar de forma thread-safe.
    /// </summary>
    [Fact]
    public void ConcurrentAccess_DoesNotExceedLimit()
    {
        var limiter = CreateLimiter(maxTokens: 100);
        int allowed = 0;

        Parallel.For(0, 200, _ =>
        {
            if (limiter.Acquire("c1").IsAllowed)
                Interlocked.Increment(ref allowed);
        });

        Assert.Equal(100, allowed);
    }
}
