import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { navigation } from '../../../src/frontend/navigation.ts'
import { server } from '../support/server.ts'
import { fakeInstance, fakeVehicleBackend, gqlError, healthHandler, renderWithApollo, user } from '../support/mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.restoreAllMocks()
})
afterAll(() => server.close())

const RECIPIENT = 'age1qyqszqgpqyqszqgpqyqszqgpqyqszqgpqyqszqgpqyqszqgpqyqs3290gq'

const point = (o: Record<string, unknown> = {}) => ({
  __typename: 'RestorePointInfo',
  file: 'tankstat-backup-20261010T020000Z.zip',
  bytes: 2_400_000,
  createdAt: '2026-10-10T02:00:00Z',
  appVersion: '0.13.0',
  provider: 'Sqlite',
  migration: 'AddInstance',
  sameMigration: true,
  safetyNet: false,
  uploaded: false,
  encrypted: true,
  recipient: RECIPIENT,
  currentRecipient: true,
  photos: 'SEPARATE',
  keys: true,
  rows: 1234,
  ...o,
})

interface Run {
  __typename: 'BackupRunInfo'
  running: boolean
  startedAt: string | null
  finishedAt: string | null
  file: string | null
  bytes: number | null
  error: string | null
}

const idle: Run = { __typename: 'BackupRunInfo', running: false, startedAt: null, finishedAt: null, file: null, bytes: null, error: null }

/** The password field of a dialog: the radios that choose between passphrase and identity carry those words too. */
const secretField = (dialog: HTMLElement, label: RegExp) => within(dialog).getByLabelText(label, { selector: 'input[type="password"]' })

/** The server's side of the backups tab: the overview, a run, an upload, a key set and a restore, each call recorded. */
function backupsServer({ points = [point()], run = idle, restoreAnswer }: { points?: unknown[]; run?: Run; restoreAnswer?: () => Response } = {}) {
  const calls: Record<string, unknown[]> = {}
  const record = (name: string, value: unknown) => (calls[name] ??= []).push(value)
  const state = { points, run, generation: 'gen-1', pending: false, polls: [] as unknown[] }
  server.use(
    graphql.query('Session', () =>
      HttpResponse.json({
        data: {
          session: { mode: 'STANDALONE', user: user({ isAdmin: true }), instance: fakeInstance(state.generation) },
          notices: state.pending ? [{ code: 'RESTORE_PENDING', severity: 'WARNING', message: 'A restore is waiting.' }] : [],
        },
      }),
    ),
    healthHandler,
    ...fakeVehicleBackend().handlers,
    graphql.query('Admin', () => HttpResponse.json({ data: { users: [], canSetUserPasswords: false, accessSettings: { defaultLevelForOthers: 'NONE' }, accessGrants: [] } })),
    graphql.query('Backups', () => {
      record('Backups', {})
      return HttpResponse.json({
        data: {
          backups: {
            __typename: 'BackupOverviewInfo',
            folder: '/data/backups',
            migration: 'AddInstance',
            run: state.run,
            settings: { __typename: 'BackupSettingsInfo', recipient: RECIPIENT, photosApart: true, photos: true, maxUploadMb: 1024 },
            sizes: { __typename: 'StorageSizesInfo', database: 5_300_000, pictures: 12_000_000, photos: 340_000_000 },
            restorePoints: state.points,
          },
        },
      })
    }),
    graphql.query('BackupRun', () => {
      record('BackupRun', {})
      return HttpResponse.json({ data: { backups: { __typename: 'BackupOverviewInfo', run: state.run } } })
    }),
    graphql.mutation('RunBackup', () => {
      record('RunBackup', {})
      // Written at once in this fake: the first poll already finds it done.
      state.run = { ...idle, startedAt: '2026-10-11T08:00:00Z', finishedAt: '2026-10-11T08:00:05Z', file: 'tankstat-backup-20261011T080000Z.zip', bytes: 3_100_000 }
      state.points = [point({ file: 'tankstat-backup-20261011T080000Z.zip', createdAt: '2026-10-11T08:00:00Z' }), ...state.points]
      return HttpResponse.json({ data: { runBackup: true } })
    }),
    graphql.mutation('PrepareBackupEncryption', ({ variables }) => {
      record('PrepareBackupEncryption', variables)
      if (typeof variables.passphrase === 'string' && variables.passphrase.length < 12)
        return HttpResponse.json(gqlError('Too short.', 'VALIDATION_FAILED', 'backup.passphraseTooShort', { min: 12 }))
      return HttpResponse.json({
        data: {
          prepareBackupEncryption: {
            __typename: 'BackupKeySetInfo',
            passphrase: variables.passphrase ?? 'K7QM-4XWD-PL2N-ZR8A-HJ3T',
            generated: variables.passphrase === null,
            identity: 'AGE-SECRET-KEY-1QQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQ',
            recipient: RECIPIENT,
            salt: 'c2FsdHNhbHRzYWx0c2FsdA==',
          },
        },
      })
    }),
    graphql.mutation('RestoreBackup', ({ variables }) => {
      record('RestoreBackup', variables.input)
      if (restoreAnswer) return restoreAnswer()
      state.pending = true
      return HttpResponse.json({ data: { restoreBackup: { __typename: 'RestorePendingInfo', file: variables.input.file, restorePoint: '2026-10-10T02:00:00Z' } } })
    }),
    http.put('/media/backups', async ({ request }) => {
      const name = new URL(request.url).searchParams.get('name')
      record('upload', { name, bytes: (await request.arrayBuffer()).byteLength })
      if (name === 'huge.zip') return HttpResponse.json({ key: 'backup.tooLarge', args: { maxMb: 1024 }, message: 'Too large.' }, { status: 413 })
      return HttpResponse.json({ file: `uploaded-${name}`, bytes: 3 })
    }),
  )
  return { calls, state }
}

