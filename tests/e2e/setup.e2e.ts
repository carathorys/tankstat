import { expect, request, test, type APIRequestContext, type Page } from '@playwright/test'
import { ADMIN, addVehicle, graphql, pageLoaded, signIn, tag } from './support/app.ts'
import { violations } from './support/axe.ts'
import { expectSoundLayout } from './support/layout.ts'
import { waitingServer, type WaitingServer } from './support/setup.ts'

// The setup of a new instance (#90), at both sizes. Each test takes over a server of its own that waits for its setup (scripts/test-e2e.sh,
// support/setup.ts): finishing the setup, or restoring a backup into it, cannot be undone. Only the restore's backup comes from the shared
// server.

/** Opens a waiting server, where the setup page stands in for every page, and enters its one-time code: the wizard's start. */
async function enterCode(page: Page, server: WaitingServer) {
  await page.goto(server.url)
  await expect(page.getByRole('heading', { level: 1, name: 'Set up Tankstat' })).toBeVisible()
  await expectSoundLayout(page)
  expect(await violations(page)).toEqual([])
  await page.getByRole('textbox', { name: /^Setup code/ }).fill(server.code)
  await page.getByRole('button', { name: 'Continue' }).click()
  await expect(page.getByRole('button', { name: 'Start the wizard' })).toBeVisible()
  await expectSoundLayout(page)
}

/** The wizard's step at hand, by its label (the stepper's current step). */
const stepAt = (page: Page, name: string) => expect(page.locator('[aria-current="step"]')).toHaveText(name)

/** Signs in with the form, as a person does after the restart a new sign-in mode (or a restore) brings. */
async function signInWithTheForm(page: Page, user: { email: string; password: string }) {
  // The restart takes a few seconds, and the page waits for it before it asks.
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible({ timeout: 60_000 })
  await page.getByLabel('E-mail').fill(user.email)
  await page.getByLabel('Password').fill(user.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
}

test('a new instance is set up through the wizard: the sign-in mode, the first administrator, one restart, then the done screen', async ({
  page,
}) => {
  await enterCode(page, waitingServer('fresh'))
  await page.getByRole('button', { name: 'Start the wizard' }).click()
  await stepAt(page, 'Sign-in')

  // The mode must be chosen (none is preselected): said at its field.
  await page.getByRole('button', { name: 'Next' }).click()
  await expect(page.getByText('Choose one to go on.')).toBeVisible()
  await expectSoundLayout(page)
  expect(await violations(page)).toEqual([])
  await page.getByRole('combobox', { name: /^Sign-in mode/ }).click()
  await page.getByRole('option', { name: 'Local accounts (Standalone)' }).click()
  await page.getByRole('button', { name: 'Next' }).click()
  // Saved with the code; local accounts bring the e-mail's step. Every step opens by its label: straight on to the last one.
  await stepAt(page, 'E-mail')
  await page.getByRole('button', { name: 'Finish', exact: true }).click()
  await stepAt(page, 'Finish')

  const last = page.getByRole('region', { name: 'Finish' })
  const admin = { email: `admin-${tag()}@example.com`, password: `password-${tag()}` }
  await last.getByRole('textbox', { name: /^E-mail/ }).fill(admin.email)
  await last.getByLabel(/^Password/).fill(admin.password)
  await expectSoundLayout(page)
  await last.getByRole('button', { name: 'Create the administrator' }).click()

  // Sign-in off ran, so the server restarts into local accounts, which signs everyone out: the new administrator signs in and finds
  // what to set up next.
  await signInWithTheForm(page, admin)
  await expect(page.getByText("The setup is done: you are this instance's administrator.")).toBeVisible()
  await expect(page.getByRole('link', { name: 'People: add the other users' })).toBeVisible()
  await expectSoundLayout(page)
})

type BackupRun = { running: boolean; startedAt: string | null; file: string | null; error: string | null }

/**
 * A backup of the shared server that holds a vehicle made for it, as its file: the backup the restore test moves to its new server. The
 * other tests write backups too, one at a time, so a run that started after the vehicle was saved is waited for (and asked for again
 * while the last one is older).
 */
async function backupOfTheSharedServer(api: APIRequestContext) {
  await signIn(api)
  const vehicle = await addVehicle(api, `Moved ${tag()}`)
  const since = Date.now()
  let run: BackupRun | undefined
  await expect
    .poll(
      async () => {
        run = (await graphql<{ backups: { run: BackupRun } }>(api, '{ backups { run { running startedAt file error } } }')).backups.run
        if (run.running) return 'running'
        if (run.startedAt && Date.parse(run.startedAt) >= since) return run.error ?? 'written'
        await graphql(api, 'mutation { runBackup }')
        return 'asked'
      },
      { timeout: 60_000, intervals: [500] },
    )
    .toBe('written')
  const download = await api.get(`/media/backups/${encodeURIComponent(run!.file!)}`)
  expect(download.ok()).toBe(true)
  return { vehicle, file: run!.file!, body: await download.body() }
}

test('a new instance is restored from a backup uploaded on the setup page, and its data is back', async ({ page }) => {
  const api = await request.newContext({ baseURL: test.info().project.use.baseURL })
  const backup = await backupOfTheSharedServer(api).finally(() => api.dispose())

  await enterCode(page, waitingServer('restore'))
  await page.getByRole('button', { name: 'Restore from a backup' }).click()
  await stepAt(page, 'Restore a backup')
  const restore = page.getByRole('region', { name: 'Restore a backup' })
  await expect(restore).toContainText('There is no backup in the server')

  // A backup of the old server, from this device: uploaded with the code, then listed to restore.
  const chooser = page.waitForEvent('filechooser')
  await restore.getByRole('button', { name: 'Upload a backup…' }).click()
  await (await chooser).setFiles({ name: backup.file, mimeType: 'application/zip', buffer: backup.body })
  const point = restore.getByRole('button', { name: /^Restore the backup of .*\(uploaded-[^)]+\.zip\)$/ })
  await expect(point).toBeVisible({ timeout: 30_000 })
  await expectSoundLayout(page)
  expect(await violations(page)).toEqual([])

  await point.click()
  const dialog = page.getByRole('alertdialog', { name: /^Restore the backup of/ })
  await dialog.getByRole('button', { name: 'Restore and restart' }).click()

  // The server restarts and restores the backup as it starts; the page reloads once it serves the restored data, which brings its own
  // administrator, and the vehicle made for the backup is there.
  await signInWithTheForm(page, ADMIN)
  await pageLoaded(page)
  await page.getByLabel('Search vehicles').fill(backup.vehicle.name)
  await expect(page.getByRole('link', { name: backup.vehicle.name })).toBeVisible()
})
