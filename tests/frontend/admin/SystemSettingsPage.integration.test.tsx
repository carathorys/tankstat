import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, beforeEach, expect, it, vi } from 'vitest'
import { RESTART_WAIT } from '../../../src/frontend/admin/restartFlow.ts'
import App from '../../../src/frontend/App.tsx'
import type { SystemSettingFieldsFragment } from '../../../src/frontend/gql/generated.ts'
import { navigation } from '../../../src/frontend/navigation.ts'
import { server } from '../support/server.ts'
import { fakeVehicleBackend, gqlError, healthHandler, renderWithApollo, sessionHandler, user } from '../support/mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const slow = { ...RESTART_WAIT }
beforeEach(() => Object.assign(RESTART_WAIT, { interval: 1, downFor: 5, upFor: 500 }))
afterEach(() => Object.assign(RESTART_WAIT, slow))

// Typed, as the server answers them: a fragment never matches an untyped object.
const setting = (over: Partial<SystemSettingFieldsFragment> & { key: string }): SystemSettingFieldsFragment & { __typename: 'SystemSetting' } => ({
  __typename: 'SystemSetting',
  kind: 'TEXT',
  choices: [],
  value: null,
  isSet: false,
  isSecret: false,
  source: 'DEFAULT',
  editable: true,
  restartPending: false,
  ...over,
})

const MODES = ['None', 'Standalone', 'Oidc', 'ProxyHeader']
const LEVELS = ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical', 'None']

/** The server's answer: the sign-in mode held by the environment, a list and a secret from the file, a currency waiting for a restart. */
function fixture({ modeEditable = false }: { modeEditable?: boolean } = {}) {
  return {
    __typename: 'SystemSettingsInfo',
    file: { __typename: 'SettingsFileInfo', path: '/data/settings.json', exists: true, ignored: false, problems: [] },
    restartPending: true,
    groups: [
      {
        __typename: 'SystemSettingsGroup',
        name: 'AUTH',
        settings: [
          setting(
            modeEditable
              ? { key: 'Auth:Mode', kind: 'CHOICE', choices: MODES, value: 'Standalone', isSet: true, source: 'SHIPPED' }
              : { key: 'Auth:Mode', kind: 'CHOICE', choices: MODES, value: 'Standalone', isSet: true, source: 'ENVIRONMENT', editable: false },
          ),
          setting({ key: 'Auth:PublicUrl' }),
          setting({ key: 'Auth:AdminEmails', kind: 'LIST', value: 'a@example.com\nb@example.com', isSet: true, source: 'FILE' }),
          setting({ key: 'Auth:Oidc:ClientSecret', kind: 'SECRET', isSecret: true, isSet: true, source: 'FILE' }),
          setting({ key: 'Auth:Standalone:AllowAdminSetPassword', kind: 'BOOLEAN' }),
        ],
      },
      { __typename: 'SystemSettingsGroup', name: 'DEFAULTS', settings: [setting({ key: 'Defaults:Currency', value: 'HUF', isSet: true, source: 'FILE', restartPending: true })] },
      { __typename: 'SystemSettingsGroup', name: 'LOGGING', settings: [setting({ key: 'Logging:LogLevel:Default', kind: 'CHOICE', choices: LEVELS, value: 'Information', isSet: true, source: 'SHIPPED' })] },
    ],
  }
}

function settingsServer(answer: object = fixture()) {
  const calls: Record<string, unknown[]> = {}
  const record = (name: string, vars: unknown) => (calls[name] ??= []).push(vars)
  server.use(
    sessionHandler('STANDALONE', () => user({ isAdmin: true })),
    healthHandler,
    ...fakeVehicleBackend().handlers,
    graphql.query('SystemSettings', () => HttpResponse.json({ data: { systemSettings: answer } })),
    graphql.mutation('UpdateSystemSettings', ({ variables }) => {
      record('UpdateSystemSettings', variables)
      return HttpResponse.json({ data: { updateSystemSettings: answer } })
    }),
    graphql.mutation('RestartServer', ({ variables }) => {
      record('RestartServer', variables)
      return HttpResponse.json({ data: { restartServer: true } })
    }),
  )
  return calls
}

const page = async () => within(await screen.findByRole('region', { name: 'System settings' }))
const group = async (name: string) => within(await screen.findByRole('region', { name }))

