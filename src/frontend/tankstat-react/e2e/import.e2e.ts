import type { Page } from '@playwright/test'
import { expect, test } from './support/test.ts'
import { pageLoaded, signInAsNewUser } from './support/app.ts'
import { expectSoundLayout } from './support/layout.ts'
import { importServer } from './support/setup.ts'

// Importing another app's export (Fuelio): the file, where it goes, what will be imported, and the vehicle it made.

test('imports a Fuelio export into a new vehicle', async ({ page }) => {
  await signInAsNewUser(page)
  await page.goto('/import')
  await pageLoaded(page)
  const chooser = page.waitForEvent('filechooser')
  await page.getByRole('button', { name: 'Choose file' }).click()
  await (await chooser).setFiles('e2e/files/fuelio.csv')
  await page.getByRole('button', { name: 'Read the file' }).click()

  await page.getByRole('radio', { name: 'A new vehicle' }).check()
  await expect(page.getByRole('group', { name: 'The new vehicle' }).getByLabel('Name')).toHaveValue('Polo')
  await page.getByRole('button', { name: 'What will be imported' }).click()
  await expect(page.getByText('3 fuel logs, 1 expenses and 0 recurring expenses, from Jun 1, 2026 to Sep 17, 2026.')).toBeVisible()
  await page.getByRole('button', { name: 'Import', exact: true }).click()
  await expect(page.getByRole('status').filter({ hasText: 'Imported 3 fuel logs, 1 expenses and 0 recurring expenses.' })).toBeVisible()

  await page.getByRole('link', { name: 'Open the vehicle' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Polo' })).toBeVisible()
  await page.getByRole('tab', { name: 'Refuelings' }).click()
  await expect(page.getByRole('row').filter({ hasText: /38\.5/ })).toBeVisible()
})

// Importing cars from a Tankstat backup (#337): the file, the cars, the review, the run and its report; and a run cancelled while it goes.
// A server of this size's own (sign-in off), one test after the other: a run holds the database's lock, and one person has one run.
test.describe('from a Tankstat backup', () => {
  test.describe.configure({ mode: 'serial' })

  const api = async <T>(page: Page, url: string, query: string, variables?: Record<string, unknown>) => {
    const response = await page.request.post(`${url}/graphql`, { data: { query, variables } })
    const body = (await response.json()) as { data?: T; errors?: { message: string }[] }
    expect(body.errors ?? [], `GraphQL errors for ${query}`).toEqual([])
    return body.data as T
  }
  const cars = async (page: Page, url: string) => (await api<{ myVehicles: { id: string; name: string; refuelingCount: number }[] }>(page, url, '{ myVehicles { id name refuelingCount } }')).myVehicles

  /** The first step, to the opened file. */
  async function openTheBackup(page: Page, server: { url: string; backup: string }) {
    await page.goto(`${server.url}/import`)
    await pageLoaded(page)
    await page.getByRole('combobox', { name: 'Source' }).click()
    await page.getByRole('option', { name: 'Tankstat backup or export' }).click()
    const chooser = page.waitForEvent('filechooser')
    await page.getByRole('button', { name: 'Choose file' }).click()
    await (await chooser).setFiles(server.backup)
    await page.getByRole('button', { name: 'Read the file' }).click()
    await expect(page.getByRole('status').filter({ hasText: /written by Tankstat .*: 2 cars\./ })).toBeVisible({ timeout: 30_000 })
    await expectSoundLayout(page)
    await page.getByRole('button', { name: 'Next' }).click()
    await expect(page.getByText(/Pick the ones to import; what you import becomes yours\./)).toBeVisible()
  }

  test('imports one car as a new car and one into an own car, and reports what came in', async ({ page }) => {
    const server = importServer()
    // An own car the backup's Fabia goes into: it has the Fabia's fill-up already.
    const own = (await api<{ addVehicle: { id: string } }>(page, server.url, 'mutation { addVehicle(input: { name: "My Fabia", fuelType: PETROL }) { id } }')).addVehicle.id
    await api(page, server.url, 'mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }', {
      i: { vehicleId: own, date: '2025-05-01', volume: 30, totalCost: 45, currency: 'EUR', odometer: 55000, isFullTank: true },
    })
    await openTheBackup(page, server)

    const octavia = page.getByRole('region', { name: 'Octavia · IMP-001' })
    await expect(octavia.getByText('240 refuellings · 1 expenses · 1 recurring expenses')).toBeVisible()
    await octavia.getByRole('checkbox', { name: 'Octavia · IMP-001' }).check()
    await expect(octavia.getByRole('checkbox', { name: 'As a new car' })).toBeChecked() // nothing here matches it
    const fabia = page.getByRole('region', { name: 'Fabia' })
    await fabia.getByRole('checkbox', { name: 'Fabia', exact: true }).check()
    await fabia.getByRole('checkbox', { name: 'As a new car' }).uncheck()
    await fabia.getByRole('radio', { name: 'My Fabia' }).check()
    await expectSoundLayout(page)
    await page.getByRole('button', { name: 'Next' }).click()

    await expect(page.getByRole('region', { name: 'Octavia · a new car' }).getByText('240 refuellings, 1 expenses and 1 recurring expenses are new.')).toBeVisible()
    const added = page.getByRole('region', { name: 'Fabia · added to your car My Fabia' })
    await expect(added.getByText('1 refuellings, 0 expenses and 0 recurring expenses already exist and are skipped.')).toBeVisible()
    await expect(added.getByRole('radio', { name: 'Import the rest' })).toBeChecked()
    await expectSoundLayout(page)
    await page.getByRole('button', { name: 'Import', exact: true }).click()

    await expect(page.getByRole('status').filter({ hasText: 'Import finished: 2 cars.' })).toBeVisible({ timeout: 60_000 })
    await expect(page.getByRole('region', { name: 'Octavia' }).getByText('Imported 240 refuellings, 1 expenses and 1 recurring expenses.')).toBeVisible()
    await expect(page.getByRole('region', { name: 'Fabia' }).getByText('Imported 0 refuellings, 0 expenses and 0 recurring expenses.')).toBeVisible()
    await expectSoundLayout(page)
    expect((await cars(page, server.url)).find((c) => c.name === 'My Fabia')?.refuelingCount).toBe(1) // nothing twice

    await page.getByRole('link', { name: 'Open the vehicle: Octavia' }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Octavia' })).toBeVisible()
    await page.getByRole('tab', { name: 'Refuelings' }).click()
    await expect(page.getByRole('row').filter({ hasText: /40/ }).first()).toBeVisible()
  })

  test('a run cancelled while it goes leaves the cars as they were', async ({ page }) => {
    const server = importServer()
    const before = await cars(page, server.url)
    await openTheBackup(page, server)
    const octavia = page.getByRole('region', { name: 'Octavia · IMP-001' })
    await octavia.getByRole('checkbox', { name: 'Octavia · IMP-001' }).check()
    // The first test's Octavia is here by now and is suggested: this one comes as another new car, so everything of it is to be saved.
    await octavia.getByRole('checkbox', { name: 'As a new car' }).check()
    await page.getByRole('button', { name: 'Next' }).click()
    await expect(page.getByRole('region', { name: 'Octavia · a new car' })).toBeVisible()
    await page.getByRole('button', { name: 'Import', exact: true }).click()

    // The run takes a few seconds (a pause before its transaction, then 242 entries): cancelled as soon as it shows.
    await page.getByRole('button', { name: 'Cancel the import' }).click()
    await page.getByRole('alertdialog', { name: 'Cancel the import?' }).getByRole('button', { name: 'Cancel the import' }).click()

    await expect(page.getByRole('alert').filter({ hasText: 'The import was cancelled. Nothing was imported: your cars are as they were.' })).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('button', { name: 'Back to the review' })).toBeVisible()
    await expectSoundLayout(page)
    expect(await cars(page, server.url)).toEqual(before)
  })
})
