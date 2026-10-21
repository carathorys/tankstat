import { defineConfig, devices } from '@playwright/test'

// The README's screenshots (`mise run screenshots`): the published build with the seeder's data and sign-in off, started by
// scripts/screenshots.sh, captured by e2e/screenshots.ts into docs/screenshots at phone and desktop size. Not a test: no layout check,
// no retries, and nothing in the main config's `*.e2e.ts` match picks the file up.
const baseURL = process.env.E2E_URL ?? 'http://127.0.0.1:5110'

export default defineConfig({
  testDir: 'e2e',
  testMatch: 'screenshots.ts',
  outputDir: 'test-results/screenshots',
  fullyParallel: true,
  retries: 0,
  workers: 2,
  reporter: [['list']],
  use: {
    baseURL,
    locale: 'en-US',
    timezoneId: 'Europe/Budapest',
    contextOptions: { reducedMotion: 'reduce' },
    // A failed capture keeps what it saw.
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    // Two device pixels per CSS pixel instead of the Pixel 7's 2.625: sharp enough for a README, at a third less bytes.
    { name: 'phone-dark', use: { ...devices['Pixel 7'], deviceScaleFactor: 2 } },
    // Taller than the tests' desktop (900), so the dashboard's Overview charts are whole under its key figures. The dialogs are in the
    // README on a phone only.
    { name: 'desktop-dark', grepInvert: /@phone-only/, use: { viewport: { width: 1280, height: 1024 } } },
    // The app is dark unless the browser keeps another choice (the anonymous user has no account to keep one); only the pages tagged
    // `@light`, the rest would only double the files.
    {
      name: 'desktop-light',
      grep: /@light/,
      use: {
        viewport: { width: 1280, height: 1024 },
        storageState: { cookies: [], origins: [{ origin: baseURL, localStorage: [{ name: 'tankstat.colorMode', value: 'light' }] }] },
      },
    },
  ],
})
