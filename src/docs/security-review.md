# Security review

Scope: the whole system as built in phases 0-7 (API, Function App, both front ends, container images, Bicep infrastructure,
CI/CD pipeline). Reviewed in phase 8 by combining automated scanners, a manual read of the code against the OWASP Top 10,
and adversarial tests that run against the real stack. Findings were fixed where the fix was small and clearly right, and
recorded below where it was a trade-off that belongs to the owner.

> **Not covered:** a penetration test by a third party, a review of the live Azure environment (nothing has been deployed
> yet), or real payment/email/carrier integrations (all simulated). Revisit when any of those exist.

## Method

| What | How | Result |
|---|---|---|
| Known-vulnerable dependencies | `dotnet list package --vulnerable --include-transitive`, `npm audit` (both front ends) | 0 findings |
| Container images | Trivy (OS packages + libraries) on all three images; Trivy on the Dockerfiles | 1 CRITICAL + 9 HIGH in the nginx images, now 0 (see F1); Dockerfile hygiene fixed (F9) |
| Infrastructure as code | Checkov on the Bicep templates | 38 findings → triaged below; 1 template (`rbac.bicep`) could not be parsed by the tool and was reviewed by hand |
| Code review | Authentication, authorization, input handling, error handling, logging, secrets, injection, XSS, CSRF, redirects, CORS, cookies, caching, concurrency | Findings F2-F8 |
| Adversarial tests | 15 Playwright tests + 11 API integration tests that attack the running system (`tests/e2e/tests/store/security.spec.ts`, `Store.Api.Tests/SecurityTests.cs`) | All pass; they now guard against regressions |

## Findings that were fixed

| # | Severity | Finding | Fix | Guarded by |
|---|---|---|---|---|
| F1 | **High** | The storefront and admin images shipped **nginx 1.28.2 with one CRITICAL and nine HIGH known vulnerabilities**. The base image pins nginx and its optional modules (GeoIP, image filter, njs, XSLT) so a plain `apk upgrade` skipped them; the modules also pulled in vulnerable libtiff/libpng/libxml2. | Modules we don't use are removed, nginx is unpinned and everything upgraded at every build (nginx 1.28.3, 0 fixable HIGH/CRITICAL, images also smaller) | CI scans every image with Trivy and fails on any fixable HIGH/CRITICAL |
| F2 | Medium | Identity's login endpoint can issue an **auth cookie** (`?useCookies=true`). Nothing uses it, but it silently added a cookie-based (CSRF-relevant) way in next to the bearer-only design. | Refused with 400 | `CookieSignIn_IsRefused…`, Playwright test |
| F3 | Medium | A malformed JSON body returned **HTTP 500**, not 400. No details leaked, but a client mistake looked like a server fault: error-rate alerts and dashboards would fire, and it's trivially triggerable. | Framework "bad request" exceptions map to their 4xx with a generic message | `Errors_DoNotLeakInternalDetails` |
| F4 | Medium | Dead-letter administration reported a message as "gone" (404) when another operator was acting on it, because the receive locks hide it from the second operator's batch. An admin could discard or skip the wrong thing believing it was already handled. | Retries briefly, then answers **409 "busy, try again"** if the message still exists | `DeadLetter_ThatAnotherOperatorIsHandling…`; found by the end-to-end run |
| F5 | Low | API responses carried no defensive headers, and personal data (cart, orders, addresses, profile, all of `/api/admin`) had no `Cache-Control: no-store`. | `nosniff`, `Referrer-Policy: no-referrer`, a locked-down CSP for JSON, `no-store` on private paths, HSTS in non-development | `Responses_CarryDefensiveHeaders`, `PersonalData_IsNeverCached…` |
| F6 | Low | nginx lacked HSTS and the legacy `X-Frame-Options`. | Added (alongside the existing strict CSP, nosniff, referrer and permissions policies) | `security.spec.ts` header tests |
| F7 | Low | Product, category and stock edits by staff left **no audit trail** (only order cancel/refund did). | One structured `Admin.Audit` log line per change (who, what; never customer data) | `AdminChanges_AreAuditLogged…` also asserts no email or token is ever logged |
| F8 | Low | Azure SQL had no auditing or threat detection. | Auditing of sign-ins and permission/schema changes to Log Analytics (90-day retention); Microsoft Defender for SQL threat detection (parameter `sqlThreatDetection`, **billed per server**) | Bicep compiles; Checkov |
| F9 | Low | Dockerfiles had no explicit `USER` (the base already ran non-root, now stated and verified at runtime); a scanner flagged a variable *named* `AUTH_MODE` as a secret. | `USER 101`; placeholder `ENV` lines removed (the start-up script validates required values) | CI asserts every image runs non-root |
| F10 | Low (availability) | Customer token-signing keys lived inside the container, so a restart or a second instance signed everyone out. *(Fixed in phase 7; listed because the effect is security-relevant.)* | Keys stored in SQL, wrapped by a Key Vault key in Azure | `TokenSigningKeys_AreStoredInTheDatabase…`, verified across a destroyed container |

Accessibility problems found by the axe-core scans (also fixed): unlabelled filter dropdowns, link names that didn't contain
their visible text (cart count, logo), a hint inside a `<label>` polluting the field's name, an ARIA attribute not allowed on the
drawer overlay, and several colour-contrast failures (tinted badges, accent-coloured prices in the dark theme, stacked dimming of
small text, the error red). Every accessibility scan now passes, in the light and dark themes.

