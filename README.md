# Cleared

Multi-tenant invoicing and VAT platform for South African SMEs.

## Layout

```
server/          ASP.NET Core 10 backend (Clean Architecture)
  Cleared.Domain/          entities, value objects, business rules — zero package references
  Cleared.Application/     use-case services and the ports they depend on
  Cleared.Infrastructure/  adapters implementing those ports (EF Core, storage, email, payments)
  Cleared.API/             controllers, auth, middleware — the composition root
  Cleared.*.Tests/         see "Testing" below
client/          Angular 20 SPA
docs/            design documents and architecture decision records
```

Dependencies point inward only. `Cleared.Domain` references nothing; `Cleared.Application`
declares the interfaces that `Cleared.Infrastructure` implements. `Cleared.Architecture.Tests`
enforces this so it cannot silently regress.

## Prerequisites

- .NET SDK 10 (pinned in `global.json`)
- PostgreSQL 17
- Podman (for integration tests via Testcontainers)
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
| `Cleared.Domain.Tests` | Pure logic — VAT, money, rounding, state transitions. No I/O. |
| `Cleared.Application.Tests` | Use-case orchestration against faked ports. |
| `Cleared.Architecture.Tests` | Dependency direction and layering rules. |

Integration tests run against real PostgreSQL via Testcontainers — never the EF Core
in-memory provider, which cannot evaluate row-level security, transactions or row locks.
That package is blocked at build time in `server/Directory.Build.targets`.

## Configuration

No secrets in `appsettings.json`. Local development uses `dotnet user-secrets`;
deployed environments read from AWS Secrets Manager.

## Conventions

- Package versions are managed centrally in `server/Directory.Packages.props`.
- Shared build settings live in `server/Directory.Build.props`. Warnings are errors.
- Money is `decimal` in the domain and a **string** on the wire — never a JSON number.
- Tax dates are `DateOnly` in SAST. Instants are UTC.
