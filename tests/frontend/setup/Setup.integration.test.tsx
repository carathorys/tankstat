import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, beforeEach, expect, it, vi } from 'vitest'
import { RESTART_WAIT } from '../../../src/frontend/admin/restartFlow.ts'
import App from '../../../src/frontend/App.tsx'
import en from '../../../src/frontend/i18n/locales/en.json'
import type { AuthMode, SessionQuery } from '../../../src/frontend/gql/generated.ts'
import { navigation } from '../../../src/frontend/navigation.ts'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { backupRecipientHandler, fakeInstance, fakeVehicleBackend, gqlError, healthHandler, renderWithApollo, stubViewport, user, wizardSettings } from '../support/mocks.tsx'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.restoreAllMocks()
  vi.unstubAllGlobals() // a phone's viewport, where a test asks for one
})
afterAll(() => server.close())

const slow = { ...RESTART_WAIT }
beforeEach(() => Object.assign(RESTART_WAIT, { interval: 1, downFor: 5, upFor: 500 }))
afterEach(() => {
  Object.assign(RESTART_WAIT, slow)
  sessionStorage.clear()
})

const CODE = 'ABCD-EFGH-JKLM'
type Notice = SessionQuery['notices'][number]
const setupNotice: Notice = { code: 'SETUP_PENDING', severity: 'INFO', message: 'This instance is not set up yet.' }

const backup = {
  file: 'tankstat-backup-20261010T020000Z.zip',
  createdAt: '2026-10-10T02:00:00Z',
  appVersion: '0.15.0',
  sameMigration: true,
  olderMigration: false,
  encrypted: false,
  held: false,
  heldSince: null as string | null,
  // A backup of this release with nothing to warn about.
  settingsFile: { __typename: 'BackupSettingsFileInfo', state: 'INCLUDED', secrets: true, secretsLeftOut: [] } as Record<string, unknown> | null,
  environment: { __typename: 'BackupEnvironmentInfo', mode: 'RECORD', settings: [], missing: [], missingCertain: true, overridden: [] } as Record<string, unknown> | null,
}

/**
 * A new instance waiting for its setup: the session answers with `SETUP_PENDING` until a step completes it, the code opens the steps, and
 * the calls are recorded.
 */