## Accepted risks and decisions for the owner

| # | Risk | Why it's acceptable now | Revisit when |
|---|---|---|---|
| A1 | **Registration reveals whether an email is registered** (Identity's "duplicate email" response). | Sign-in and password-reset answers are uniform; registration and sign-in are rate limited per IP and accounts lock after 5 failures. Removing it means an email-first flow with confirmation. | Before launch if enumeration matters to your threat model |
| A2 | **Passwords: 8+ characters with complexity, no breached-password check.** | Meets a common baseline; lockout and rate limits cap guessing. | Raise to 12+ and screen against breached-password lists before launch |
| A3 | **The refresh token is in `localStorage`**, so a successful XSS could steal it. | XSS is closed off in depth (Vue escaping, no `v-html`, strict CSP with no inline/eval/third-party scripts, tested with hostile input); logout revokes the refresh token; access tokens last 15 min. | If the threat model grows: move to a backend-for-frontend with `HttpOnly` cookies |
| A4 | **No email provider yet**: account confirmation is off, and password reset only works for confirmed addresses. | The stub never logs codes outside Development. | **Before launch**: wire a real provider and turn on `RequireConfirmedEmail` (TODO in `AuthEndpoints.cs`) |
| A5 | **Public network endpoints** for SQL, Key Vault, Redis, Service Bus, Storage and the web apps. | All data planes require Entra/RBAC or keys-in-Key-Vault and TLS 1.2; there are no passwords or shared keys. Private endpoints are the stronger posture but add cost and complexity. | When compliance requires it; see `deployment.md` |
| A6 | One App Service instance, no zone redundancy, LRS storage, software-protected Key Vault keys with no expiry, no Defender for Containers. | Availability/cost trade-offs, not exposures. | When the availability target or compliance demands it |
| A7 | Per-IP rate limiting is shared by everyone behind one NAT. | Only credential endpoints are strict (20/min); page loads (token refresh) are exempt. Behind App Service the forwarded-headers middleware keeps its default limit of one hop, i.e. the address the platform's front end appended (the rightmost), so a client can't spoof its way out of the limit by sending its own header. | If the limit causes complaints: raise it, or key by account too |
| A8 | `AllowedHosts` is `*`. | App Service only routes its own hostnames to the container. | When custom domains are added (set it with them) |
| A9 | GitHub Actions are pinned to version tags, not commit SHAs. | Only first-party and widely used actions; workflow permissions are minimal and there is no `pull_request_target`. | Add Dependabot and pin by SHA |
| A10 | No bot protection (CAPTCHA) on registration; no account deletion/export for customers. | Out of scope for the simulated store. | Before handling real customers (privacy law) |
| A11 | Payments are simulated: there is no card data in this system and PCI scope is nil. | By design. | With a real provider, use its hosted card fields so card numbers never touch our servers |

### Infrastructure scanner findings, triaged

Checkov's remaining failures all fall into the accepted risks above (A5, A6) or are not applicable: *client certificates* and *public
network access disabled* for the web apps (they are public by design), *HSM-backed keys* and *key/secret expiry* (A6; an
expiring wrapping key would break token validation), *minimum instances for failover* (A6; `appServiceInstances` is a parameter),
*health check on the Function App* (it has no HTTP endpoints), and *purge protection* (flagged only because the setting is a
parameter that defaults to on). `rbac.bicep` could not be parsed by the tool (it uses a user-defined function), so it was reviewed
manually: it grants exactly the roles documented in `deployment.md` and nothing broader.

## Things checked that were already sound

* **Authorization:** a test lists every endpoint in the application and fails if one is reachable without signing in that isn't on
  an explicit allow-list; another asserts every `/api/admin` endpoint demands the `Admin` role, not just any login.
* **Separation of identities:** customer tokens don't work on admin endpoints and admin tokens don't work on customer endpoints.
* **Tenant isolation (IDOR):** carts, addresses and orders are always scoped to the signed-in user (tests for each); an order
  belonging to someone else is a 404.
* **Forged tokens:** unsigned (`alg: none`), wrongly signed and garbage tokens are rejected; with no Entra settings the admin API
  fails *closed*.
* **The development sign-in bypass** exists only in the `Development` environment with an explicit flag, and a test proves it is
  inert in `Production` even if the flag is set.
* **Injection:** all data access is through EF Core parameters; the one hand-written SQL (the grant tool) is parameterized,
  `QUOTENAME`d, and rejects hostile names. Injection strings sent to every search/filter/path input are inert.
* **Cross-site scripting:** no `v-html`/`innerHTML`/`eval` anywhere; hostile HTML in product names and search terms renders as text.
* **Open redirects:** `?redirect=` accepts only same-site paths (`javascript:`, `//host` and absolute URLs are ignored).
* **CORS** allows only configured origins, with no credentials.
* **Secrets:** none in the repository other than clearly local-development defaults (the SQL `sa` password for the compose stack); logs
  contain no emails, tokens or reset codes outside Development (asserted by a test).
* **Money and stock integrity:** checkout is one transaction (order, reservation, cleared cart, outbox row); stock updates use
  row-version concurrency; billing/fulfillment/checkout are idempotent; order changes by admins, billing and fulfillment cannot
  silently overwrite each other.
* **Supply chain:** CI fails on known-vulnerable NuGet/npm packages and on fixable HIGH/CRITICAL image vulnerabilities; images are built
  inside the registry with the commit SHA as the tag.
