# Cleared — web client

Angular 22 SPA. Standalone components, **zoneless** change detection, Tailwind 4, Vitest.

See the [root README](../README.md) for the monorepo layout and the backend.

## Commands

```bash
npm start            # dev server on :4200, proxies /api to localhost:5038
npm run build        # production build (runs bundle budgets)
npm test             # Vitest, watch mode
npm run test:ci      # Vitest once, with coverage
npm run lint         # ESLint, including template accessibility rules
npm run lint:fix
npm run format       # Prettier write
npm run format:check # Prettier check (CI runs this)
```

## Zoneless — read this before writing a component

There is no `zone.js`. Angular will not detect changes just because a callback ran, so
the pattern that works in a zone-based app does **not** work here:

```ts
// Does NOT update the view.
this.http.get<Invoice[]>(url).subscribe((data) => (this.invoices = data));
```

Use signals instead, and the view tracks them automatically:

```ts
private readonly invoices = signal<Invoice[]>([]);

load() {
  this.http.get<Invoice[]>(url).subscribe((data) => this.invoices.set(data));
}
```

Every component is generated with `ChangeDetectionStrategy.OnPush` (the Angular 22
default). Keep it.

## Structure

```
src/app/
  core/            cross-cutting, app-wide singletons
    guards/        functional CanActivateFn guards
    interceptors/  functional HttpInterceptorFn
    models/        shared interfaces and types
    services/      API clients and app services
  features/        one folder per feature, lazy loaded
  shared/          reusable presentational pieces
    components/
src/environments/  build-time configuration
```

Path aliases, so imports don't crawl back up the tree:

| Alias | Resolves to |
|---|---|
| `@core/*` | `src/app/core/*` |
| `@features/*` | `src/app/features/*` |
| `@shared/*` | `src/app/shared/*` |
| `@env` | `src/environments/environment` |

## API URL

`environment.apiUrl` is `/api/v1` — **relative, on purpose**. In deployed environments
CloudFront routes `/api/*` to the load balancer, so one built artifact works everywhere:
build once, promote the same bundle through staging and production. Locally the dev
server proxies `/api` to the API instead (see `proxy.conf.json`).

## Package registry

`.npmrc` pins installs to `https://registry.npmjs.org/`. Keep it. If a machine has a
different default registry configured, that registry's URLs get written into
`package-lock.json`, and the lockfile then only installs for people who can reach it —
CI included.

`npm run check:registry` enforces this, and CI runs the same check before `npm ci`.

If it ever fails:

```bash
rm -rf node_modules package-lock.json && npm install
```

## Bundle budgets

The initial chunk is capped at 280 kB (warning) / 350 kB (error), raw. The baseline
skeleton is 217 kB raw / 59.5 kB gzipped, so that leaves deliberate headroom without
being unbounded.

This matters because the public payment page at `/pay/:token` is opened by the Owner's
customers — often on a mid-range Android over mobile data — and must stay under 200 kB
gzipped. Nothing from the authenticated app may leak into that chunk. Note that Angular
budgets measure **raw** size; the gzipped gate is a separate CI check added when that
route exists.

## Testing

Vitest with jsdom. `TestBed`, `provideHttpClientTesting` and `HttpTestingController`
work exactly as they do under Karma — only the runner differs: `describe`/`it`/`expect`
come from Vitest, and spies are `vi.fn()` rather than `jasmine.createSpy`.

A guard or interceptor is not done until a test proves both that it allows the valid
case **and** that it rejects the invalid one.