function waitingServer(running: AuthMode, options: { signedIn?: SessionQuery['session']['user']; points?: (typeof backup)[] } = {}) {
  const state = {
    pending: true,
    restorePending: false,
    generation: 'gen-1',
    mode: running,
    current: options.signedIn ?? null,
    chosen: undefined as string | undefined, // the sign-in mode the wizard saved, waiting for the restart
    points: [...(options.points ?? [])],
    calls: {} as Record<string, unknown[]>,
  }
  const record = (name: string, vars: unknown) => (state.calls[name] ??= []).push(vars)
  const codeOk = (code: string) => code.replace(/[-\s]/g, '').toUpperCase() === CODE.replaceAll('-', '') // the server ignores hyphens, spaces and case
  const codeInvalid = gqlError("The one-time setup code from the server's log is needed.", 'FORBIDDEN', 'setup.codeInvalid')
  const notices = (): Notice[] => [
    ...(state.mode === 'NONE' ? [{ code: 'AUTH_DISABLED', severity: 'WARNING' as const, message: 'Authentication is disabled.' }] : []),
    ...(state.restorePending ? [{ code: 'RESTORE_PENDING', severity: 'WARNING' as const, message: 'A restore is waiting.' }] : []),
    ...(state.pending ? [setupNotice] : []),
  ]
  server.use(
    graphql.query('Session', () => HttpResponse.json({ data: { session: { mode: state.mode, user: state.current, instance: fakeInstance(state.generation) }, notices: notices() } })),
    healthHandler,
    ...fakeVehicleBackend().handlers,
    graphql.query('Setup', () => HttpResponse.json({ data: { setup: { __typename: 'SetupInfo', pending: state.pending } } })),
    graphql.query('SetupRestorePoints', ({ variables }) => {
      record('SetupRestorePoints', variables)
      if (!codeOk(variables.code)) return HttpResponse.json(codeInvalid)
      return HttpResponse.json({ data: { setupRestorePoints: state.points.map((p) => ({ __typename: 'RestorePointInfo', ...p })), setupUploadLimitMb: 1024 } })
    }),
    // A backup opened before it is restored, with the code (what its manifest says).
    graphql.query('SetupBackupDetails', ({ variables }) => {
      record('SetupBackupDetails', variables)
      if (!codeOk(variables.code)) return HttpResponse.json(codeInvalid)
      return HttpResponse.json({
        data: {
          setupBackupDetails: {
            __typename: 'BackupDetailsInfo', file: variables.file, bytes: 2_400_000, held: true, heldSince: '2026-10-12T08:00:00Z', format: 1, writtenBy: 'tankstat',
            createdAt: backup.createdAt, appVersion: backup.appVersion, provider: 'Sqlite', migration: 'AddInstance', safetyNet: false, uploaded: false, encrypted: false,
            recipient: null, currentRecipient: false, encryptionMethod: null, pictures: true, photos: 'WITH_PICTURES', keys: false,
            tables: [{ __typename: 'BackupTableInfo', name: 'Vehicles', rows: 3 }],
            sizes: { __typename: 'BackupSizesInfo', tables: 1000, pictures: 0, photos: 0, keys: 0, archive: 900 },
            archiveEntry: 'data.zip', sha256: 'cd'.repeat(32), settingsFile: null, environment: null,
          },
        },
      })
    }),
    // The wizard's settings, with the code: a sign-in mode saved waits for the restart the last step makes.
    graphql.query('SystemSettings', ({ variables }) => {
      record('SystemSettings', variables)
      if (!variables.code || !codeOk(variables.code)) return HttpResponse.json(codeInvalid)
      return HttpResponse.json({ data: { systemSettings: wizardSettings(running, state.chosen) } })
    }),
    graphql.mutation('UpdateSystemSettings', ({ variables }) => {
      record('UpdateSystemSettings', variables)
      const mode = (Array.isArray(variables.changes) ? variables.changes : [variables.changes]).find((c) => c.key === 'Auth:Mode')
      if (mode) state.chosen = mode.value ?? undefined
      return HttpResponse.json({ data: { updateSystemSettings: wizardSettings(running, state.chosen) } })
    }),
    // The last step, in the mode the next start runs: the administrator (signed in when Standalone runs already), or the acknowledgement;
    // a mode saved by the wizard restarts the server into it.
    graphql.mutation('SetupFinish', ({ variables }) => {
      record('SetupFinish', variables.input)
      const restarting = wizardSettings(running, state.chosen).restartPending
      const next = (state.chosen ?? { NONE: 'None', STANDALONE: 'Standalone', OIDC: 'Oidc', PROXY_HEADER: 'ProxyHeader' }[running]).toLowerCase()
      const signedIn = next === 'standalone' && running === 'STANDALONE'
      if (next === 'standalone' || next === 'none') state.pending = false
      if (signedIn) state.current = user({ email: variables.input.email ?? '', isAdmin: true })
      if (restarting) state.mode = next === 'standalone' ? 'STANDALONE' : next === 'oidc' ? 'OIDC' : next === 'proxyheader' ? 'PROXY_HEADER' : 'NONE'
      return HttpResponse.json({ data: { setupFinish: { __typename: 'SetupFinishInfo', restarting, signedIn } } })
    }),
    graphql.mutation('SetupClaimAdministrator', ({ variables }) => {
      record('SetupClaimAdministrator', variables)
      state.pending = false
      state.current = { ...state.current!, isAdmin: true }
      return HttpResponse.json({ data: { setupClaimAdministrator: { __typename: 'UserInfo', id: state.current.id, isAdmin: true } } })
    }),
    backupRecipientHandler(),
    // An upload with the code, as the setup sends it: the file joins the folder.
    http.put('/media/backups', async ({ request }) => {
      record('Upload', { name: new URL(request.url).searchParams.get('name'), code: request.headers.get('X-Setup-Code') })
      if (!codeOk(request.headers.get('X-Setup-Code') ?? '')) return HttpResponse.json({ key: 'setup.codeInvalid', message: 'No.' }, { status: 403 })
      state.points.push({ ...backup, file: 'uploaded-from-old.zip' })
      return HttpResponse.json({ file: 'uploaded-from-old.zip', bytes: (await request.arrayBuffer()).byteLength })
    }),
    graphql.mutation('RestoreBackup', ({ variables }) => {
      record('RestoreBackup', variables.input)
      state.restorePending = true
      return HttpResponse.json({ data: { restoreBackup: { file: variables.input.file, restorePoint: backup.createdAt } } })
    }),
  )
  return state
}

