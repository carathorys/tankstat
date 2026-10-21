import { defineConfig, devices } from '@playwright/test'

// The README's screenshots (`mise run screenshots`): the published build with the seeder's data and sign-in off, started by
// scripts/screenshots.sh, captured by e2e/screenshots.ts into docs/screenshots at phone and desktop size. Not a test: no layout check,
// no retries, and nothing in the main config's `*.e2e.ts` match picks the file up.
export default defineConfig({
  testDir: 'e2e',
  testMatch: 'screenshots.ts',
  outputDir: 'test-results/screenshots',
  fullyParallel: true,
  retries: 0,
  workers: 2,
  reporter: [['list']],
  use: {
    baseURL: process.env.E2E_URL ?? 'http://127.0.0.1:5098',
    locale: 'en-US',
    timezoneId: 'Europe/Budapest',
  },
  projects: [
    // Two device pixels per CSS pixel instead of the Pixel 7's 2.625: sharp enough for a README, at a third less bytes.
    { name: 'phone-dark', use: { ...devices['Pixel 7'], deviceScaleFactor: 2, colorScheme: 'dark' } },
    // Taller than the tests' desktop (900), so the dashboard's Overview charts are whole under its key figures.
    { name: 'desktop-dark', use: { viewport: { width: 1280, height: 1024 }, colorScheme: 'dark' } },
    { name: 'desktop-light', use: { viewport: { width: 1280, height: 1024 }, colorScheme: 'light' } },
  ],
})
