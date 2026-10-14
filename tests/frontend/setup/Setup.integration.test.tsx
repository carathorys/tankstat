import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import type { AuthMode, SessionQuery } from '../../../src/frontend/gql/generated.ts'
import { navigation } from '../../../src/frontend/navigation.ts'
import { fakeInstance, fakeVehicleBackend, gqlError, healthHandler, renderWithApollo, user } from '../support/mocks.tsx'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.restoreAllMocks()
})
afterAll(() => server.close())

const CODE = 'ABCD-EFGH-JKLM'
type Notice = SessionQuery['notices'][number]
const setupNotice: Notice = { code: 'SETUP_PENDING', severity: 'INFO', message: 'This instance is not set up yet.' }

const backup = {
  file: 'tankstat-backup-20261010T020000Z.zip',
  bytes: 4096,
  createdAt: '2026-10-10T02:00:00Z',
  appVersion: '0.15.0',
  migration: 'AddSetup',
  sameMigration: true,
  olderMigration: false,
  safetyNet: false,
  uploaded: false,
  encrypted: false,
  photos: 'WITH_PICTURES',
}

/**
 * A new instance waiting for its setup: the session answers with `SETUP_PENDING` until a step completes it, the code opens the steps, and
 * the calls are recorded.
 */
function waitingServer(mode: AuthMode, options: { signedIn?: SessionQuery['session']['user']; points?: (typeof backup)[] } = {}) {
  const state = {
    pending: true,
    restorePending: false,
    generation: 'gen-1',
    current: options.signedIn ?? null,
    calls: {} as Record<string, unknown[]>,
  }
  const record = (name: string, vars: unknown) => (state.calls[name] ??= []).push(vars)
  const notices = (): Notice[] => [
    ...(mode === 'NONE' ? [{ code: 'AUTH_DISABLED', severity: 'WARNING' as const, message: 'Authentication is disabled.' }] : []),
    ...(state.restorePending ? [{ code: 'RESTORE_PENDING', severity: 'WARNING' as const, message: 'A restore is waiting.' }] : []),
    ...(state.pending ? [setupNotice] : []),
  ]
  server.use(
    graphql.query('Session', () => HttpResponse.json({ data: { session: { mode, user: state.current, instance: fakeInstance(state.generation) }, notices: notices() } })),
    healthHandler,
    ...fakeVehicleBackend().handlers,
    graphql.query('Setup', () => HttpResponse.json({ data: { setup: { __typename: 'SetupInfo', pending: state.pending } } })),
    graphql.query('SetupRestorePoints', ({ variables }) => {
      record('SetupRestorePoints', variables)
      if (variables.code.replace(/[-\s]/g, '').toUpperCase() !== CODE.replaceAll('-', '')) // the server ignores hyphens, spaces and case
        return HttpResponse.json(gqlError("The one-time setup code from the server's log is needed.", 'FORBIDDEN', 'setup.codeInvalid'))
      return HttpResponse.json({ data: { setupRestorePoints: (options.points ?? []).map((p) => ({ __typename: 'RestorePointInfo', ...p })) } })
    }),
    graphql.mutation('SetupCreateAdministrator', ({ variables }) => {
      record('SetupCreateAdministrator', variables)
      state.pending = false
      state.current = user({ email: variables.input.email, isAdmin: true })
      return HttpResponse.json({ data: { setupCreateAdministrator: { __typename: 'UserInfo', id: 'u1', email: variables.input.email, isAdmin: true } } })
    }),
    graphql.mutation('SetupClaimAdministrator', ({ variables }) => {
      record('SetupClaimAdministrator', variables)
      state.pending = false
      state.current = { ...state.current!, isAdmin: true }
      return HttpResponse.json({ data: { setupClaimAdministrator: { __typename: 'UserInfo', id: state.current.id, isAdmin: true } } })
    }),
    graphql.mutation('SetupAcknowledgeOpenAccess', ({ variables }) => {
      record('SetupAcknowledgeOpenAccess', variables)
      state.pending = false
      return HttpResponse.json({ data: { setupAcknowledgeOpenAccess: { __typename: 'SetupInfo', pending: false } } })
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

it('in Standalone, the setup page stands in for every page, and the code makes the first administrator, who is then signed in', async () => {
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

  const fresh = await screen.findByRole('region', { name: 'Start fresh' })
  expect(screen.getByRole('region', { name: 'Restore a backup' })).toHaveTextContent('There is no backup in the server')
  await ui.type(within(fresh).getByRole('textbox', { name: /^E-mail/ }), 'root@example.com')
  await ui.type(within(fresh).getByLabelText(/^Password/), 'initial-password-1')
  await ui.click(within(fresh).getByRole('button', { name: 'Create the administrator' }))

  await waitFor(() => expect(screen.queryByRole('heading', { name: 'Set up Tankstat' })).not.toBeInTheDocument())
  expect(state.calls.SetupCreateAdministrator).toEqual([{ input: { code: 'abcd efgh jklm', email: 'root@example.com', password: 'initial-password-1' } }])
  expect(state.current?.isAdmin).toBe(true)
})

it('with sign-in off, the setup ends with the acknowledgement of open access', async () => {
  const ui = userEvent.setup()
  const state = waitingServer('NONE')
  renderWithApollo(<App />)

  await enterCode(ui)
  const fresh = await screen.findByRole('region', { name: 'Start fresh' })
  const finish = within(fresh).getByRole('button', { name: 'Finish the setup' })
  expect(finish).toBeDisabled()
  expect(screen.queryByRole('navigation')).not.toBeInTheDocument() // no menu: no page is there yet
  await ui.click(within(fresh).getByRole('checkbox', { name: /everyone who can reach this address sees and changes all data/ }))
  await ui.click(finish)

  await waitFor(() => expect(screen.queryByRole('heading', { name: 'Set up Tankstat' })).not.toBeInTheDocument())
  expect(state.calls.SetupAcknowledgeOpenAccess).toEqual([{ code: CODE }])
})

it('restores a backup of the folder with the code, and reloads once the restarted server has the restored data', async () => {
  const ui = userEvent.setup()
  const state = waitingServer('STANDALONE', { points: [backup, { ...backup, file: 'newer.zip', sameMigration: false }] })
  const reload = vi.spyOn(navigation, 'reload').mockImplementation(() => undefined)
  renderWithApollo(<App />)

  await enterCode(ui)
  const restore = await screen.findByRole('region', { name: 'Restore a backup' })
  expect(within(restore).getByText('Made by a newer release of Tankstat: restore it with that release.')).toBeInTheDocument()
  expect(within(restore).getAllByRole('button', { name: /^Restore the backup of/ })).toHaveLength(1) // a newer release's has no button
  await ui.click(within(restore).getByRole('button', { name: /^Restore the backup of/ }))
  const dialog = await screen.findByRole('alertdialog', { name: /^Restore the backup of .*\?$/ })
  await ui.click(within(dialog).getByRole('button', { name: 'Restore and restart' }))

  expect(await screen.findByText(/the server is restarting/)).toBeInTheDocument()
  expect(state.calls.RestoreBackup).toEqual([{ file: backup.file, passphrase: null, identity: null, code: CODE }])
  state.restorePending = false // back with the backup's data, which is set up
  state.pending = false
  state.generation = 'gen-2'
  await waitFor(() => expect(reload).toHaveBeenCalled(), { timeout: 5000 })
})

it('with a provider, the signed-in user finds the setup through the notice and becomes the administrator', async () => {
  const ui = userEvent.setup()
  const state = waitingServer('OIDC', { signedIn: user({ displayName: 'Ada', isAdmin: false }) })
  renderWithApollo(<App />)

  await ui.click(await screen.findByRole('link', { name: 'Open the setup' }))
  await enterCode(ui)
  const fresh = await screen.findByRole('region', { name: 'Start fresh' })
  expect(fresh).toHaveTextContent('You are signed in as Ada.')
  await ui.click(within(fresh).getByRole('button', { name: 'Become the administrator' }))

  await waitFor(() => expect(state.calls.SetupClaimAdministrator).toEqual([{ code: CODE }]))
  await waitFor(() => expect(screen.queryByRole('link', { name: 'Open the setup' })).not.toBeInTheDocument())
})

it('keeps the notice above the sign-in at /setup, where no setup page is shown before it', async () => {
  waitingServer('PROXY_HEADER')
  renderWithApollo(<App />, '/setup')

  expect(await screen.findByRole('link', { name: 'Open the setup' })).toBeInTheDocument()
})

it('says there is nothing to set up once the instance is set up', async () => {
  const state = waitingServer('STANDALONE', { signedIn: user({ isAdmin: true }) })
  state.pending = false
  renderWithApollo(<App />, '/setup')

  expect(await screen.findByText('This instance is set up: there is nothing to set up here.')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Go to the start page' })).toHaveAttribute('href', '/')
})