async function enterCode(ui: ReturnType<typeof userEvent.setup>, code = CODE) {
  await ui.type(await screen.findByRole('textbox', { name: /^Setup code/ }), code)
  await ui.click(screen.getByRole('button', { name: 'Continue' }))
}

/** The wizard's step at hand: its label, the stepper's current step, which takes the focus as the step opens. */
const stepAt = (name: string) => screen.findByText(name, { selector: '[aria-current="step"]' })

it('in Standalone, the code opens the wizard: its settings, then the first administrator, signed in, who sees what to set up next', async () => {
  const ui = userEvent.setup()
  const state = waitingServer('STANDALONE')
  renderWithApollo(<App />, '/vehicles/v1')

  expect(await screen.findByRole('heading', { level: 1, name: 'Set up Tankstat' })).toBeInTheDocument()
  expect(screen.queryByRole('heading', { name: 'Sign in' })).not.toBeInTheDocument()
  expect(screen.queryByRole('link', { name: 'Open the setup' })).not.toBeInTheDocument() // the page is the notice

  await enterCode(ui, 'nope')
  expect(await screen.findByRole('alert')).toHaveTextContent("This is not the setup code from the server's log.")
  await ui.clear(screen.getByRole('textbox', { name: /^Setup code/ }))
  await enterCode(ui, 'abcd efgh jklm')

  // Every step at once, the start at hand: set the instance up, or restore a backup. Nothing is chosen for the person.
  expect(await stepAt('Start')).toHaveFocus()
  expect(state.calls.SystemSettings).toEqual([{ code: 'abcd efgh jklm' }])
  const steps = screen.getByRole('list', { name: 'Setup steps' })
  expect(within(steps).getAllByRole('listitem')).toHaveLength(1) // a way's steps come once it is chosen
  // A restore instead, chosen on purpose; the start can be gone back to at any time.
  await ui.click(screen.getByRole('button', { name: 'Restore from a backup' }))
  expect(await stepAt('Restore a backup')).toHaveFocus()
  expect(screen.getByRole('region', { name: 'Restore a backup' })).toHaveTextContent('There is no backup in the server')
  await ui.click(screen.getByRole('button', { name: 'Start' }))
  await ui.click(await screen.findByRole('button', { name: 'Start the wizard' }))

  // The sign-in is the environment's (Standalone) and its other setting is a provider's: no step for it. The e-mail first.
  expect(await stepAt('E-mail')).toHaveFocus()
  expect(within(steps).getAllByRole('listitem').map((step) => step.querySelector('.MuiStepLabel-label')?.textContent)).toEqual(['Start', 'E-mail', 'Defaults', 'Finish'])
  await ui.click(screen.getByRole('button', { name: 'Skip' }))
  expect(await stepAt('Defaults')).toHaveFocus()
  await ui.click(screen.getByRole('button', { name: 'Back' }))
  await stepAt('E-mail')
  await ui.click(screen.getByRole('button', { name: 'Defaults' })) // every step opens by its label, in any order
  await stepAt('Defaults')
  await ui.click(screen.getByRole('button', { name: 'Next' })) // nothing changed: nothing saved, on to the next step
  expect(await stepAt('Finish')).toHaveFocus()
  expect(state.calls.UpdateSystemSettings).toBeUndefined()

  const last = screen.getByRole('region', { name: 'Finish' })
  await ui.type(within(last).getByRole('textbox', { name: /^E-mail/ }), 'root@example.com')
  await ui.type(within(last).getByLabelText(/^Password/), 'initial-password-1')
  await ui.click(within(last).getByRole('button', { name: 'Create the administrator' }))

  // Signed in, with the menu, on the done screen: backups encrypted at once, and where the rest is.
  expect(await screen.findByText("The setup is done: you are this instance's administrator.")).toBeInTheDocument()
  expect(state.calls.SetupFinish).toEqual([{ code: 'abcd efgh jklm', email: 'root@example.com', password: 'initial-password-1' }])
  expect(await screen.findByRole('region', { name: 'Encrypted backups' })).toHaveTextContent('Backups are written without encryption.')
  expect(screen.getByRole('button', { name: 'Make a key set…' })).toBeInTheDocument()
  const next = screen.getByRole('region', { name: 'What to set up next' })
  expect(within(next).getByRole('link', { name: /^Backups:/ })).toHaveAttribute('href', '/admin/settings?tab=backups')
  expect(within(next).getByRole('link', { name: /^Defaults for new vehicles/ })).toHaveAttribute('href', '/admin/settings?tab=defaults')
  expect(within(next).getByRole('link', { name: /^People/ })).toHaveAttribute('href', '/admin/people')
  expect(screen.getAllByRole('navigation').length).toBeGreaterThan(0)
})