async function openBackups() {
  const ui = userEvent.setup()
  renderWithApollo(<App />, '/admin')
  await ui.click(await screen.findByRole('tab', { name: 'Backups' }))
  await screen.findByRole('heading', { name: 'Backups in the folder' })
  return ui
}

it('shows the server, where its backups go, and every backup in the folder with what it holds', async () => {
  backupsServer({
    points: [
      point(),
      point({ file: 'uploaded-old-server.zip', createdAt: '2026-09-01T10:00:00Z', uploaded: true, encrypted: false, recipient: null, currentRecipient: false, photos: 'LEFT_OUT', keys: false }),
      point({ file: 'tankstat-backup-20260101T000000Z.zip', createdAt: '2026-01-01T00:00:00Z', sameMigration: false, migration: 'AddRecurring', appVersion: '0.9.0' }),
    ],
  })
  await openBackups()

  const overview = within(screen.getByRole('region', { name: 'This server' }))
  expect(overview.getByText('/data/backups')).toBeInTheDocument()
  expect(overview.getByText('5.3 MB · at AddInstance')).toBeInTheDocument()
  expect(overview.getByText('340 MB')).toBeInTheDocument()
  expect(overview.getByText('Backups are encrypted for age1qyqszq…3290gq')).toBeInTheDocument()
  expect(overview.getByText('included')).toBeInTheDocument() // the photos, which have a folder of their own

  const rows = within(screen.getByRole('table')).getAllByRole('row').slice(1)
  expect(rows).toHaveLength(3)
  expect(within(rows[0]).getByText('Tankstat 0.13.0 on Sqlite · 1,234 rows · encrypted for the current key · with the photos')).toBeInTheDocument()
  expect(within(rows[1]).getByText(/uploaded-old-server\.zip · uploaded/)).toBeInTheDocument()
  expect(within(rows[1]).getByText(/not encrypted · without the photos/)).toBeInTheDocument()
  // Made at another version of the database: it cannot be restored here, and the row says with what it can.
  expect(within(rows[2]).queryByRole('button', { name: /^Restore/ })).not.toBeInTheDocument()
  expect(within(rows[2]).getByText(/restore it with the release that wrote it \(Tankstat 0\.9\.0\)/)).toBeInTheDocument()
  expect(within(rows[0]).getByRole('button', { name: /^Restore the backup of/ })).toBeInTheDocument()
})

