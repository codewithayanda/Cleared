# Development notes

For people working on the code. The project overview is in the [main README](../README.md).

## Layout

```
server/          ASP.NET Core 10 backend (Clean Architecture)
  Cleared.Domain/          entities, value objects, business rules. Zero package references
  Cleared.Application/     use-case services and the ports they depend on
  Cleared.Infrastructure/  adapters implementing those ports (EF Core, Identity, PDF)
  Cleared.API/             controllers, auth, middleware. The composition root
  Cleared.*.Tests/         see "Testing" below
client/          Angular 22 SPA
```

Dependencies point inward only. `Cleared.Domain` references nothing; `Cleared.Application`
declares the interfaces that `Cleared.Infrastructure` implements. `Cleared.Architecture.Tests`
checks the declared project references, so a dependency added in the wrong direction fails
the build.

## Prerequisites

- .NET SDK 10 (pinned in `global.json`)
- PostgreSQL 17
- Node 24 + npm (for the client)

## Running

```bash
cd server
dotnet run --project Cleared.API
```

Then check `GET /health`. OpenAPI is served at `/openapi/v1.json` in Development only.

## Testing

```bash
cd server
dotnet test Cleared.slnx
```

| Project | Scope |
|---|---|
| `Cleared.Domain.Tests` | Pure logic: VAT, money, rounding, state transitions. No I/O. |
| `Cleared.Application.Tests` | Use-case orchestration against faked ports. |
| `Cleared.Architecture.Tests` | Dependency direction and layering rules. |
| `Cleared.Integration.Tests` | Real HTTP requests through the whole API, and concurrency behaviour, against a real database. |

Integration tests run against a real PostgreSQL, never the EF Core in-memory provider,
which cannot evaluate row-level security, transactions or row locks. That package is
blocked at build time in `server/Directory.Build.targets`.

`Cleared.Integration.Tests` starts its own throwaway PostgreSQL container with Testcontainers,
applies the migrations to it, and discards it afterwards. It never reads user secrets or
touches a database you care about. It needs a container runtime: Docker, or Podman with its
machine running (`podman machine start`).

Every endpoint that touches tenant data needs an entry in `IsolationCases.All`, which makes a
second tenant attack the first one's data. `EndpointGuardTests` reads the routing table and
fails when an endpoint has neither an entry nor a reason to be exempt, so a new route cannot
skip the isolation check.

Writes are checked as well as reads: `TenantWriteInterceptor` refuses to save a row of a filtered
entity that does not belong to the caller's tenant, and `TenantWriteInterceptorTests` proves it.

Session behaviour (`RefreshTokenTests`, `SessionEndpointTests`, `SessionHardeningTests`) is tested with a
clock the test moves by hand. `CreateAnonymousClient` keeps no cookies, so a test passes the refresh
cookie itself and knows exactly what each request carried.

`RateLimitTests` start copies of the API with tiny limits and a header that sets the caller's
address. The shared copy's limits are set so high that the suite can register hundreds of tenants
from one address.

## Configuration

No secrets in `appsettings.json`. Local development uses `dotnet user-secrets`;
deployed environments read from AWS Secrets Manager.

`Jwt:SigningKey` must be at least 32 bytes or the API refuses to start. Make one with
`openssl rand -base64 48`.

A session ends after 7 days without a refresh, and after 30 days whatever its activity. Override them
with `Session:IdleLifetime` and `Session:AbsoluteLifetime` (for example `7.00:00:00`). The API refuses
to start if the absolute limit is shorter than the idle one.

Callers may make a limited number of requests in a window. Sign-in and registration are limited per
address (10 a minute and 10 an hour), refreshing and signing out share 30 a minute per address, and
everything else is 300 a minute per signed-in user, or per address when there is no token. Change any
of them with `RateLimits:Login`, `Register`, `Session` or `Api`, each with a `PermitLimit` and a
`Window` (for example `RateLimits:Login:Window` = `00:01:00`). The counters live in memory, so each
running copy of the API counts on its own. The API sees the address of whatever connects to it, so
behind a load balancer every caller looks like the balancer until forwarded headers are configured
with the balancer as the only trusted proxy. Do that before deploying. An account is locked for 15
minutes after 5 wrong passwords.

## Conventions

- Package versions are managed centrally in `server/Directory.Packages.props`.
- Shared build settings live in `server/Directory.Build.props`. Warnings are errors.
- Money is `decimal` in the domain and a **string** on the wire, never a JSON number.
- Tax dates are `DateOnly` in SAST. Instants are UTC.
- Every POST that creates or moves money takes an `Idempotency-Key` header and runs through `IdempotentExecutor`, so a repeated request returns the first answer instead of doing the work twice. `IdempotencyGuardTests` fails a write endpoint that skips it.
- A session is a 15-minute access token that the client keeps in memory, plus a refresh token in an HttpOnly, `SameSite=Strict` cookie (`cleared_refresh`, path `/api/v1/auth`). Every refresh swaps the cookie for a new one, and presenting an old one ends the whole session, so a client must never send the same cookie from two places at once. The auth endpoints refuse cross-site requests, so the client and the API must be served from the same site. In development the Angular proxy does that.
- The client restores its session before the first route runs, shares one refresh between callers and between tabs (Web Locks), and keeps a flag, never a token, in `localStorage` so first-time visitors skip the startup call. `session-flow.spec.ts` proves the pieces work together.
- The API never says an account is locked, because that would show anyone which emails have accounts. The sign-in page counts wrong passwords per email in `localStorage` (`LockoutTracker`) so it can show a countdown that survives a reload. It is only a courtesy to the person, and the server alone enforces the lock.