it('with sign-in off, the sign-in mode must be chosen (none is preselected), and the setup ends with the acknowledgement', async () => {
  const ui = userEvent.setup()
  const state = waitingServer('NONE')
  renderWithApollo(<App />)

  await enterCode(ui)
  // The shipped sign-in off is not chosen yet: its warning waits until the setup is done, the wizard asks for a mode.
  expect(screen.queryByText(en.notices.AUTH_DISABLED)).not.toBeInTheDocument()
  await ui.click(await screen.findByRole('button', { name: 'Start the wizard' }))
  await stepAt('Sign-in')
  expect(screen.queryByRole('button', { name: 'Skip' })).not.toBeInTheDocument() // the sign-in cannot be skipped
  const mode = screen.getByRole('combobox', { name: /^Sign-in mode/ })
  expect(mode).toHaveTextContent('Choose one')
  await ui.click(screen.getByRole('button', { name: 'Next' }))
  expect(await screen.findByText('Choose one to go on.')).toBeInTheDocument()
  expect(mode).toHaveFocus()
  await ui.click(mode)
  expect(screen.queryByRole('option', { name: /Default/ })).not.toBeInTheDocument()
  await ui.click(screen.getByRole('option', { name: 'No sign-in, not secure (None)' }))
  await ui.click(screen.getByRole('button', { name: 'Next' }))
  await stepAt('Defaults') // the e-mail is for local accounts: no step for it with sign-in off
  expect(screen.queryByRole('navigation')).not.toBeInTheDocument() // no menu: no page is there yet
  await ui.click(screen.getByRole('button', { name: 'Next' }))

  const last = await screen.findByRole('region', { name: 'Finish' })
  const finish = within(last).getByRole('button', { name: 'Finish the setup' })
  expect(finish).toBeDisabled()
  await ui.click(within(last).getByRole('checkbox', { name: /everyone who can reach this address sees and changes all data/ }))
  await ui.click(finish)

  await waitFor(() => expect(screen.queryByRole('heading', { name: 'Set up Tankstat' })).not.toBeInTheDocument())
  expect(await screen.findByText(en.notices.AUTH_DISABLED)).toBeInTheDocument() // the app itself warns again
  expect(state.calls.SetupFinish).toEqual([{ code: CODE, acknowledgeOpenAccess: true }])
  expect(state.calls.UpdateSystemSettings).toBeUndefined() // None was what ran: nothing to save
})

