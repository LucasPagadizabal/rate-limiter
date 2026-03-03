# Rate Limiter — Design Document

## Problem

Design and implement a rate limiter that controls the rate of traffic a client can send to an API. Based on Chapter 4 of *System Design Interview* by Alex Xu.

## Architecture Overview

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

### Projects

| Project | Responsibility |
|---|---|
| **RateLimiter.Core** | Algorithms, interfaces, no framework dependencies |
| **RateLimiter.Api** | ASP.NET Core middleware, HTTP integration |
| **RateLimiter.Core.Tests** | Unit tests for all algorithms and factory |

## Algorithms Implemented

### 1. Token Bucket

The classic algorithm. Each client gets a bucket of `N` tokens that refills over a time window. Each request consumes one token.

**Characteristics:**
- Allows bursts up to bucket capacity
- Smooth long-term rate enforcement
- Constant O(1) memory per client

**Implementation detail:** On each `Acquire` call, we calculate the elapsed time since the last refill and add proportional tokens (capped at max). This avoids background timers and is fully lazy.

### 2. Sliding Window Log

Keeps a timestamped log of every request within the current window. The window slides continuously with real time.

**Characteristics:**
- Most accurate — no boundary spikes
- Higher memory usage: O(N) per client where N = max requests
- Best for strict rate enforcement

**Implementation detail:** Uses a `LinkedList<DateTimeOffset>` as a deque — expired entries are evicted from the front on each call. The `RetryAfter` value points exactly to when the oldest entry will expire.

### 3. Fixed Window Counter

Divides time into discrete windows and counts requests per window.

**Characteristics:**
- Simplest and most memory-efficient: O(1) per client
- Has a known boundary problem: clients can theoretically send 2× the limit at a window boundary
- Good for approximate limiting

**Implementation detail:** Window identity is derived from `UnixTimeMilliseconds / windowMs`, avoiding any state cleanup. The counter naturally resets when the window id changes.

## Key Design Decisions

### Strategy Pattern over inheritance

The `IRateLimiter` interface allows swapping algorithms without changing the middleware or tests. This is a textbook application of the Strategy pattern and avoids a class hierarchy.

### TimeProvider injection for testing

All algorithms accept an optional `TimeProvider` parameter (.NET 8 built-in abstraction). Tests inject a `FakeTimeProvider` that allows deterministic time manipulation without `Thread.Sleep` or flaky assertions.

### Thread safety via per-client locking

Each client gets their own lock object (the mutable state class itself). This avoids a global lock bottleneck — concurrent requests for different clients never contend. We use `ConcurrentDictionary` for the client → state mapping, which is lock-free for reads.

### Value-type result

`RateLimitResult` is a `readonly record struct` — this avoids heap allocations on the hot path (every request goes through the rate limiter).

### Middleware in API, not Core

The middleware lives in the API project rather than Core. This keeps Core free of ASP.NET Core dependencies, making the algorithms reusable in other contexts (gRPC, background workers, etc.). Trade-off: slightly less convenience as a NuGet package, but avoiding overengineering per the challenge guidelines.

## Trade-offs & What I'd Change in Production

| Area | Current (Prototype) | Production |
|---|---|---|
| **Storage** | In-memory `ConcurrentDictionary` | Redis with Lua scripts for atomicity |
| **Eviction** | No cleanup of idle clients | TTL-based eviction or LRU cache |
| **Distributed** | Single-process only | Redis or consistent hashing across nodes |
| **Rules** | One global rule | Per-endpoint, per-user tier rules |
| **Config** | Hardcoded in `Program.cs` | `appsettings.json` + hot reload |
| **Observability** | Response headers only | Prometheus counters + structured logging |
| **Client identification** | IP address | API key, JWT claims, or composite key |

## How I Used AI

I used Claude to help accelerate the implementation while ensuring I understood every line:

- **Scaffolding:** Generated the initial project structure and boilerplate.
- **Algorithm review:** Discussed algorithm trade-offs and edge cases before coding.
- **Test cases:** Brainstormed test scenarios, especially concurrency and boundary conditions.
- **Documentation:** Used AI to draft this design document, which I then revised.

Every function and class is fully understood — no undocumented "black box" code.

## How to Run

```bash
# Build
dotnet build

# Run tests
dotnet test

# Run the API
cd src/RateLimiter.Api
dotnet run

# Test it
curl -i http://localhost:5000/
# Repeat rapidly to see 429 responses
```