it('shows every group, where each value comes from, and what waits for a restart', async () => {
  settingsServer()
  renderWithApollo(<App />, '/admin/settings')

  const view = await page()
  expect(await view.findByText('1 setting waits for a restart.')).toBeInTheDocument()
  expect(view.getByText('/data/settings.json')).toBeInTheDocument()
  const auth = await group('Sign-in')
  expect(auth.getByText('Set by the environment (Auth__Mode): change it there.')).toBeInTheDocument() // read-only: its variable wins
  expect(auth.queryByRole('combobox', { name: 'Sign-in mode' })).not.toBeInTheDocument()
  expect(auth.getByText('Standalone')).toBeInTheDocument()
  expect(auth.getByLabelText('Public address')).toHaveValue('')
  expect(auth.getByLabelText('Administrators (OIDC and proxy sign-in)')).toHaveValue('a@example.com\nb@example.com')
  expect(auth.getByLabelText('OIDC client secret')).toHaveValue('') // never shown back
  expect(auth.getByLabelText('OIDC client secret')).toHaveAccessibleDescription(/Set\. Leave blank to keep it\./)
  expect(auth.getByRole('switch', { name: 'Administrators may set passwords' })).not.toBeChecked()
  const defaults = await group('Defaults for new vehicles')
  expect(defaults.getByLabelText('Currency')).toHaveValue('HUF')
  expect(defaults.getByLabelText('Currency')).toHaveAccessibleDescription(/takes effect after a restart/)
  expect(defaults.getByRole('button', { name: 'Save changes' })).toBeDisabled() // nothing changed yet
  const logging = await group('Logging')
  expect(logging.getByRole('combobox', { name: 'Everything else' })).toHaveTextContent('Information')
})

it('saves only what changed, a secret only when typed, and says so', async () => {
  const ui = userEvent.setup()
  const calls = settingsServer()
  renderWithApollo(<App />, '/admin/settings')

  const auth = await group('Sign-in')
  await ui.type(auth.getByLabelText('Public address'), 'https://tank.example')
  await ui.click(auth.getByRole('switch', { name: 'Administrators may set passwords' }))
  await ui.click(auth.getByRole('button', { name: 'Save changes' }))

  await screen.findByText('Saved. It takes effect after a restart.')
  expect(calls.UpdateSystemSettings).toEqual([
    {
      changes: [
        { key: 'Auth:PublicUrl', value: 'https://tank.example', clear: false },
        { key: 'Auth:Standalone:AllowAdminSetPassword', value: 'true', clear: false },
      ],
    },
  ])
})

it('a secret is removed with its checkbox, and a cleared text is sent as cleared', async () => {
  const ui = userEvent.setup()
  const calls = settingsServer()
  renderWithApollo(<App />, '/admin/settings')

  const auth = await group('Sign-in')
  await ui.click(auth.getByRole('checkbox', { name: 'Remove the stored value' }))
  await ui.click(auth.getByRole('button', { name: 'Save changes' }))
  await waitFor(() => expect(calls.UpdateSystemSettings).toHaveLength(1))
  expect(calls.UpdateSystemSettings[0]).toEqual({ changes: [{ key: 'Auth:Oidc:ClientSecret', value: null, clear: true }] })

  const defaults = await group('Defaults for new vehicles')
  await ui.clear(defaults.getByLabelText('Currency'))
  await ui.click(defaults.getByRole('button', { name: 'Save changes' }))
  await waitFor(() => expect(calls.UpdateSystemSettings).toHaveLength(2))
  expect(calls.UpdateSystemSettings[1]).toEqual({ changes: [{ key: 'Defaults:Currency', value: null, clear: true }] })
})

it('a refused save lists what the server could not take and keeps the form', async () => {
  const ui = userEvent.setup()
  settingsServer()
  server.use(
    graphql.mutation('UpdateSystemSettings', () =>
      HttpResponse.json(gqlError('The settings cannot be used as they are: Smtp:Port must be a whole number.', 'VALIDATION_FAILED', 'settings.invalid', { problems: ['Smtp:Port must be a whole number.', 'Auth:PublicUrl is required when Smtp is configured (it is used in e-mailed links).'] })),
    ),
  )
  renderWithApollo(<App />, '/admin/settings')

  const defaults = await group('Defaults for new vehicles')
  await ui.type(defaults.getByLabelText('Currency'), 'X')
  await ui.click(defaults.getByRole('button', { name: 'Save changes' }))

  const alert = await defaults.findByRole('alert')
  expect(alert).toHaveTextContent('The server could not take these values:')
  expect(within(alert).getAllByRole('listitem').map((li) => li.textContent)).toEqual(['Smtp:Port must be a whole number.', 'Auth:PublicUrl is required when Smtp is configured (it is used in e-mailed links).'])
  expect(defaults.getByLabelText('Currency')).toHaveValue('HUFX')
})