it('on a desktop the steps stand in a row, the one at hand below them', async () => {
  const ui = userEvent.setup()
  waitingServer('STANDALONE')
  renderWithApollo(<App />)
  await enterCode(ui)
  await ui.click(await screen.findByRole('button', { name: 'Start the wizard' }))

  const current = (await stepAt('E-mail')).closest('li')!
  expect(within(current).queryByRole('button', { name: 'Next' })).not.toBeInTheDocument() // below the row, not in the step
  expect(screen.getByRole('button', { name: 'Next' })).toBeInTheDocument()
})

it('on a phone each step opens under its own label', async () => {
  const ui = userEvent.setup()
  stubViewport('phone')
  waitingServer('STANDALONE')
  renderWithApollo(<App />)
  await enterCode(ui)
  await ui.click(await screen.findByRole('button', { name: 'Start the wizard' }))

  const current = (await stepAt('E-mail')).closest('li')!
  expect(within(current).getByRole('button', { name: 'Next' })).toBeInTheDocument()
})

it('a step left without what it needs is an error step, and the last step lists it instead of finishing; values stay as chosen', async () => {
  const ui = userEvent.setup()
  waitingServer('NONE')
  renderWithApollo(<App />)

  await enterCode(ui)
  await ui.click(await screen.findByRole('button', { name: 'Start the wizard' }))
  await stepAt('Sign-in')
  await ui.click(screen.getByRole('button', { name: 'Finish' })) // straight to the end: the steps open in any order

  const last = await screen.findByRole('region', { name: 'Finish' })
  expect(last).toHaveTextContent('Before the setup can finish, these steps need you:')
  expect(within(last).queryByRole('button', { name: 'Finish the setup' })).not.toBeInTheDocument()
  expect(screen.getByText('Choose a sign-in mode', { selector: '.MuiStepLabel-root *' })).toBeInTheDocument() // said on the step's label too
  await ui.click(within(last).getByRole('button', { name: 'Sign-in: Choose a sign-in mode' }))
  await stepAt('Sign-in')

  await ui.click(screen.getByRole('combobox', { name: /^Sign-in mode/ }))
  await ui.click(screen.getByRole('option', { name: 'No sign-in, not secure (None)' }))
  await ui.click(screen.getByRole('button', { name: 'Next' }))
  await stepAt('Defaults')
  await ui.click(screen.getByRole('button', { name: 'Sign-in' }))
  // Gone back to, the step shows what was chosen (None runs already, so nothing was saved: the choice is the wizard's).
  expect(await screen.findByRole('combobox', { name: /^Sign-in mode/ })).toHaveTextContent('No sign-in, not secure (None)')
  await ui.click(screen.getByRole('button', { name: 'Finish' }))
  expect(await screen.findByRole('button', { name: 'Finish the setup' })).toBeInTheDocument() // nothing in the way any more
})

