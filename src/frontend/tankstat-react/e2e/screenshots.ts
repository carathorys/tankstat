import { expect, test, type Page } from '@playwright/test'
import { mkdirSync } from 'node:fs'
import { join } from 'node:path'
import { graphql, pageLoaded } from './support/app.ts'

// The README's screenshots, one file per page and project (`home-desktop-dark.png`), see playwright.screenshots.config.ts. The server
// runs the seeder's data with sign-in off, so nothing signs in and the first vehicle by name is the one shown.
const folder = join(import.meta.dirname, '../../../../docs/screenshots')
mkdirSync(folder, { recursive: true })

/**
 * Waits until the page stops changing (the charts and the cards animate in), then saves the viewport. The notice that sign-in is off
 * (the server's, not the app's) and a toast (the service worker's "can now be used offline") are taken out first: neither is the page.
 */
async function capture(page: Page, name: string) {
  // Before every shot: the toast arrives on its own time (the service worker's), after the page has loaded.
  const clean = () =>
    page.evaluate(() => {
      for (const el of document.querySelectorAll('[role="note"][data-severity], .MuiSnackbar-root')) el.remove()
    })
  await clean()
  let last = await page.screenshot()
  await expect
    .poll(
      async () => {
        await clean()
        const next = await page.screenshot()
        const same = next.equals(last)
        last = next
        return same
      },
      { intervals: [400], timeout: 15_000 },
    )
    .toBe(true)
  await page.screenshot({ path: join(folder, `${name}-${test.info().project.name}.png`) })
}

async function firstVehicle(page: Page) {
  const data = await graphql<{ myVehicles: { id: string; name: string }[] }>(page, 'query { myVehicles(take: 1) { id name } }')
  expect(data.myVehicles, 'the seeded vehicles').not.toHaveLength(0)
  return data.myVehicles[0]
}

test.beforeEach(async ({ page, context }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  // The app is dark unless the browser (or the account, which the anonymous user has none of) keeps another choice.
  if (test.info().project.name.endsWith('-light')) {
    await context.addInitScript(() => localStorage.setItem('tankstat.colorMode', 'light'))
  }
})

// The light scheme: the two pages the README shows in it; the rest would only double the files.
const darkOnly = () => test.skip(test.info().project.name.endsWith('-light'), 'dark only')

test('home', async ({ page }) => {
  const vehicle = await firstVehicle(page)
  await page.goto('/')
  await pageLoaded(page)
  await page.getByRole('link', { name: vehicle.name }).first().waitFor()
  await capture(page, 'home')
})

test('dashboard', async ({ page }) => {
  const vehicle = await firstVehicle(page)
  await page.goto(`/vehicles/${vehicle.id}`)
  await pageLoaded(page)
  await expect(page.locator('figure svg').first()).toBeVisible()
  await capture(page, 'dashboard')
})

test('refuelings', async ({ page }) => {
  darkOnly()
  const vehicle = await firstVehicle(page)
  await page.goto(`/vehicles/${vehicle.id}?tab=refuelings`)
  await pageLoaded(page)
  await expect(page.getByRole('row').nth(2)).toBeVisible()
  await capture(page, 'refuelings')
})

test('add refuelling', async ({ page }) => {
  darkOnly()
  const vehicle = await firstVehicle(page)
  await page.goto(`/vehicles/${vehicle.id}?tab=refuelings`)
  await pageLoaded(page)
  await page.getByRole('button', { name: 'Add refuelling' }).first().click()
  await expect(page.getByRole('dialog', { name: 'Add refuelling' })).toBeVisible()
  await capture(page, 'add-refuelling')
})

test('recurring', async ({ page }) => {
  darkOnly()
  const vehicle = await firstVehicle(page)
  await page.goto(`/vehicles/${vehicle.id}?tab=recurring`)
  await pageLoaded(page)
  await expect(page.getByRole('tab', { name: 'Recurring', exact: false })).toHaveAttribute('aria-selected', 'true')
  await capture(page, 'recurring')
})

test('mark as done', async ({ page }) => {
  darkOnly()
  const vehicle = await firstVehicle(page)
  await page.goto(`/vehicles/${vehicle.id}?tab=recurring`)
  await pageLoaded(page)
  // The first schedule's Done button (not the disabled "Mark 0 selected as done" above the table): the dialog ticks it and every
  // other due one of its category.
  await page.getByRole('button', { name: /^Mark .* as done$/, disabled: false }).first().click()
  await expect(page.getByRole('dialog', { name: /^Mark as done/ })).toBeVisible()
  await capture(page, 'mark-as-done')
})
