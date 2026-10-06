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

## Configuration

No secrets in `appsettings.json`. Local development uses `dotnet user-secrets`;
deployed environments read from AWS Secrets Manager.

## Conventions

- Package versions are managed centrally in `server/Directory.Packages.props`.
- Shared build settings live in `server/Directory.Build.props`. Warnings are errors.
- Money is `decimal` in the domain and a **string** on the wire, never a JSON number.
- Tax dates are `DateOnly` in SAST. Instants are UTC.
