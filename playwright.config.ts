import { defineConfig, devices } from '@playwright/test'

// The real-browser tests (tests/e2e, `mise run test:e2e`): the published build, started by scripts/test-e2e.sh, at phone and desktop
// size. A failure keeps its trace, screenshot and video in test-results/e2e (not test-results itself, which Playwright empties at every
// start and other tools write into).
export default defineConfig({
  testDir: 'tests/e2e',
  testMatch: '**/*.e2e.ts',
  outputDir: 'test-results/e2e',
  // Once before the tests: a user per size with a password shorter than the minimum the shared server then runs with.
  globalSetup: './tests/e2e/support/global-setup.ts',
  // Every test makes its own data (tests/e2e/support/app.ts), so the tests of a file run side by side too, and CI's shards split by test.
  fullyParallel: true,
  // A flaky test is fixed, not retried.
  retries: 0,
  forbidOnly: !!process.env.CI,
  // CI's runner takes Playwright's default; two here, as the machine's memory allows (like Vitest's worker cap).
  workers: process.env.CI ? undefined : 2,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.E2E_URL ?? 'http://127.0.0.1:5097',
    colorScheme: 'dark',
    locale: 'en-US',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    // A video of every test, kept only when it fails (test-results/e2e/<test>/video.webm, also in the HTML report and CI's e2e-results).
    video: 'retain-on-failure',
  },
  projects: [
    { name: 'phone', use: { ...devices['Pixel 7'] } },
    { name: 'desktop', use: { viewport: { width: 1280, height: 900 } } },
  ],
})
