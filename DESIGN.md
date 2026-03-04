# Rate Limiter — Documento de Diseño

## Problema

Diseñar e implementar un rate limiter que controle la tasa de tráfico que un cliente puede enviar a una API. Basado en el Capítulo 4 de *System Design Interview* de Alex Xu.

## Arquitectura General

```
┌──────────────┐     ┌─────────────────────┐     ┌──────────────┐
│  HTTP Client │────▶│  RateLimiter         │────▶│  API Handler │
│              │◀────│  Middleware           │     │              │
│              │ 429 │  (ASP.NET Core)      │     │              │
└──────────────┘     └──────┬──────────────┘     └──────────────┘
                            │
                     ┌──────▼──────────────┐
                     │  IRateLimiter        │
                     │  (strategy pattern)  │
                     ├─────────────────────┤
                     │ • TokenBucket        │
                     │ • SlidingWindowLog   │
                     │ • FixedWindowCounter │
                     └─────────────────────┘
```

### Proyectos

| Proyecto | Responsabilidad |
|---|---|
| **RateLimiter.Core** | Algoritmos e interfaces, sin dependencias de framework |
| **RateLimiter.Api** | Middleware ASP.NET Core, integración HTTP |
| **RateLimiter.Core.Tests** | Tests unitarios para todos los algoritmos y factory |

## Algoritmos Implementados

### 1. Token Bucket

El algoritmo clásico. Cada cliente recibe un balde con `N` tokens que se recarga a lo largo de una ventana de tiempo. Cada request consume un token.

**Características:**
- Permite ráfagas (bursts) hasta la capacidad del balde
- Enforcement suave de la tasa a largo plazo
- Memoria constante O(1) por cliente

**Detalle de implementación:** En cada llamada a `Acquire`, se calcula el tiempo transcurrido desde la última recarga y se agregan tokens proporcionales (con tope en el máximo). Esto evita timers en background y es completamente lazy.

### 2. Sliding Window Log

Mantiene un log con timestamps de cada request dentro de la ventana actual. La ventana se desliza continuamente con el tiempo real.

**Características:**
- El más preciso — sin picos en los bordes de ventana
- Mayor uso de memoria: O(N) por cliente donde N = max requests
- Ideal para enforcement estricto

**Detalle de implementación:** Usa un `LinkedList<DateTimeOffset>` como deque — las entradas expiradas se eliminan desde el frente en cada llamada. El valor de `RetryAfter` apunta exactamente al momento en que la entrada más vieja expirará.

### 3. Fixed Window Counter

Divide el tiempo en ventanas discretas de duración fija y cuenta requests por ventana.

**Características:**
- El más simple y eficiente en memoria: O(1) por cliente
- Tiene un problema conocido de borde: un cliente puede teóricamente enviar 2× el límite en la frontera entre ventanas
- Bueno para rate limiting aproximado

**Detalle de implementación:** La identidad de la ventana se deriva de `UnixTimeMilliseconds / windowMs`, evitando cualquier limpieza de estado. El contador se resetea naturalmente cuando cambia el id de ventana.

## Decisiones de Diseño Clave

### Strategy Pattern en vez de herencia

La interfaz `IRateLimiter` permite intercambiar algoritmos sin modificar el middleware ni los tests. Es una aplicación de libro del patrón Strategy y evita una jerarquía de clases.

### Inyección de TimeProvider para testing

Todos los algoritmos aceptan un parámetro opcional `TimeProvider` (abstracción built-in de .NET 8). Los tests inyectan un `FakeTimeProvider` que permite manipular el tiempo de forma determinística sin `Thread.Sleep` ni assertions frágiles.

### Thread safety mediante lock por cliente

Cada cliente obtiene su propio objeto de lock (la clase de estado mutable en sí). Esto evita un cuello de botella con un lock global — requests concurrentes de distintos clientes nunca compiten. Usamos `ConcurrentDictionary` para el mapeo cliente → estado, que es lock-free para lecturas.

### Resultado como value type

`RateLimitResult` es un `readonly record struct` — esto evita allocations en el heap en el hot path (cada request pasa por el rate limiter).

### Middleware en API, no en Core

El middleware vive en el proyecto API y no en Core. Esto mantiene a Core libre de dependencias de ASP.NET Core, haciendo los algoritmos reutilizables en otros contextos (gRPC, background workers, etc.).

## Trade-offs y Qué Cambiaría en Producción

| Área | Actual (Prototipo) | Producción |
|---|---|---|
| **Storage** | In-memory `ConcurrentDictionary` | Redis con Lua scripts para atomicidad |
| **Eviction** | Sin limpieza de clientes inactivos | Eviction basado en TTL o LRU cache |
| **Distribuido** | Solo un proceso | Redis o consistent hashing entre nodos |
| **Reglas** | Una regla global | Por endpoint, por nivel de usuario |
| **Config** | Hardcodeado en `Program.cs` | `appsettings.json` + hot reload |
| **Observabilidad** | Solo headers en response | Contadores Prometheus + logging estructurado |
| **Identificación de cliente** | Dirección IP | API key, JWT claims, o clave compuesta |

## Uso de AI

Utilicé herramientas de AI para acelerar la implementación asegurándome de entender cada línea:

- **Scaffolding:** Generación de la estructura del proyecto y boilerplate.
- **Revisión de algoritmos:** Discusión de trade-offs y edge cases antes de codificar.
- **Casos de test:** Brainstorming de escenarios de test, especialmente concurrencia y condiciones de borde.
- **Documentación:** Uso de AI para redactar borradores de este documento, que luego revisé y ajusté.

Todo el código es comprendido en su totalidad — no hay código "caja negra" sin documentar.

## Cómo Ejecutar

```bash
# Compilar
dotnet build

# Correr tests
dotnet test

# Levantar la API
cd src/RateLimiter.Api
dotnet run