it('a sign-in mode chosen in the wizard is saved with the code, and the last step makes its administrator, then one restart', async () => {
  const ui = userEvent.setup()
  const state = waitingServer('NONE')
  renderWithApollo(<App />)

  await enterCode(ui)
  await ui.click(await screen.findByRole('button', { name: 'Start the wizard' }))
  await stepAt('Sign-in')
  await ui.click(screen.getByRole('combobox', { name: /^Sign-in mode/ }))
  await ui.click(screen.getByRole('option', { name: 'Local accounts (Standalone)' }))
  await ui.click(screen.getByRole('button', { name: 'Next' }))

  expect(await stepAt('E-mail')).toHaveFocus() // local accounts send e-mail: its step is there now
  expect(state.calls.UpdateSystemSettings).toEqual([{ changes: [{ key: 'Auth:Mode', value: 'Standalone', clear: false }], code: CODE }])
  await ui.click(screen.getByRole('button', { name: 'Skip' }))
  await stepAt('Defaults')
  await ui.click(screen.getByRole('button', { name: 'Next' }))
  const last = screen.getByRole('region', { name: 'Finish' })
  await ui.type(within(last).getByRole('textbox', { name: /^E-mail/ }), 'root@example.com')
  await ui.type(within(last).getByLabelText(/^Password/), 'initial-password-1')
  await ui.click(within(last).getByRole('button', { name: 'Create the administrator' }))

  // The restart signs everyone out of the old mode: the new administrator signs in.
  expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  expect(state.calls.SetupFinish).toEqual([{ code: CODE, email: 'root@example.com', password: 'initial-password-1' }])
  expect(state.mode).toBe('STANDALONE')
})

it('with sign-in off, a device that loses the server shows its own data, not a setup page that needs the server', async () => {
  waitingServer('NONE')
  renderWithApollo(<App />)
  expect(await screen.findByRole('heading', { level: 1, name: 'Set up Tankstat' })).toBeInTheDocument()

  act(() => connectivity.failed()) // the session is the device's last answer from now on, maybe from before a setup done elsewhere

  expect(await screen.findByRole('navigation')).toBeInTheDocument()
  expect(screen.queryByRole('heading', { level: 1, name: 'Set up Tankstat' })).not.toBeInTheDocument()
})

it('uploads a backup the folder does not hold, with the code, and lists it to restore', async () => {
  const ui = userEvent.setup()
  const state = waitingServer('STANDALONE')
  renderWithApollo(<App />)

  await enterCode(ui)
  await ui.click(await screen.findByRole('button', { name: 'Restore from a backup' }))
  const restore = await screen.findByRole('region', { name: 'Restore a backup' })
  expect(within(restore).getByText(/up to 1024 MB/)).toBeInTheDocument()
  await ui.upload(screen.getByTestId('backup-file'), new File([new Uint8Array([1, 2, 3])], 'from-old.zip', { type: 'application/zip' }))

  // Listed with the folder's, to restore like them.
  expect(await within(restore).findByRole('button', { name: /^Restore the backup of .*\(uploaded-from-old\.zip\)$/ })).toBeInTheDocument()
  expect(state.calls.Upload).toEqual([{ name: 'from-old.zip', code: CODE }]) // the code in its header, never the address
})

it('opens a backup of the folder before restoring it, with the code, and marks one held or holding its secrets in plain', async () => {
  const ui = userEvent.setup()
  const state = waitingServer('STANDALONE', { points: [{ ...backup, held: true, heldSince: '2026-10-12T08:00:00Z' }] })
  renderWithApollo(<App />)

  await enterCode(ui)
  await ui.click(await screen.findByRole('button', { name: 'Restore from a backup' }))
  const restore = await screen.findByRole('region', { name: 'Restore a backup' })
  expect(within(restore).getByText(/^Held since .*: never removed by retention/)).toBeInTheDocument()
  expect(within(restore).getByText('In plain with its secrets: anyone who has the file reads the passwords in it')).toBeInTheDocument() // plain, its secrets in
  await ui.click(within(restore).getByRole('button', { name: /^Open the backup of .*\(tankstat-backup-20261010T020000Z\.zip\)$/ }))

  const dialog = await screen.findByRole('dialog', { name: /^The backup of/ })
  expect(await within(dialog).findByRole('table', { name: 'Tables: 1 · rows: 3' })).toBeInTheDocument()
  expect(dialog).toHaveTextContent('None: an older release wrote it, before backups held the settings file.')
  expect(state.calls.SetupBackupDetails).toEqual([{ code: CODE, file: 'tankstat-backup-20261010T020000Z.zip' }])
})

