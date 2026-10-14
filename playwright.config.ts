import { defineConfig, devices } from '@playwright/test'

// The real-browser tests (tests/e2e, `mise run test:e2e`): the published build, started by scripts/test-e2e.sh, at phone and desktop
// size. A failure keeps its trace and screenshot in test-results/e2e (not test-results itself, which Playwright empties at every start
// and other tools write into).
export default defineConfig({
  testDir: 'tests/e2e',
  testMatch: '**/*.e2e.ts',
  outputDir: 'test-results/e2e',
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
  },
  projects: [
    { name: 'phone', use: { ...devices['Pixel 7'] } },
    { name: 'desktop', use: { viewport: { width: 1280, height: 900 } } },
  ],
})
