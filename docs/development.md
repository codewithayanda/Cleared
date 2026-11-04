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

`SmtpEmailTests` also starts a throwaway Mailpit container, a real SMTP server that keeps what it receives, and reads
each message back through its HTTP API. `SmtpFailureTests` uses a tiny fake SMTP server to make refusals and timeouts
happen on demand.

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
with the balancer as the only trusted proxy. Do that before deploying.

Wrong passwords are counted on the server, per email, whether or not the email has an account. The
fifth wrong one locks sign-in for that email for 15 minutes from every address, and the answer is a
429 with `Retry-After`. Change it with `SignInThrottle:MaxTries`, `LockDuration` and `ForgetAfter`.
A try is reserved before the password is checked, so guesses that arrive together cannot all be
checked.

A forgotten password is reset through an emailed link: `POST /auth/forgot-password`, then `POST /auth/reset-password`.
The link works once and for 30 minutes, and a person has one live link at a time. An email may ask for 3 links an
hour, and the API answers the same for an email with an account and one without. Change these with
`PasswordReset:TokenLifetime`, `MaxRequestsPerWindow` and `Window`, and the per-address limits with
`RateLimits:ForgotPassword` and `ResetPassword`. `App:PublicUrl` is where people open the web app, which the link
in the email points at. It is `http://localhost:4200` in `appsettings.Development.json`, and the API refuses to
start without one.

By default no email is sent. In Development the API prints each email to its console, link included, so that
is where to find a reset link. Everywhere else a message is dropped with a warning that never carries the address
or the link. Set `Email:Smtp:Host` and the API sends through that SMTP server instead, in every environment.
The other settings are `Port` (587), `TlsMode` (`StartTls`, `SslOnConnect` or `None`), `Username`, `Password`,
`From` (required), `FromName` (`Cleared`) and `Timeout` (15 seconds for each step). `StartTls` refuses a server
that cannot encrypt, and a username is only allowed with `StartTls` or `SslOnConnect`, so a password never crosses
the network in the clear. The API refuses to start with settings that break these rules. A message the server
refuses is dropped and logged as one fixed sentence, with no address, link or password in it, and it is not retried.

To read emails on your own machine, run Mailpit, a mail catcher that keeps every message and shows it at
`http://localhost:8025`. It needs no account and nothing leaves your laptop:

```bash
podman run -d --name cleared-mail -p 1025:1025 -p 8025:8025 axllent/mailpit:v1.33.0
```

Then point the API at it from `server/Cleared.API`, and restart the API:

```bash
dotnet user-secrets set "Email:Smtp:Host" "localhost"
dotnet user-secrets set "Email:Smtp:Port" "1025"
dotnet user-secrets set "Email:Smtp:TlsMode" "None"
dotnet user-secrets set "Email:Smtp:From" "no-reply@cleared.test"
```

Remove those four settings with `dotnet user-secrets remove` to go back to printing in the console, and use
`podman start cleared-mail` after the machine has restarted. A real provider's password goes in the same way, typed
by you into `dotnet user-secrets`, never into a file in the repo.

## Conventions

- Package versions are managed centrally in `server/Directory.Packages.props`.
- Shared build settings live in `server/Directory.Build.props`. Warnings are errors.
- Money is `decimal` in the domain and a **string** on the wire, never a JSON number.
- Tax dates are `DateOnly` in SAST. Instants are UTC.
- Every POST that creates or moves money takes an `Idempotency-Key` header and runs through `IdempotentExecutor`, so a repeated request returns the first answer instead of doing the work twice. `IdempotencyGuardTests` fails a write endpoint that skips it.
- A session is a 15-minute access token that the client keeps in memory, plus a refresh token in an HttpOnly, `SameSite=Strict` cookie (`cleared_refresh`, path `/api/v1/auth`). Every refresh swaps the cookie for a new one, and presenting an old one ends the whole session, so a client must never send the same cookie from two places at once. The auth endpoints refuse cross-site requests, so the client and the API must be served from the same site. In development the Angular proxy does that.
- The client restores its session before the first route runs, shares one refresh between callers and between tabs (Web Locks), and keeps a flag, never a token, in `localStorage` so first-time visitors skip the startup call. `session-flow.spec.ts` proves the pieces work together.
- Sign-in is throttled on the server, per email and whether or not the email has an account, so the answer never shows which emails have one (`SignInThrottle`, table `sign_in_throttles`, which stores only a hash of the email). The sign-in page keeps nothing in the browser. It counts down whatever `Retry-After` the server sends.
- Email goes through `IEmailQueue`: a bounded queue that a background dispatcher empties, one message at a time, after the response has gone, into `IEmailSender`. The integration tests replace the sender with `CapturingEmailSender` (`factory.Emails`), so none sends a real email, and they wait for the queue to empty before reading what was sent.
- A reset link's secret sits after the `#`, so it never reaches a server log or a `Referer` header. The page reads it once and removes it from the address bar. Only a hash of it is stored (`password_reset_tokens`), and the database allows one live link per person.
- CI scans dependencies on every push. The NuGet scan fails on any known vulnerability, and `npm audit --audit-level=high` fails the client on a high or critical advisory. Dev tools count, because they run in CI and on a laptop. Run the same command in `client` to see what CI sees.
- `client/package.json` pins `vite` to 8.3.5 with `overrides`, because `@angular/build` 22.2.1 pins vite 8.3.0, which has high advisories. Remove the override once Angular is updated to 22.2.5 or later, whose `@angular/build` pins a patched vite. Angular's packages pin each other's versions exactly, so update them together with `ng update`, never one at a time.
- A sender that fails throws `EmailDeliveryException` with a fixed message and no inner exception, because a mail server's reply often repeats the address it refused and a log is readable by many people. `SmtpEmailSender` also names the domain of `Email:Smtp:From` in the hello and the message id, so the name of the machine it runs on never leaves it. `SmtpFailureTests` proves the address, the link, the user name and the password stay out.