it('says before a restore on the setup page which settings of the old server this new one lacks, which it is refused for', async () => {
  const ui = userEvent.setup()
  waitingServer('STANDALONE', {
    points: [
      {
        ...backup,
        settingsFile: { __typename: 'BackupSettingsFileInfo', state: 'INCLUDED', secrets: true, secretsLeftOut: [] },
        environment: { __typename: 'BackupEnvironmentInfo', mode: 'RECORD', settings: ['Auth__Mode', 'Auth__Oidc__ClientSecret'], missing: ['Auth__Oidc__ClientSecret'], missingCertain: true, overridden: [] },
      },
    ],
  })
  renderWithApollo(<App />)

  await enterCode(ui)
  await ui.click(await screen.findByRole('button', { name: 'Restore from a backup' }))
  const restore = await screen.findByRole('region', { name: 'Restore a backup' })
  await ui.click(within(restore).getByRole('button', { name: /^Restore the backup of/ }))
  const dialog = await screen.findByRole('alertdialog', { name: /^Restore the backup of/ })

  expect(screen.getByText('Refused: this server lacks Auth__Oidc__ClientSecret')).toBeInTheDocument() // the list said it already
  expect(dialog).toHaveTextContent('The restore will be refused: its server set Auth__Oidc__ClientSecret in its environment')
  expect(within(dialog).getByRole('button', { name: 'Restore and restart' })).toBeDisabled()
})

it('restores a backup of the folder with the code, and reloads once the restarted server has the restored data', async () => {
  const ui = userEvent.setup()
  const state = waitingServer('STANDALONE', { points: [backup, { ...backup, file: 'newer.zip', sameMigration: false }] })
  const reload = vi.spyOn(navigation, 'reload').mockImplementation(() => undefined)
  renderWithApollo(<App />)

  await enterCode(ui)
  await ui.click(await screen.findByRole('button', { name: 'Restore from a backup' }))
  const restore = await screen.findByRole('region', { name: 'Restore a backup' })
  expect(within(restore).getByText('Made by a newer release of Tankstat: restore it with that release.')).toBeInTheDocument()
  expect(within(restore).getAllByRole('button', { name: /^Restore the backup of/ })).toHaveLength(1) // a newer release's has no button
  // Named by the file too: two backups of one minute (one and its uploaded copy) are told apart.
  expect(within(restore).getByRole('button', { name: /^Restore the backup of .* \(tankstat-backup-20261010T020000Z\.zip\)$/ })).toBeInTheDocument()
  await ui.click(within(restore).getByRole('button', { name: /^Restore the backup of/ }))
  const dialog = await screen.findByRole('alertdialog', { name: /^Restore the backup of .*\?$/ })
  await ui.click(within(dialog).getByRole('button', { name: 'Restore and restart' }))

  expect(await screen.findByText(/the server is restarting/)).toBeInTheDocument()
  expect(state.calls.RestoreBackup).toEqual([{ file: backup.file, passphrase: null, identity: null, code: CODE }])
  // The restart takes the server away: the page stays with its wait (no sign-in of the device's last answer in its place).
  act(() => connectivity.failed())
  expect(screen.getByText(/the server is restarting/)).toBeInTheDocument()
  expect(screen.getByRole('heading', { level: 1, name: 'Set up Tankstat' })).toBeInTheDocument()
  expect(screen.queryByRole('heading', { name: 'Sign in' })).not.toBeInTheDocument()
  act(() => connectivity.succeeded())
  state.restorePending = false // back with the backup's data, which is set up
  state.pending = false
  state.generation = 'gen-2'
  await waitFor(() => expect(reload).toHaveBeenCalled(), { timeout: 5000 })
})

