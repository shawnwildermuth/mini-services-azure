# End-to-end tests

Playwright tests that drive the real storefront and admin site in a browser, against the real API, database, queues and
Function App. They cover browsing, cart, sign-in, checkout, payment and shipping (including the failure paths), the
administration workflows, accessibility (WCAG 2.1 A/AA via axe-core), and phone-sized layouts.

## Run them

The suite does not start anything itself. Start the stack with Aspire (`dotnet run` in `apps/Store.AppHost`, wait until every
resource is running in the dashboard), then:

```powershell
cd src/tests/e2e
npm install
npx playwright install chromium                      # or use an installed Edge: $env:PW_CHANNEL = "msedge"
npx playwright test                                  # desktop + phone projects
npx playwright test --project=mobile                 # only the @mobile tests on a phone-sized browser
npx playwright test -g "declined card" --headed      # one test, watching it
npx playwright show-report                           # HTML report; traces are kept for failures
```

The security header/source-map tests apply to the built nginx images, so they are skipped against Aspire's Vite dev servers;
set `E2E_IMAGES=1` when running against the images (`docker compose --profile apps up -d --build`, as CI does).
A start-up check fails fast with instructions if the stack, or the Function App, isn't running.

## How the tests are written

* **Data is created through the API, behaviour is tested through the UI.** Every test makes its own customers, products and
  categories (`support/api.ts`), so tests don't depend on each other, on run order, or on stock left over from earlier runs, and
  can run in parallel.
* **Failure paths use the simulated providers.** A customer email containing `+decline`, `+error` or `+shipfail` makes the
  mock payment provider / carrier decline or go down (see the main README).
* **Waiting is by condition, never by sleeping.** Orders are paid and shipped asynchronously, so tests assert on the page
  updating (`expect(...).toBeVisible({ timeout })`, `expect.poll`). A few deliberate pauses exist only where the test is proving
  that something does *not* change.
* **Dead-letter queues are emptied before and after a run** (`support/cleanup.ts`), because failure tests leave messages there.
* **Accessibility** (`support/a11y.ts`) fails on serious or critical axe violations, in both themes.

## CI

`.github/workflows/e2e.yml` builds the same stack on a runner and runs this suite on pull requests that touch `src/`, nightly,
and on demand. It does not gate deployment.
