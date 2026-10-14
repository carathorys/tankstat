import { expect, test } from '@playwright/test'
import { addVehicle, graphql, logRefueling, openVehicle, pageLoaded, signInAsNewUser } from './support/app.ts'

// Working without the server: the device downloads a vehicle, the browser goes offline, a refuelling is kept on the device, and it
// reaches the server once the browser is back online. Only in-app navigation while offline (the address bar would need the server).

test('keeps a refuelling made offline and sends it when the server is back', async ({ page, context }) => {
  await signInAsNewUser(page)
  const vehicle = await addVehicle(page)
  const today = new Date().toISOString().slice(0, 10)
  await logRefueling(page, vehicle.id, { date: today, volume: 35, odometer: 1000 })

  // Download what the device keeps, now rather than whenever the app gets to it.
  await page.goto('/account')
  await pageLoaded(page)
  await page.getByRole('button', { name: 'Download now' }).click()
  await expect(page.getByText('Downloaded.')).toBeVisible({ timeout: 30_000 })

  await openVehicle(page, vehicle.id, 'refuelings')
  await expect(page.getByRole('row').filter({ hasText: '35 L' })).toBeVisible()

  await context.setOffline(true)
  await page.getByRole('button', { name: 'Add refuelling' }).click()
  const dialog = page.getByRole('dialog', { name: 'Add refuelling' })
  await dialog.getByLabel(/^Volume/).fill('22.5')
  await dialog.getByLabel('Total cost').fill('13500')
  await dialog.getByLabel(/^Odometer/).fill('1400')
  await dialog.getByRole('button', { name: 'Add refuelling' }).click()
  await expect(dialog).toBeHidden()
  await expect(page.getByText('Saved on this device · syncs when online.')).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: '22.5 L' })).toBeVisible()

  await context.setOffline(false)
  // Once the server answers again the device sends what it kept: the server then has the refuelling.
  await expect
    .poll(
      async () => {
        const data = await graphql<{ refuelings: { volume: number }[] }>(
          page,
          'query($v: UUID!) { refuelings(vehicleId: $v) { volume } }',
          { v: vehicle.id },
        )
        return data.refuelings.map((r) => r.volume).sort((a, b) => a - b)
      },
      { timeout: 60_000 },
    )
    .toEqual([22.5, 35])
})
