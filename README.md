# Parts Store

A retail web application for computer parts, built on Azure: a .NET API, a Vue storefront and administration site, an Azure
Function App for billing and fulfillment, Service Bus between them, Azure SQL, and a Redis cache. This README is both the
guide to the repository and the record of how it was planned and built.

- [What was asked for, and where it lives](#what-was-asked-for-and-where-it-lives)
- [Architecture](#architecture)
- [How the plan was carried out (phases 0-8)](#how-the-plan-was-carried-out-phases-0-8)
- [Where the build departed from the original plan](#where-the-build-departed-from-the-original-plan)
- [Repository layout](#repository-layout)
- [Run it locally](#run-it-locally)
- [Testing and quality results](#testing-and-quality-results)
- [Deploying to Azure](#deploying-to-azure)
- [Documentation index](#documentation-index)
- [Known limitations and what to do before launch](#known-limitations-and-what-to-do-before-launch)

## What was asked for, and where it lives

| Requirement | How it is met | Where |
|---|---|---|
| .NET Core API | Minimal-API .NET 10 service: catalog, cart, addresses, orders, admin | `src/apps/Store.Api` |
| Vue front end | Two Vue 3 + TypeScript apps (storefront, administration), Pinia, **Vue Router 5 file-based routing** | `src/apps/store-web`, `src/apps/admin-web` |
| Styling | **Tailwind CSS 4 + daisyUI 5** in both apps (light and dark themes) | each app's `src/style.css` |
| Azure Function App for billing and fulfillment | Two Service Bus-triggered functions (.NET 10 isolated); payments mocked, UPS (North America) / DHL (elsewhere) simulated | `src/apps/Store.Functions` |
| Service Bus, API to function app | Transactional outbox in the API → `order-placed` queue → billing → `payment-captured` queue → fulfillment | `src/apps/Store.Api/Messaging`, `Store.Messaging` |
| Azure SQL | EF Core with migrations; Entra-only authentication in Azure | `src/apps/Store.Data` |
| Redis to cache the catalog | Cache-aside with version-token invalidation; **5.8× throughput** measured | `src/apps/Store.Api/Catalog` |
| Identity for the store's authentication | ASP.NET Core Identity (bearer + refresh tokens, lockout, password reset) | `src/apps/Store.Api/Auth` |
| MSAL for the administration site | `@azure/msal-browser` (Entra ID) in the admin site; the API validates Entra tokens and the `Admin` role | `src/apps/admin-web/src/auth`, `Store.Api/Admin` |
| Docker files for each front end and the API, for Azure App Service | Three multi-stage, non-root, patched images; front ends configured at start-up so one image serves every environment | `*/Dockerfile` |
| Bicep | Whole environment as code (identities, registry, App Service, Function App, SQL, Service Bus, Redis, Key Vault, monitoring) | `src/infra` |
| Single region, single environment, mock payments | As requested; CI/CD on GitHub Actions | `.github/workflows` |

## Architecture

```
   Customers                                   Staff
       │                                         │
┌──────▼─────────┐                      ┌────────▼─────────┐
│ store-web       │                      │ admin-web         │      nginx containers on
│ Vue + Identity  │                      │ Vue + MSAL (Entra)│      Azure App Service
└──────┬─────────┘                      └────────┬─────────┘
       └───────────────┐      ┌──────────────────┘
                       ▼      ▼
                 ┌───────────────────┐   outbox row, same transaction as the order
                 │ Store.Api (.NET)   │──────────────────────────────┐
                 └──┬──────┬──────┬──┘                               ▼
                    │      │      │                      ┌─────────────────────┐
                    ▼      ▼      ▼                      │ Service Bus          │
              Azure SQL  Redis  Key Vault                │  order-placed        │
                    ▲      (catalog   (secrets, token    │  payment-captured    │
                    │       cache)    -key wrapping)     └───────┬─────────────┘
                    │                                            ▼
                    │                              ┌────────────────────────────┐
                    └──────────────────────────────┤ Store.Functions            │
                                                   │  Billing  → mock payment   │
                                                   │  Fulfillment → UPS / DHL   │
                                                   └────────────────────────────┘
```

**Life of an order**

1. The customer checks out. In **one database transaction** the API snapshots prices and the shipping address, **reserves stock**,
   empties the cart and writes an `OrderPlaced` row to the outbox. A retried request with the same `Idempotency-Key` returns the
   original order.
2. A background dispatcher publishes the outbox row to the `order-placed` queue (the row's id is the message id, so a repeat is
   dropped by duplicate detection).
3. **Billing** charges the (mock) provider, records the payment, marks the order *Paid* and publishes `payment-captured`. A declined
   card marks the order *Payment failed* and releases the reservation.
4. **Fulfillment** books the carrier (UPS for US/CA/MX, DHL otherwise), records the shipment and tracking number, marks the order
   *Shipped* and turns the reservation into a real stock reduction.
5. The customer's order page refreshes itself as the order progresses. Messages that fail five times land in the dead-letter queue,
   where staff can inspect, replay or discard them in the admin site.

Every step is safe to repeat (Service Bus delivers at least once): status guards, one-payment-per-order and one-shipment-per-order
unique indexes, deterministic carrier tracking numbers, and row-version concurrency on stock and orders.

## How the plan was carried out (phases 0-8)

The work followed a nine-step plan, one phase at a time, each verified before moving on.

| Phase | What was built | Verified by |
|---|---|---|
| **0 Foundations** | Solution and projects, `docker-compose` for SQL Server, Redis, Service Bus emulator and Azurite, git, repository layout rules (everything under `src/`) | Builds; compose file validates |
| **1 API core and catalog** | EF Core model and migrations, 50 seeded parts across 8 categories, catalog endpoints (paging, search, brand/price/spec filters, sort), facets endpoint, Redis cache-aside, health checks, OpenAPI, ProblemDetails | API tests; run against real SQL Server and Redis |
| **2 Customer auth, cart, addresses** | Identity (register, login, refresh, logout that revokes the refresh token, password reset), cart with stock checks, address book (default handling, ownership), per-IP rate limiting, CORS | API tests; real-database smoke test |
| **3 Storefront** | Vue 3 app with a typed API client generated from OpenAPI, file-based routes, filters kept in the URL, guest cart merged on sign-in, checkout, account, Tailwind + daisyUI, dark theme | Unit tests; headless-browser screenshots incl. phone width |
| **4 Checkout and messaging** | Transactional outbox, dispatcher with exponential back-off, Service Bus publisher, idempotent checkout with stock reservation, order list/detail/status pages | API tests; **two customers racing for the last unit** against SQL Server; message read off the real queue |
| **5 Function App** | Billing and fulfillment functions, mock payment provider and simulated UPS/DHL carriers, retries, dead-lettering with reasons, tracking shown to customers | 26 unit tests; whole pipeline run with the real Functions host |
| **6 Administration site** | MSAL sign-in, product/category/stock management (cache invalidated on every write), order cancel and refund, dashboard, failed-message console, admin API with the `Admin` role | API tests; 12-step browser walk-through |
| **7 Containers, infrastructure, pipeline** | Dockerfiles, Bicep (identity-first, Entra-only SQL, no shared keys), GitHub Actions CI and deploy, SQL access tool for managed identities, Data Protection keys in SQL | Images run in a real browser; all templates compile clean; workflows pass `actionlint` |
| **8 Quality** | 80 Playwright tests (functional, accessibility, security attacks, phone), load test of the cache, security review with scanners and fixes | See [results](#testing-and-quality-results) |

Requirements decisions made along the way (as agreed in conversation): payments mocked; shipping simulated with UPS for North America
and DHL for everywhere else; Bicep for infrastructure; one region and one environment; GitHub Actions for CI/CD; Tailwind and daisyUI
for styling in both front ends; Vue Router 5 file-based routing in both front ends.

## Where the build departed from the original plan

| Plan said | What was built, and why |
|---|---|
| Fulfillment reserves stock | **Checkout reserves stock**, in the same transaction as the order, so stock can't be oversold between "accepted" and "shipped". Billing releases it on failure; fulfillment consumes it |
| Function output binding sends `payment-captured` | The worker returned the whole message object as the body (found by running the real pipeline). Functions now **send explicitly** through a shared publisher (`Store.Messaging`) |
| Managed identity for SQL, Service Bus, Key Vault | **User-assigned** identities created first, so every role exists before any app starts and no Microsoft Graph lookups are needed |
| Azure Cache for Redis | **Azure Managed Redis** (the older service is being retired) |
| Function hosting unspecified | **Flex Consumption**, which supports .NET 10 isolated and scales on queue depth |
| Customer sign-in via cookies *or* tokens | **Bearer tokens only**; Identity's cookie option is actively refused |
| (not in plan) | **Dead-letter console** in the admin site; **Data Protection keys in SQL** (otherwise every restart or second instance signs customers out); **audit log** of admin changes; SQL auditing; image/dependency vulnerability gates in CI |
| Rate limit all auth endpoints | Strict limit only on credential endpoints; token refresh (every page load) is exempt so shared office IPs aren't locked out |
| Repository root has only README and LICENSE | Plus `.github/`, because GitHub only runs workflows from there |
| Admin sign-in with MSAL | MSAL is implemented, but because no Entra tenant existed yet there is also a **Development-only sign-in** (proven inert in Production by a test) |

Other build-time decisions worth knowing: TypeScript is pinned to 5.9 (TypeScript 7 removed the compiler API that `vue-tsc` and
`openapi-typescript` use); the Function project was created by hand because the Functions templates need Core Tools; the OpenAPI
document is generated by Debug builds and committed, and CI fails if it or the generated API clients are stale.

## Repository layout

All source lives under `src/`. The only other things at the root are this README, the license, and `.github/workflows/`.

```
.github/workflows/   ci.yml (pull requests), e2e.yml (browser suite), deploy.yml (release)
src/
  apps/
    Store.Api/         .NET API (+ Dockerfile)
    Store.Functions/   billing + fulfillment (Azure Functions)
    Store.Data/        EF Core context, migrations, seed data
    Store.Domain/      entities and message contracts
    Store.Messaging/   Service Bus publisher and dead-letter administration
    store-web/         Vue storefront (+ Dockerfile, nginx config)
    admin-web/         Vue administration site (+ Dockerfile, nginx config)
  tests/
    Store.Api.Tests/        API integration + security tests
    Store.Functions.Tests/  billing, fulfillment, provider tests
    e2e/                    Playwright browser tests
    load/                   k6 load test
  infra/                Bicep (main.bicep + modules/), scripts/grant-sql-access.cs
  docs/                 deployment, Entra setup, performance, security review
  docker/               local Service Bus emulator config
  docker-compose.yml    local infrastructure; `--profile apps` also runs the three images
  PartsStore.slnx
```

## Run it locally

Prerequisites: .NET SDK 10, Node.js 22, Docker. (Azure Functions Core Tools v4 to run the function app.)

```powershell
cd src
docker compose --profile apps up -d --build        # SQL Server, Redis, Service Bus emulator, Azurite + API (5233), storefront (5173), admin (5174)
cd apps/Store.Functions
copy local.settings.example.json local.settings.json   # once
func start                                               # billing and fulfillment
```

Without the `apps` profile, `docker compose up -d` starts only the infrastructure, so the apps can be run from source:
`dotnet run --project apps/Store.Api` (migrates and seeds the dev database), and `npm run dev` in `apps/store-web` and
`apps/admin-web`. If port 1433 is taken, set `SQL_PORT` before `docker compose`.

Locally the admin site signs in with a development token (only an API in the `Development` environment offers it). To use real
Microsoft sign-in see [`src/docs/entra-setup.md`](src/docs/entra-setup.md).

**Trying the failure paths.** Payments and carriers are simulated. The customer's email selects a behavior (plus-addressing):

| Email contains | Effect |
|---|---|
| *(nothing special)* | Payment approved; ships via UPS (US/CA/MX) or DHL (everywhere else) |
| `+decline` | Card declined: order becomes *Payment failed* and its stock reservation is released |
| `+error` | Payment-provider outage: retried, then dead-lettered; the order stays *Pending* |
| `+shipfail` | Carrier outage: retried, then dead-lettered; the order stays *Paid* |

## Testing and quality results

| What | Result | Run it |
|---|---|---|
| .NET unit + integration tests | **117 passing** (91 API incl. security rules, 26 billing/fulfillment) | `dotnet test` (no containers) |
| Front-end unit tests | **41 passing** (22 storefront, 19 admin) | `npm test` in each app |
| End-to-end browser tests | **80 passing**, desktop + phone: browsing, cart, auth, checkout, payment and shipping incl. failures, admin workflows, accessibility (WCAG 2.1 A/AA, both themes), security attacks. Run 3× in parallel to check for flakiness | [`src/tests/e2e/README.md`](src/tests/e2e/README.md) |
| Load test, catalog with Redis on vs off | **12,904 vs 2,219 req/s (5.8×); p95 7.8 ms vs 56.4 ms; 0 errors** | [`src/docs/performance.md`](src/docs/performance.md) |
| Security review | Scanners (NuGet, npm, Trivy images + Dockerfiles, Checkov Bicep) + code review + attack tests. 10 findings fixed (incl. 1 critical + 9 high nginx CVEs), risks accepted in writing | [`src/docs/security-review.md`](src/docs/security-review.md) |
| Infrastructure and pipeline | All 12 Bicep templates compile with no warnings; 3 workflows pass `actionlint` | CI |

**CI** (`.github/workflows`): `ci.yml` on every pull request (build, test, vulnerable-package and image scans, Bicep, container builds,
generated-file freshness); `e2e.yml` runs the browser suite (pull requests, nightly, on demand; does not gate deployment); `deploy.yml`
tests → provisions → builds images → migrates the database → releases → smoke-tests.

## Deploying to Azure

`src/infra/main.bicep` provisions everything, and `deploy.yml` releases on every push to `main` using OIDC (no stored credentials).
Access between services uses managed identities: there are no passwords, keys or SAS tokens. One-time setup (resource group, the
pipeline's Azure identity, repository variables, the Entra app registration) is in
[`src/docs/deployment.md`](src/docs/deployment.md).

## Documentation index

| Document | About |
|---|---|
| [`src/docs/deployment.md`](src/docs/deployment.md) | What gets created, the permission model, one-time setup, what a pipeline run does, rollback, troubleshooting |
| [`src/docs/entra-setup.md`](src/docs/entra-setup.md) | Registering the admin site with Microsoft Entra ID |
| [`src/docs/performance.md`](src/docs/performance.md) | Cache on vs off load test: method, results, caveats |
| [`src/docs/security-review.md`](src/docs/security-review.md) | Findings, fixes, accepted risks, what was verified |
| [`src/tests/e2e/README.md`](src/tests/e2e/README.md) | How the browser tests are written and run |

## Known limitations and what to do before launch

Honest status: **the infrastructure templates and pipeline have never been run against a real Azure subscription**, and Microsoft
sign-in has not been tried against a real tenant. Everything else was run end to end locally.

Before this serves real customers:

1. **Deploy and fix the first run** (region support for Flex Consumption and Managed Redis, provider registration, quotas); see the
   troubleshooting table in `deployment.md`.
2. **Register the Entra app** and switch the admin site from the development sign-in to Microsoft sign-in.
3. **Add a real email provider** and turn on `RequireConfirmedEmail`; until then password reset can't work in practice.
4. **Replace the simulations** (payment provider, UPS/DHL) behind the existing interfaces; with a real card processor, use its hosted
   card fields so card numbers never touch these servers.
5. **Decide on the accepted risks** in the security review (password policy, registration enumeration, refresh token storage, public
   endpoints vs private networking), and add customer data deletion/export if privacy law applies.
6. Defender for SQL is on by default and carries a monthly charge; turn it off with `sqlThreatDetection = false` if undesired.

## License

[MIT](LICENSE)

# AI Cost

```
 Total cost:            $25.13
 Total duration (API):  1h 15m 35s
 Total duration (wall): 3h 40m 8s
 Total code changes:    15408 lines added, 322 lines removed
 Usage by model:
     claude-haiku-5-5:  17.5k input, 4.7k output, 0 cache read, 0 cache write ($0.0041)
    claude-sonnet-5-5:  4.4k input, 694.6k output, 145.2m cache read, 913.4k cache write ($25.13)

 Current session
 █████████████████████████████████████████▌         83% used
 Resets 2:50am (Europe/Paris)

 Current week (all models)
 ███████                                            14% used
 Resets Oct 24, 12pm (Europe/Paris)
```