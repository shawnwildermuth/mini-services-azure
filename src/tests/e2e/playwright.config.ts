import { defineConfig, devices } from '@playwright/test'
import { urls } from './support/urls'

// The suite drives the real applications; it does not start them. Bring the stack up first (see README.md):
//   dotnet run --project src/apps/Store.AppHost   (Aspire: infrastructure, API, Functions, both sites)
// or the built images, as CI does (with E2E_IMAGES=1):  docker compose --profile apps up -d --build  and  func start
//
// PW_CHANNEL=msedge uses an installed Microsoft Edge (no browser download). Leave it unset to use Playwright's Chromium
// (`npx playwright install chromium`), as CI does.
const channel = process.env.PW_CHANNEL || undefined

export default defineConfig({
  testDir: './tests',
  globalSetup: './global-setup.ts',
  globalTeardown: './global-teardown.ts',
  timeout: 60_000, // orders pass through two queues and two functions; allow for slow machines
  expect: { timeout: 10_000 },
  fullyParallel: true,
  workers: process.env.CI ? 2 : 4,
  retries: process.env.CI ? 1 : 0,
  forbidOnly: !!process.env.CI,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: urls.store,
    channel,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
  },
  projects: [
    { name: 'desktop', grepInvert: /@mobile/, use: { ...devices['Desktop Chrome'], channel } },
    // A phone-sized browser runs only the tests tagged @mobile.
    { name: 'mobile', grep: /@mobile/, use: { ...devices['Pixel 7'], channel } },
  ],
})
