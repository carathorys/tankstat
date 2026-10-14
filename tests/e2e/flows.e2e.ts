import { readFileSync, statSync } from 'node:fs'
import { expect, test } from '@playwright/test'
import { ADMIN, addVehicle, pageLoaded, signIn } from './support/app.ts'
import { violations } from './support/axe.ts'
import { expectSoundLayout } from './support/layout.ts'

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

test('writes a backup from the Backups page and downloads it', async ({ page }) => {
  await signIn(page)
  // A vehicle of its own, so the backup has something in it whatever ran before.
  await addVehicle(page)
  await page.goto('/admin/backups')
  await pageLoaded(page)
  await page.getByRole('button', { name: 'Back up now' }).click()
  const written = page.getByText(/^The last backup was written/)
  await expect(written).toBeVisible({ timeout: 30_000 })

  // "The last backup was written <time>: <file>, <size>." The other size's run writes one too, so this one is found by its file.
  const file = /: (\S+), [^,]+\.$/.exec((await written.textContent()) ?? '')?.[1]
  expect(file, 'the file named in the last backup line').toBeTruthy()
  const escaped = file!.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: new RegExp(`^Download the backup of .*\\(${escaped}\\)$`) }).click()
  expect((await download).suggestedFilename()).toBe(file)
  expect(statSync(await (await download).path()).size).toBeGreaterThan(0)

  // Its restore dialog says what it brings of the server's settings: the variables its environment set, none of them missing here.
  await page.getByRole('button', { name: new RegExp(`^Restore the backup of .*\\(${escaped}\\)$`) }).click()
  const restore = page.getByRole('alertdialog', { name: /^Restore the backup of/ })
  await expect(restore).toContainText("Its server's environment set")
  await expect(restore).toContainText('Auth__Oidc__Authority')
  // Never refused here: this server sets all of it. Whether it has a risk to accept depends on whether a test saved settings before.
  await expect(restore).not.toContainText('The restore will be refused')
  expect(await violations(page, '[role="alertdialog"]')).toEqual([])
  await restore.getByRole('button', { name: 'Cancel' }).click()
  await expect(restore).toBeHidden()
})

test('says what an older backup can break in its row, and its restore waits until that is accepted', async ({ page }) => {
  await signIn(page)
  await page.goto('/admin/backups')
  await pageLoaded(page)
  // An older release's backup, which every release restores (the upgrade tests' own): no settings file, nothing of its environment.
  const chooser = page.waitForEvent('filechooser')
  await page.getByRole('button', { name: 'Upload a backup…' }).click()
  await (await chooser).setFiles({ name: 'older.zip', mimeType: 'application/zip', buffer: readFileSync('tests/backend/contracts/backups/older-migration-AddExpenses.zip') })
  const uploaded = page.getByText(/^Uploaded as /)
  await expect(uploaded).toBeVisible()
  const file = /^Uploaded as (\S+)\.$/.exec((await uploaded.textContent()) ?? '')?.[1]
  expect(file, 'the name the upload got').toBeTruthy()

  // Said in its row before anything is opened, in words.
  const row = page.getByRole('row').filter({ hasText: file! })
  await expect(row).toContainText('Risk: No settings file (an older release wrote it)')
  await expectSoundLayout(page)

  await row.getByRole('button', { name: /^Restore the backup of/ }).click()
  const restore = page.getByRole('alertdialog', { name: /^Restore the backup of/ })
  await expect(restore).toContainText('What this restore can break here')
  const go = restore.getByRole('button', { name: 'Restore and restart' })
  await expect(go).toBeDisabled()
  await restore.getByRole('checkbox', { name: /^I understand that the restored server may not work as before/ }).check()
  await expect(go).toBeEnabled()
  expect(await violations(page, '[role="alertdialog"]')).toEqual([])
  await restore.getByRole('button', { name: 'Cancel' }).click() // the shared server is never restored
  await expect(restore).toBeHidden()
})

