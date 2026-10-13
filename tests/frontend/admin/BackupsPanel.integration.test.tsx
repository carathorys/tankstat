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
  olderMigration: false,
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

const WEEK = ['MONDAY', 'TUESDAY', 'WEDNESDAY', 'THURSDAY', 'FRIDAY', 'SATURDAY', 'SUNDAY']

interface Schedule {
  times: string[]
  days: string[]
  timeZone: string
  nextRun: string | null
  keep: number | null
  keepDays: number | null
}

const idle: Run = { __typename: 'BackupRunInfo', running: false, startedAt: null, finishedAt: null, file: null, bytes: null, error: null }

/** The password field of a dialog: the radios that choose between passphrase and identity carry those words too. */
const secretField = (dialog: HTMLElement, label: RegExp) => within(dialog).getByLabelText(label, { selector: 'input[type="password"]' })

/** The server's side of the Backups page: the overview, a run, an upload, a download, a key set and a restore, each call recorded. */
function backupsServer({
  points = [point()],
  run = idle,
  restoreAnswer,
  maxUploadMb = 1024,
  keysHeldByEnvironment = false,
  keysProblems,
  keysAnswered,
}: {
  points?: unknown[]
  run?: Run
  restoreAnswer?: () => Response
  maxUploadMb?: number
  keysHeldByEnvironment?: boolean
  /** The settings the server names when it cannot take the file as a whole (`settings.invalid`). */
  keysProblems?: string[]
  /** Holds the answer to a save of the keys until it settles. */
  keysAnswered?: Promise<unknown>
} = {}) {
  const calls: Record<string, unknown[]> = {}
  const record = (name: string, value: unknown) => (calls[name] ??= []).push(value)
  const state = {
    points,
    run,
    generation: 'gen-1',
    pending: false,
    polls: [] as unknown[],
    gone: [] as string[],
    schedule: { times: [], days: [], timeZone: 'UTC', nextRun: null, keep: null, keepDays: null } as Schedule,
  }
  server.use(
    graphql.query('Session', () => {
      record('Session', { generation: state.generation, pending: state.pending })
      return HttpResponse.json({
        data: {
          session: { mode: 'STANDALONE', user: user({ isAdmin: true }), instance: fakeInstance(state.generation) },
          notices: state.pending ? [{ code: 'RESTORE_PENDING', severity: 'WARNING', message: 'A restore is waiting.' }] : [],
        },
      })
    }),
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
            settings: { __typename: 'BackupSettingsInfo', recipient: RECIPIENT, photosApart: true, photos: true, maxUploadMb, ...state.schedule },
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
    graphql.mutation('DeleteBackup', ({ variables }) => {
      record('DeleteBackup', variables.file)
      if (state.pending) return HttpResponse.json(gqlError('A restore waits.', 'VALIDATION_FAILED', 'backup.restorePending'))
      state.points = state.points.filter((p) => (p as { file: string }).file !== variables.file)
      return HttpResponse.json({ data: { deleteBackup: true } })
    }),
    graphql.mutation('RunBackup', () => {
      record('RunBackup', {})
      // Written at once in this fake: the first poll already finds it done.
      state.run = { ...idle, startedAt: '2026-10-11T08:00:00Z', finishedAt: '2026-10-11T08:00:05Z', file: 'tankstat-backup-20261011T080000Z.zip', bytes: 3_100_000 }
      state.points = [point({ file: 'tankstat-backup-20261011T080000Z.zip', createdAt: '2026-10-11T08:00:00Z' }), ...state.points]
      return HttpResponse.json({ data: { runBackup: true } })
    }),
    graphql.mutation('UpdateSystemSettings', async ({ variables }) => {
      record('UpdateSystemSettings', variables)
      await keysAnswered
      if (keysHeldByEnvironment)
        return HttpResponse.json(gqlError('Set by the environment.', 'VALIDATION_FAILED', 'settings.readOnly', { key: 'Backups:Encryption:Recipient' }))
      if (keysProblems)
        return HttpResponse.json(gqlError('The settings cannot be used as they are.', 'VALIDATION_FAILED', 'settings.invalid', { problems: keysProblems }))
      return HttpResponse.json({
        data: {
          updateSystemSettings: {
            __typename: 'SystemSettingsInfo',
            file: { __typename: 'SettingsFileInfo', path: '/data/settings.json', exists: true, ignored: false, problems: [] },
            restartPending: false,
            groups: [],
          },
        },
      })
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
    // Asked before a download: whether the file is there (a HEAD answer has a status only).
    http.head('/media/backups/:file', ({ params }) => {
      record('head', params.file)
      return new HttpResponse(null, { status: state.gone.includes(String(params.file)) ? 404 : 200 })
    }),
  )
  return { calls, state }
}

async function openBackups() {
  const ui = userEvent.setup()
  renderWithApollo(<App />, '/admin/backups')
  await screen.findByRole('heading', { name: 'Backups in the folder' })
  return ui
}

it('shows the server, where its backups go, and every backup in the folder with what it holds', async () => {
  backupsServer({
    points: [
      point(),
      point({ file: 'uploaded-old-server.zip', createdAt: '2026-09-01T10:00:00Z', uploaded: true, encrypted: false, recipient: null, currentRecipient: false, photos: 'LEFT_OUT', keys: false }),
      point({ file: 'tankstat-backup-20260101T000000Z.zip', createdAt: '2026-01-01T00:00:00Z', sameMigration: false, migration: 'AddSomethingNewer', appVersion: '9.9.0' }),
      point({ file: 'tankstat-backup-20250101T000000Z.zip', createdAt: '2025-01-01T00:00:00Z', sameMigration: false, olderMigration: true, migration: 'AddRecurring', appVersion: '0.9.0' }),
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
  expect(rows).toHaveLength(4)
  // A column per property: the kind, encryption and photos as icons named by what they mean (their tooltip says it too).
  const cells = (row: HTMLElement) => within(row).getAllByRole('cell').map((cell) => cell.textContent)
  expect(cells(rows[0]).slice(0, 5)).toEqual(['', 'Tankstat 0.13.0Sqlite', '1,234', '', ''])
  expect(within(rows[0]).getByRole('img', { name: 'Backup (written by the app or copied into the folder)' })).toBeInTheDocument()
  expect(within(rows[0]).getByRole('img', { name: 'Encrypted for the current key' })).toBeInTheDocument()
  expect(within(rows[0]).getByRole('img', { name: 'With the photos of logs' })).toBeInTheDocument()
  expect(within(rows[1]).getByText('uploaded-old-server.zip')).toBeInTheDocument()
  expect(within(rows[1]).getByRole('img', { name: 'Uploaded on this page' })).toBeInTheDocument()
  expect(within(rows[1]).getByRole('img', { name: 'Not encrypted (holds no sign-in keys)' })).toBeInTheDocument()
  expect(within(rows[1]).getByRole('img', { name: 'Without the photos of logs' })).toBeInTheDocument()
  await userEvent.setup().hover(within(rows[1]).getByRole('img', { name: 'Uploaded on this page' }))
  expect(await screen.findByRole('tooltip')).toHaveTextContent('Uploaded on this page')
  // Made by a newer release: it cannot be restored here, and the row says with what it can.
  expect(within(rows[2]).queryByRole('button', { name: /^Restore/ })).not.toBeInTheDocument()
  expect(within(rows[2]).getByText('Made by a newer release: restore it with Tankstat 9.9.0 or later.')).toBeInTheDocument()
  // Made by an older release: restored and brought up to date.
  expect(within(rows[3]).getByText('Made by an older release: brought up to date as it is restored.')).toBeInTheDocument()
  expect(within(rows[3]).getByRole('button', { name: /^Restore the backup of/ })).toBeInTheDocument()
  expect(within(rows[0]).getByRole('button', { name: /^Restore the backup of/ })).toBeInTheDocument()
  await userEvent.setup().click(within(rows[3]).getByRole('button', { name: /^Restore the backup of/ }))
  expect(await screen.findByRole('alertdialog')).toHaveAccessibleDescription(/made by an older release \(Tankstat 0\.9\.0\): the database is set up as that release left it/)
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

it('downloads a backup through the browser, once the server said it can', async () => {
  const { calls } = backupsServer()
  const download = vi.spyOn(navigation, 'download').mockImplementation(() => undefined)
  const ui = await openBackups()

  await ui.click(screen.getByRole('button', { name: /^Download the backup of .*\(tankstat-backup-20261010T020000Z\.zip\)$/ }))

  await waitFor(() => expect(download).toHaveBeenCalledWith('/media/backups/tankstat-backup-20261010T020000Z.zip'))
  expect(calls.head).toEqual(['tankstat-backup-20261010T020000Z.zip'])
})

it('says on the page when a backup is gone since the list, instead of leaving it for the server answer', async () => {
  const { state } = backupsServer()
  state.gone = ['tankstat-backup-20261010T020000Z.zip'] // removed by hand meanwhile
  const download = vi.spyOn(navigation, 'download').mockImplementation(() => undefined)
  const ui = await openBackups()

  await ui.click(screen.getByRole('button', { name: /^Download the backup of/ }))

  expect(await screen.findByRole('alert')).toHaveTextContent('There is no such backup in the backups folder.')
  expect(download).not.toHaveBeenCalled()
})

it('deletes a backup once asked to confirm, and lists the folder afresh', async () => {
  const kept = point({ file: 'tankstat-backup-20261009T020000Z.zip', createdAt: '2026-10-09T02:00:00Z' })
  const { calls } = backupsServer({ points: [point(), kept] })
  const ui = await openBackups()

  await ui.click(screen.getByRole('button', { name: /^Delete the backup of .*\(tankstat-backup-20261010T020000Z\.zip\)$/ }))
  const confirm = await screen.findByRole('alertdialog', { name: /^Delete the backup of .*\(tankstat-backup-20261010T020000Z\.zip\)\?$/ })
  expect(calls.DeleteBackup).toBeUndefined() // only once confirmed
  await ui.click(within(confirm).getByRole('button', { name: 'Delete' }))

  await waitFor(() => expect(screen.queryByText(/tankstat-backup-20261010T020000Z\.zip/)).not.toBeInTheDocument())
  expect(calls.DeleteBackup).toEqual(['tankstat-backup-20261010T020000Z.zip'])
  expect(screen.getByText(/tankstat-backup-20261009T020000Z\.zip/)).toBeInTheDocument()
})

it('says why a backup cannot be deleted while a restore waits', async () => {
  const { state } = backupsServer()
  state.pending = true
  const ui = await openBackups()

  await ui.click(screen.getByRole('button', { name: /^Delete the backup of/ }))
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Delete' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('A restore waits for the next start; no backup can be deleted until it has run.')
  expect(screen.getByText(/tankstat-backup-20261010T020000Z\.zip/)).toBeInTheDocument()
})

it('refuses a file larger than the server takes before sending anything', async () => {
  const { calls } = backupsServer({ maxUploadMb: 1 })
  const ui = await openBackups()
  const tooLarge = new File([new Uint8Array(1024 * 1024 + 1)], 'big.zip', { type: 'application/zip' })

  await ui.upload(screen.getByTestId('backup-file'), tooLarge)

  expect(await screen.findByRole('alert')).toHaveTextContent('The file is larger than an upload may be (1 MB); copy it into the backups folder instead.')
  expect(calls.upload).toBeUndefined()
})

it('follows a backup under way that an answer of the overview reports, and lists it once it is written', async () => {
  const { calls, state } = backupsServer()
  const ui = await openBackups()
  // Started elsewhere (another tab), or still going when the page is opened again: the overview's next answer says so.
  state.run = { ...idle, running: true, startedAt: '2026-10-11T08:00:00Z' }

  await ui.upload(screen.getByTestId('backup-file'), new File([new Uint8Array([1, 2, 3])], 'from-old.zip', { type: 'application/zip' }))

  await waitFor(() => expect(calls.BackupRun?.length).toBeGreaterThan(0))
  expect(screen.getByRole('button', { name: 'Back up now' })).toBeDisabled()
  state.run = { ...idle, startedAt: '2026-10-11T08:00:00Z', finishedAt: '2026-10-11T08:00:05Z', file: 'tankstat-backup-20261011T080000Z.zip', bytes: 3_100_000 }
  state.points = [point({ file: 'tankstat-backup-20261011T080000Z.zip', createdAt: '2026-10-11T08:00:00Z' }), ...state.points]
  expect(await screen.findByText(/The last backup was written .*: tankstat-backup-20261011T080000Z\.zip/, undefined, { timeout: 5000 })).toBeInTheDocument()
  await waitFor(() => expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(3))
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

it('follows the restart when the restore got no answer, since the server stops right after it answers', async () => {
  const { state } = backupsServer({ restoreAnswer: () => HttpResponse.error() })
  const reload = vi.spyOn(navigation, 'reload').mockImplementation(() => undefined)
  const ui = await openBackups()

  await ui.click(screen.getByRole('button', { name: /^Restore the backup of/ }))
  const dialog = await screen.findByRole('alertdialog')
  await ui.type(secretField(dialog, /^Passphrase/), 'correct horse battery')
  state.pending = true // the request reached the server; its answer did not come back
  await ui.click(within(dialog).getByRole('button', { name: 'Restore and restart' }))

  expect(await screen.findByText(/the server is restarting\. This page reloads by itself/)).toBeInTheDocument()
  state.generation = 'gen-2'
  state.pending = false
  await waitFor(() => expect(reload).toHaveBeenCalled(), { timeout: 5000 })
})

it('takes the generation from before the restore from the server, not from an older answer the page kept', async () => {
  const { calls, state } = backupsServer()
  vi.spyOn(navigation, 'reload').mockImplementation(() => undefined)
  state.generation = 'gen-0'
  const ui = await openBackups()
  state.generation = 'gen-1' // restored elsewhere since the page loaded; the page still holds gen-0

  await ui.click(screen.getByRole('button', { name: /^Restore the backup of/ }))
  const dialog = await screen.findByRole('alertdialog')
  await ui.type(secretField(dialog, /^Passphrase/), 'correct horse battery')
  await ui.click(within(dialog).getByRole('button', { name: 'Restore and restart' }))
  await screen.findByText(/the server is restarting/)
  // The server before the restart has answered, the restore still waiting: gen-1 is the data from before it.
  await waitFor(() => expect(calls.Session).toContainEqual({ generation: 'gen-1', pending: true }))
  state.pending = false // refused as the app started: still gen-1

  expect(await screen.findByRole('alert', {}, { timeout: 5000 })).toHaveTextContent('The server is back, but the backup was not restored.')
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
  expect(calls.UpdateSystemSettings).toBeUndefined() // nothing is saved unless asked
  const done = within(dialog).getByRole('button', { name: 'Done' })
  expect(done).toBeDisabled()
  await ui.keyboard('{Escape}') // shown only once: not dismissed before it is kept
  expect(screen.getByRole('dialog', { name: 'A key set for encrypted backups' })).toBeInTheDocument()
  await ui.click(within(dialog).getByRole('checkbox', { name: 'I have kept the passphrase in a safe place' }))
  await ui.click(done)
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
})

it('saves a new key set into the server settings when asked, and the page then says which key it encrypts for', async () => {
  const { calls } = backupsServer()
  const ui = await openBackups()
  await ui.click(screen.getByRole('button', { name: 'Make a key set…' }))
  const dialog = await screen.findByRole('dialog', { name: 'A key set for encrypted backups' })
  await ui.click(within(dialog).getByRole('button', { name: 'Make the keys' }))
  await within(dialog).findByText('K7QM-4XWD-PL2N-ZR8A-HJ3T')
  const backupsAsked = calls.Backups?.length ?? 0

  await ui.click(within(dialog).getByRole('button', { name: 'Use these keys for new backups' }))

  const inUse = 'Saved on the server: every backup from now on is encrypted with these keys.'
  expect(await within(dialog).findByText(inUse)).toBeInTheDocument()
  await waitFor(() => expect(document.activeElement).toHaveTextContent(inUse)) // the button that had the focus is gone: its outcome has it
  expect(calls.UpdateSystemSettings).toEqual([
    { changes: [{ key: 'Backups:Encryption:Recipient', value: expect.stringMatching(/^age1/) }, { key: 'Backups:Encryption:Salt', value: 'c2FsdHNhbHRzYWx0c2FsdA==' }] },
  ])
  await waitFor(() => expect(calls.Backups!.length).toBeGreaterThan(backupsAsked)) // the page says which key it encrypts for now
  expect(within(dialog).queryByRole('button', { name: 'Use these keys for new backups' })).not.toBeInTheDocument()
})

it('keeps the key set open while its keys are being saved, and names the settings a refused save was about', async () => {
  let answer!: () => void
  const held = new Promise<void>((resolve) => (answer = resolve))
  backupsServer({ keysProblems: ['Smtp:Port must be a whole number.'], keysAnswered: held })
  const ui = await openBackups()
  await ui.click(screen.getByRole('button', { name: 'Make a key set…' }))
  const dialog = await screen.findByRole('dialog', { name: 'A key set for encrypted backups' })
  await ui.click(within(dialog).getByRole('button', { name: 'Make the keys' }))
  await within(dialog).findByText('K7QM-4XWD-PL2N-ZR8A-HJ3T')

  await ui.click(within(dialog).getByRole('button', { name: 'Use these keys for new backups' }))
  await ui.click(within(dialog).getByRole('checkbox', { name: 'I have kept the passphrase in a safe place' }))

  // Closed now, the answer would go unseen.
  expect(within(dialog).getByRole('button', { name: 'Done' })).toBeDisabled()
  await ui.keyboard('{Escape}')
  expect(screen.getByRole('dialog', { name: 'A key set for encrypted backups' })).toBeInTheDocument()

  answer()

  expect(await within(dialog).findByText('The server could not take these values:')).toBeInTheDocument()
  expect(within(dialog).getByText('Smtp:Port must be a whole number.')).toBeInTheDocument()
  expect(within(dialog).getByRole('button', { name: 'Done' })).toBeEnabled()
})

it('says why a key set cannot be saved when the environment holds the settings', async () => {
  backupsServer({ keysHeldByEnvironment: true })
  const ui = await openBackups()
  await ui.click(screen.getByRole('button', { name: 'Make a key set…' }))
  const dialog = await screen.findByRole('dialog', { name: 'A key set for encrypted backups' })
  await ui.click(within(dialog).getByRole('button', { name: 'Make the keys' }))
  await within(dialog).findByText('K7QM-4XWD-PL2N-ZR8A-HJ3T')

  await ui.click(within(dialog).getByRole('button', { name: 'Use these keys for new backups' }))

  expect(await within(dialog).findByText('Backups:Encryption:Recipient is set by the environment, which wins over the settings file.')).toBeInTheDocument()
  expect(within(dialog).getByText(/Backups__Encryption__Recipient=age1/)).toBeInTheDocument() // the other way stays shown
})

it('shows the schedule with the next run and what retention keeps, or that neither is set', async () => {
  const { state } = backupsServer()
  state.schedule = { times: ['02:00'], days: WEEK, timeZone: 'Europe/Budapest', nextRun: '2026-10-12T02:00:00Z', keep: 14, keepDays: 30 }
  await openBackups()

  // The time of day as the server has it (its zone, named, written as the language writes times), the next run in this machine's zone.
  const daily = new Intl.DateTimeFormat('en', { timeStyle: 'short', timeZone: 'UTC' }).format(new Date('2000-01-01T02:00:00Z'))
  const next = new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date('2026-10-12T02:00:00Z'))
  const overview = within(screen.getByRole('region', { name: 'This server' }))
  expect(overview.getByText(`Every day at ${daily} (the server's time, Europe/Budapest) · next ${next}`)).toBeInTheDocument()
  expect(overview.getByText('After each backup the app writes, it keeps the newest 14 and none older than 30 days; the newest always stays')).toBeInTheDocument()
})

it('names the days and the times of a schedule that is not every day', async () => {
  const { state } = backupsServer()
  state.schedule = { times: ['02:00', '14:30'], days: ['MONDAY', 'THURSDAY'], timeZone: 'UTC', nextRun: null, keep: null, keepDays: null }
  await openBackups()

  const at = (h: number, m: number) => new Intl.DateTimeFormat('en', { timeStyle: 'short', timeZone: 'UTC' }).format(new Date(Date.UTC(2000, 0, 1, h, m)))
  const overview = within(screen.getByRole('region', { name: 'This server' }))
  expect(overview.getByText(`Monday and Thursday at ${at(2, 0)} and ${at(14, 30)} (the server's time, UTC)`)).toBeInTheDocument()
})

it('says when nothing is scheduled and every backup is kept', async () => {
  backupsServer()
  await openBackups()

  const overview = within(screen.getByRole('region', { name: 'This server' }))
  expect(overview.getByText('Not scheduled')).toBeInTheDocument()
  expect(overview.getByText('Every backup is kept')).toBeInTheDocument()
  expect(overview.getByRole('link', { name: 'Change the schedule, retention and encryption in the server settings' })).toHaveAttribute('href', '/admin/settings?tab=backups')
})

it('a server without backups says how to make the first one', async () => {
  backupsServer({ points: [] })
  await openBackups()

  expect(screen.getByText('There are no backups in the folder yet. Back up now, upload one, or copy a file into /data/backups.')).toBeInTheDocument()
  expect(screen.queryByRole('table')).not.toBeInTheDocument()
})

it('is a page of the Administration group, and the backups are only asked for there', async () => {
  const { calls } = backupsServer()
  renderWithApollo(<App />, '/admin/people')
  await screen.findByRole('tab', { name: 'Users' })
  expect(calls.Backups).toBeUndefined()
  const ui = userEvent.setup()
  await ui.click(within(screen.getByRole('group', { name: 'Administration' })).getByRole('link', { name: 'Backups' }))
  expect(await screen.findByRole('heading', { level: 1, name: 'Backups' })).toBeInTheDocument()
  await screen.findByRole('heading', { name: 'Backups in the folder' })
  expect(calls.Backups).toBeDefined()
})