it('with a provider, the signed-in user finds the setup through the notice, and its last step makes them the administrator', async () => {
  const ui = userEvent.setup()
  const state = waitingServer('OIDC', { signedIn: user({ displayName: 'Ada', isAdmin: false }) })
  renderWithApollo(<App />)

  await ui.click(await screen.findByRole('link', { name: 'Open the setup' }))
  await enterCode(ui)
  await ui.click(await screen.findByRole('button', { name: 'Start the wizard' }))
  await stepAt('Sign-in') // the provider's administrators may be named here; the e-mail is for local accounts
  await ui.click(screen.getByRole('button', { name: 'Next' }))
  await stepAt('Defaults')
  await ui.click(screen.getByRole('button', { name: 'Skip' }))

  const last = await screen.findByRole('region', { name: 'Finish' })
  expect(last).toHaveTextContent('You are signed in as Ada.')
  await ui.click(within(last).getByRole('button', { name: 'Become the administrator' }))

  expect(await screen.findByText("The setup is done: you are this instance's administrator.")).toBeInTheDocument()
  expect(state.calls.SetupClaimAdministrator).toEqual([{ code: CODE }])
  expect(screen.queryByRole('link', { name: 'Open the setup' })).not.toBeInTheDocument()
  expect(within(screen.getByRole('region', { name: 'What to set up next' })).queryByRole('link', { name: /^People/ })).not.toBeInTheDocument() // the provider brings the people
})

it('back from the provider after the wizard restarted into it, the setup goes on at its last step, the claim', async () => {
  const ui = userEvent.setup()
  sessionStorage.setItem('tankstat.setup.claimNext', '1') // what the last step left in this tab before the restart and the provider's sign-in
  waitingServer('OIDC', { signedIn: user({ displayName: 'Ada', isAdmin: false }) })
  renderWithApollo(<App />, '/setup')

  await enterCode(ui) // asked once more: the code is kept nowhere
  expect(await stepAt('Finish')).toHaveFocus()
  expect(screen.getByRole('button', { name: 'Become the administrator' })).toBeInTheDocument()
})

it('keeps the notice above the sign-in at /setup, where no setup page is shown before it', async () => {
  waitingServer('PROXY_HEADER')
  renderWithApollo(<App />, '/setup')

  expect(await screen.findByRole('link', { name: 'Open the setup' })).toBeInTheDocument()
})

it('asks the session again when the setup was completed elsewhere meanwhile, so the sign-in takes the setup page\'s place', async () => {
  const state = waitingServer('STANDALONE')
  server.use(
    graphql.query('Setup', () => {
      state.pending = false // another server of the instance completed it since the session was asked
      return HttpResponse.json({ data: { setup: { __typename: 'SetupInfo', pending: false } } })
    }),
  )
  renderWithApollo(<App />)

  expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
})

it('says why the key set is missing from the done screen when the backups cannot be asked for', async () => {
  const state = waitingServer('STANDALONE', { signedIn: user({ isAdmin: true }) })
  state.pending = false
  server.use(graphql.query('BackupRecipient', () => HttpResponse.json(gqlError('The backups folder cannot be read.', 'INTERNAL_SERVER_ERROR', 'backup.folderUnreadable'))))
  renderWithApollo(<App />, '/setup')

  expect(await screen.findByText('The backups folder cannot be read.')).toBeInTheDocument() // not an endless "Loading"
  expect(screen.getByRole('region', { name: 'What to set up next' })).toBeInTheDocument()
})

it('says there is nothing to set up once the instance is set up, to someone who is no administrator', async () => {
  const state = waitingServer('OIDC', { signedIn: user({ isAdmin: false }) })
  state.pending = false
  renderWithApollo(<App />, '/setup')

  expect(await screen.findByText('This instance is set up: there is nothing to set up here.')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Go to the start page' })).toHaveAttribute('href', '/')
})