it('backs up now, follows the run, and lists the new backup when it is written', async () => {
  const { calls } = backupsServer()
  const ui = await openBackups()

  await ui.click(screen.getByRole('button', { name: 'Back up now' }))

  expect(await screen.findByText(/The last backup was written .*: tankstat-backup-20261011T080000Z\.zip, 3\.1 MB\./)).toBeInTheDocument()
  expect(calls.RunBackup).toHaveLength(1)
  expect(calls.BackupRun?.length).toBeGreaterThan(0)
  await waitFor(() => expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(3)) // the overview asked again
})

it('says why the last run failed, in words', async () => {
  backupsServer({ run: { ...idle, startedAt: '2026-10-11T08:00:00Z', finishedAt: '2026-10-11T08:00:01Z', error: 'backup.failed' } })
  await openBackups()

  expect(screen.getByText('The last backup failed: an unexpected error (the server log says more)')).toBeInTheDocument()
})

it('uploads a backup into the folder and lists it; one too large is refused with the reason', async () => {
  const { calls } = backupsServer()
  const ui = await openBackups()
  const input = screen.getByTestId('backup-file')

  await ui.upload(input, new File([new Uint8Array([1, 2, 3])], 'from-old.zip', { type: 'application/zip' }))

  expect(await screen.findByText('Uploaded as uploaded-from-old.zip.')).toBeInTheDocument()
  expect(calls.upload).toEqual([{ name: 'from-old.zip', bytes: 3 }])
  await waitFor(() => expect(calls.Backups?.length).toBeGreaterThan(1))

  await ui.upload(input, new File([new Uint8Array([1])], 'huge.zip', { type: 'application/zip' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('The file is larger than an upload may be (1024 MB); copy it into the backups folder instead.')
})

it('downloads a backup through the browser, after renewing the sign-in', async () => {
  backupsServer()
  const download = vi.spyOn(navigation, 'download').mockImplementation(() => undefined)
  const ui = await openBackups()

  await ui.click(screen.getByRole('button', { name: /^Download the backup of/ }))

  await waitFor(() => expect(download).toHaveBeenCalledWith('/media/backups/tankstat-backup-20261010T020000Z.zip'))
})

it('restores an encrypted backup with its passphrase, waits for the restarted server and reloads once its data is the restored one', async () => {
  const { calls, state } = backupsServer()
  const reload = vi.spyOn(navigation, 'reload').mockImplementation(() => undefined)
  const ui = await openBackups()

  await ui.click(screen.getByRole('button', { name: /^Restore the backup of/ }))
  const dialog = await screen.findByRole('alertdialog', { name: /^Restore the backup of .*\?$/ })
  expect(dialog).toHaveAccessibleDescription(/All data on this server is replaced/)
  await ui.type(secretField(dialog, /^Passphrase/), 'correct horse battery')
  await ui.click(within(dialog).getByRole('button', { name: 'Restore and restart' }))

  expect(await screen.findByText(/the server is restarting\. This page reloads by itself/)).toBeInTheDocument()
  expect(calls.RestoreBackup).toEqual([{ file: 'tankstat-backup-20261010T020000Z.zip', passphrase: 'correct horse battery', identity: null }])
  expect(reload).not.toHaveBeenCalled()

  state.generation = 'gen-2' // the server is back with the restored data
  state.pending = false
  await waitFor(() => expect(reload).toHaveBeenCalled(), { timeout: 5000 })
})

it('opens a backup with its identity instead, and keeps the dialog with the reason when the key does not open it', async () => {
  const { calls } = backupsServer({ restoreAnswer: () => HttpResponse.json(gqlError('Wrong key.', 'VALIDATION_FAILED', 'backup.wrongKey')) })
  const ui = await openBackups()

  await ui.click(screen.getByRole('button', { name: /^Restore the backup of/ }))
  const dialog = await screen.findByRole('alertdialog')
  await ui.click(within(dialog).getByRole('radio', { name: 'Identity' }))
  await ui.type(secretField(dialog, /^Identity/), 'AGE-SECRET-KEY-1ABC ')
  await ui.click(within(dialog).getByRole('button', { name: 'Restore and restart' }))

  expect(await within(dialog).findByRole('alert')).toHaveTextContent('The passphrase (or identity) does not open this backup.')
  expect(calls.RestoreBackup).toEqual([{ file: 'tankstat-backup-20261010T020000Z.zip', passphrase: null, identity: 'AGE-SECRET-KEY-1ABC' }])
  expect(screen.getByRole('alertdialog')).toBeInTheDocument()
})

it('says so when the server comes back without the restore', async () => {
  const { state } = backupsServer()
  vi.spyOn(navigation, 'reload').mockImplementation(() => undefined)
  const ui = await openBackups()

  await ui.click(screen.getByRole('button', { name: /^Restore the backup of/ }))
  const dialog = await screen.findByRole('alertdialog')
  await ui.type(secretField(dialog, /^Passphrase/), 'correct horse battery')
  state.pending = false // refused as it started: nothing waits any more, the data is as it was
  server.use(graphql.mutation('RestoreBackup', () => HttpResponse.json({ data: { restoreBackup: { file: 'x', restorePoint: '2026-10-10T02:00:00Z' } } })))
  await ui.click(within(dialog).getByRole('button', { name: 'Restore and restart' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('The server is back, but the backup was not restored.')
  expect(navigation.reload).not.toHaveBeenCalled()
})

it('makes a key set, shows it once with the two settings to set, and is done only once the passphrase is kept', async () => {
  const { calls } = backupsServer()
  const ui = await openBackups()

  await ui.click(screen.getByRole('button', { name: 'Make a key set…' }))
  const dialog = await screen.findByRole('dialog', { name: 'A key set for encrypted backups' })
  expect(within(dialog).getByText(/Backups are already encrypted/)).toBeInTheDocument()
  await ui.type(within(dialog).getByLabelText(/^Passphrase \(optional\)/), 'short')
  await ui.click(within(dialog).getByRole('button', { name: 'Make the keys' }))
  expect(await within(dialog).findByText('A passphrase has at least 12 characters.')).toBeInTheDocument()

  await ui.clear(within(dialog).getByLabelText(/^Passphrase \(optional\)/))
  await ui.click(within(dialog).getByRole('button', { name: 'Make the keys' }))

  expect(await within(dialog).findByText('K7QM-4XWD-PL2N-ZR8A-HJ3T')).toBeInTheDocument()
  expect(within(dialog).getByText('Passphrase (made up)')).toBeInTheDocument()
  expect(within(dialog).getByText(/Backups__Encryption__Recipient=age1qy.*\s+Backups__Encryption__Salt=c2FsdHNhbHRzYWx0c2FsdA==/)).toBeInTheDocument()
  expect(calls.PrepareBackupEncryption).toEqual([{ passphrase: 'short' }, { passphrase: null }])
  const done = within(dialog).getByRole('button', { name: 'Done' })
  expect(done).toBeDisabled()
  await ui.click(within(dialog).getByRole('checkbox', { name: 'I have kept the passphrase in a safe place' }))
  await ui.click(done)
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
})

it('a server without backups says how to make the first one', async () => {
  backupsServer({ points: [] })
  await openBackups()

  expect(screen.getByText('There are no backups in the folder yet. Back up now, upload one, or copy a file into /data/backups.')).toBeInTheDocument()
  expect(screen.queryByRole('table')).not.toBeInTheDocument()
})

it('the backups are only asked for once the tab is opened', async () => {
  const { calls } = backupsServer()
  renderWithApollo(<App />, '/admin')
  await screen.findByRole('tab', { name: 'Backups' })
  expect(calls.Backups).toBeUndefined()
})
