import { expect, test } from '@playwright/test'
import { ADMIN, addVehicle, pageLoaded, signIn } from './support/app.ts'

// A few flows across the real server, its sign-in and the routing, at both sizes.

test('signs in with the form and lands on the home page', async ({ page }) => {
  await page.goto('/')
  await page.getByLabel('E-mail').fill(ADMIN.email)
  await page.getByLabel('Password').fill(ADMIN.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await pageLoaded(page)
  await expect(page.getByRole('button', { name: 'Notifications' })).toBeVisible()
})

test('adds a vehicle and a refuelling', async ({ page, isMobile }) => {
  await signIn(page)
  const name = `Flow ${crypto.randomUUID().slice(0, 8)}`
  await page.goto('/')
  await pageLoaded(page)
  await page.getByRole('button', { name: 'Add vehicle' }).first().click()
  const vehicleDialog = page.getByRole('dialog', { name: 'Add vehicle' })
  await vehicleDialog.getByLabel('Name').fill(name)
  await vehicleDialog.getByRole('button', { name: 'Add vehicle' }).click()
  await expect(vehicleDialog).toBeHidden()

  // Found by its name: the home page lists a page of vehicles by name, and the other tests' vehicles may fill it. On a touch screen the
  // first tap on a card shows its figures ("Tap again to open"), the second opens the vehicle.
  await page.getByLabel('Search vehicles').fill(name)
  const card = page.getByRole('link', { name })
  await card.click()
  if (isMobile) await card.click()
  await expect(page.getByRole('heading', { level: 1, name })).toBeVisible()
  await page.getByRole('tab', { name: 'Refuelings' }).click()
  await page.getByRole('button', { name: 'Add refuelling' }).first().click()
  const logDialog = page.getByRole('dialog', { name: 'Add refuelling' })
  await logDialog.getByLabel(/^Volume/).fill('42.5')
  await logDialog.getByLabel('Total cost').fill('25000')
  await logDialog.getByLabel(/^Odometer/).fill('12345')
  await logDialog.getByRole('button', { name: 'Add refuelling' }).click()
  await expect(logDialog).toBeHidden()
  // The new row in the grid (the odometer column is hidden on a phone; the volume shows at both sizes).
  await expect(page.getByRole('row').filter({ hasText: '42.5 L' })).toBeVisible()
})

test('writes a backup from the Backups page', async ({ page }) => {
  await signIn(page)
  // A vehicle of its own, so the backup has something in it whatever ran before.
  await addVehicle(page)
  await page.goto('/admin/backups')
  await pageLoaded(page)
  await page.getByRole('button', { name: 'Back up now' }).click()
  await expect(page.getByText(/^The last backup was written/)).toBeVisible({ timeout: 30_000 })
})