it('changing the sign-in mode asks once more and says the way back', async () => {
  const ui = userEvent.setup()
  const calls = settingsServer(fixture({ modeEditable: true }))
  renderWithApollo(<App />, '/admin/settings')

  const auth = await group('Sign-in')
  await ui.click(auth.getByRole('combobox', { name: 'Sign-in mode' }))
  await ui.click(await screen.findByRole('option', { name: 'Oidc' }))
  await ui.click(auth.getByRole('button', { name: 'Save changes' }))

  const dialog = await screen.findByRole('alertdialog', { name: 'Change the sign-in mode?' })
  expect(dialog).toHaveAccessibleDescription(/Oidc at the next restart, which signs everybody out, you included.*Auth__Mode/)
  expect(calls.UpdateSystemSettings).toBeUndefined()
  await ui.click(within(dialog).getByRole('button', { name: 'Change the mode' }))

  await waitFor(() => expect(calls.UpdateSystemSettings).toEqual([{ changes: [{ key: 'Auth:Mode', value: 'Oidc', clear: false }] }]))
})

it('restarting asks once more, waits for the server to go and come back, and reloads the page', async () => {
  const ui = userEvent.setup()
  const reload = vi.spyOn(navigation, 'reload').mockImplementation(() => undefined)
  const calls = settingsServer()
  let probes = 0
  renderWithApollo(<App />, '/admin/settings')
  const view = await page()
  server.use(graphql.query('Health', () => (++probes <= 2 ? HttpResponse.error() : HttpResponse.json({ data: { health: { status: 'ok', version: '1.2.4', databaseReachable: true } } }))))

  await ui.click(view.getByRole('button', { name: 'Restart server' }))
  const dialog = await screen.findByRole('alertdialog', { name: 'Restart the server?' })
  expect(dialog).toHaveAccessibleDescription(/stops and starts again within seconds/)
  await ui.click(within(dialog).getByRole('button', { name: 'Restart server' }))

  await waitFor(() => expect(reload).toHaveBeenCalledTimes(1))
  expect(calls.RestartServer).toHaveLength(1)
  expect(probes).toBeGreaterThanOrEqual(3) // down twice, then back
  expect(view.getByRole('status', { name: '' })).toBeDefined()
  expect(screen.getByText('The server is back; reloading the page.')).toBeInTheDocument()
})

it('a restart the next start would refuse says why, names the settings, and leaves the page as it is', async () => {
  const ui = userEvent.setup()
  const reload = vi.spyOn(navigation, 'reload').mockImplementation(() => undefined)
  settingsServer()
  server.use(
    graphql.mutation('RestartServer', () =>
      HttpResponse.json(
        gqlError('The server would not start again with the settings file as it is.', 'VALIDATION_FAILED', 'settings.restartWouldFail', {
          problems: ['Notifications:MaxPerHour must be at least 1.'],
        }),
      ),
    ),
  )
  renderWithApollo(<App />, '/admin/settings')
  const view = await page()

  await ui.click(view.getByRole('button', { name: 'Restart server' }))
  const dialog = await screen.findByRole('alertdialog', { name: 'Restart the server?' })
  await ui.click(within(dialog).getByRole('button', { name: 'Restart server' }))

  const alert = await view.findByRole('alert')
  expect(alert).toHaveTextContent('The server would not start again with the settings file as it is, so it was not restarted.')
  expect(within(alert).getAllByRole('listitem').map((li) => li.textContent)).toEqual(['Notifications:MaxPerHour must be at least 1.'])
  expect(view.getByRole('button', { name: 'Restart server' })).toBeEnabled() // nothing waits for a server that never went
  expect(reload).not.toHaveBeenCalled()
})

it('only administrators can open it', async () => {
  server.use(sessionHandler('STANDALONE', () => user()), healthHandler, ...fakeVehicleBackend().handlers)
  renderWithApollo(<App />, '/admin/settings')

  await screen.findByRole('heading', { name: 'Your vehicles' }) // bounced back to the home page
  expect(screen.queryByRole('region', { name: 'System settings' })).not.toBeInTheDocument()
})
