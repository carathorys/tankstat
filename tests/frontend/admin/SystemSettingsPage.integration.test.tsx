import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, beforeEach, expect, it, vi } from 'vitest'
import { RESTART_WAIT } from '../../../src/frontend/admin/restartFlow.ts'
import App from '../../../src/frontend/App.tsx'
import type { SystemSettingFieldsFragment } from '../../../src/frontend/gql/generated.ts'
import { navigation } from '../../../src/frontend/navigation.ts'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { server } from '../support/server.ts'
import { fakeVehicleBackend, gqlError, healthHandler, renderWithApollo, sessionHandler, stubViewport, user } from '../support/mocks.tsx'

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

/** The server: `answer` to the page's question, `saved` to a save (the page as it is after it). */
function settingsServer(answer: object = fixture(), saved: object = answer) {
  const calls: Record<string, unknown[]> = {}
  const record = (name: string, vars: unknown) => (calls[name] ??= []).push(vars)
  server.use(
    sessionHandler('STANDALONE', () => user({ isAdmin: true })),
    healthHandler,
    ...fakeVehicleBackend().handlers,
    graphql.query('SystemSettings', ({ variables }) => {
      record('SystemSettings', variables)
      return HttpResponse.json({ data: { systemSettings: answer } })
    }),
    graphql.mutation('UpdateSystemSettings', ({ variables }) => {
      record('UpdateSystemSettings', variables)
      return HttpResponse.json({ data: { updateSystemSettings: saved } })
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
  expect(logging.getByRole('combobox', { name: 'Everything else' })).toHaveAccessibleDescription('Logging__LogLevel__Default · the shipped default') // a choice's hint too
})

it('shows values as the server reads them however they are written, and sends a decimal as the server parses it', async () => {
  const ui = userEvent.setup()
  const calls = settingsServer({
    ...fixture(),
    groups: [
      {
        __typename: 'SystemSettingsGroup',
        name: 'SMTP',
        // A spelling the server still takes but no longer offers.
        settings: [setting({ key: 'Smtp:Security', kind: 'CHOICE', choices: ['Auto', 'None', 'StartTls', 'StartTlsWhenAvailable', 'SslOnConnect'], value: 'Ssl', isSet: true, source: 'FILE' })],
      },
      {
        __typename: 'SystemSettingsGroup',
        name: 'PHOTO_READING',
        settings: [
          setting({ key: 'Recognition:OpenAiCompatible:LogTraffic', kind: 'BOOLEAN', value: 'True', isSet: true, source: 'FILE' }), // a JSON true in the file, as .NET reads it
          setting({ key: 'Recognition:MinConfidence', kind: 'DECIMAL', value: '0.5', isSet: true, source: 'FILE' }),
        ],
      },
      { __typename: 'SystemSettingsGroup', name: 'LOGGING', settings: [setting({ key: 'Logging:LogLevel:Tankstat', kind: 'CHOICE', choices: LEVELS, value: 'debug', isSet: true, source: 'FILE' })] },
    ],
  })
  renderWithApollo(<App />, '/admin/settings')

  expect((await group('E-mail')).getByRole('combobox', { name: 'Connection security' })).toHaveTextContent('Ssl')
  expect((await group('Logging')).getByRole('combobox', { name: 'Tankstat' })).toHaveTextContent('Debug')
  const photos = await group('Photo reading')
  const traffic = photos.getByRole('switch', { name: 'Log every request and answer' })
  expect(traffic).toBeChecked()
  await ui.click(traffic)
  await ui.click(traffic)
  expect(photos.getByRole('button', { name: 'Save changes' })).toBeDisabled() // off and on again is no change
  await ui.clear(photos.getByLabelText('Minimum confidence (0 to 1)'))
  await ui.type(photos.getByLabelText('Minimum confidence (0 to 1)'), '0,5')
  expect(photos.getByRole('button', { name: 'Save changes' })).toBeDisabled() // the same number, typed with a comma
  await ui.type(photos.getByLabelText('Minimum confidence (0 to 1)'), '5')
  await ui.click(photos.getByRole('button', { name: 'Save changes' }))

  await waitFor(() => expect(calls.UpdateSystemSettings).toEqual([{ changes: [{ key: 'Recognition:MinConfidence', value: '0.55', clear: false }] }]))
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

it('a save starts the form afresh, even when its answer looks as before', async () => {
  const ui = userEvent.setup()
  const calls = settingsServer()
  renderWithApollo(<App />, '/admin/settings')

  const auth = await group('Sign-in')
  await ui.type(auth.getByLabelText('OIDC client secret'), 'zzzz-new')
  await ui.click(auth.getByRole('button', { name: 'Save changes' }))

  await screen.findByText('Saved. It takes effect after a restart.')
  expect(calls.UpdateSystemSettings).toEqual([{ changes: [{ key: 'Auth:Oidc:ClientSecret', value: 'zzzz-new', clear: false }] }])
  // A secret is never shown back, so the answer is the same as before: the typed one must still not stay behind to be sent again.
  expect(auth.getByLabelText('OIDC client secret')).toHaveValue('')
  expect(auth.getByRole('button', { name: 'Save changes' })).toBeDisabled()
})

it("a save's answer is the page from then on, with no second question to the server", async () => {
  const ui = userEvent.setup()
  const saved = fixture()
  saved.groups[0].settings[1] = setting({ key: 'Auth:PublicUrl', value: 'https://tank.example', isSet: true, restartPending: true })
  const calls = settingsServer(fixture(), saved)
  renderWithApollo(<App />, '/admin/settings')

  await ui.type((await group('Sign-in')).getByLabelText('Public address'), 'https://tank.example')
  await ui.click((await group('Sign-in')).getByRole('button', { name: 'Save changes' }))

  expect(await (await page()).findByText('2 settings wait for a restart.')).toBeInTheDocument()
  expect((await group('Sign-in')).getByLabelText('Public address')).toHaveValue('https://tank.example')
  expect(calls.SystemSettings).toHaveLength(1) // a refetch that failed would show a save that was written as refused
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

it('a mode change the server refuses says why, and the focus is back on Save once the confirmation is gone', async () => {
  const ui = userEvent.setup()
  settingsServer(fixture({ modeEditable: true }))
  server.use(
    graphql.mutation('UpdateSystemSettings', () =>
      HttpResponse.json(gqlError('https://id.example does not answer with an OpenID Connect discovery document (timeout).', 'VALIDATION_FAILED', 'settings.oidcUnreachable', { authority: 'https://id.example', reason: 'timeout' })),
    ),
  )
  renderWithApollo(<App />, '/admin/settings')
  const auth = await group('Sign-in')

  await ui.click(auth.getByRole('combobox', { name: 'Sign-in mode' }))
  await ui.click(await screen.findByRole('option', { name: 'Oidc' }))
  await ui.click(auth.getByRole('button', { name: 'Save changes' }))
  await ui.click(within(await screen.findByRole('alertdialog', { name: 'Change the sign-in mode?' })).getByRole('button', { name: 'Change the mode' }))

  expect(await auth.findByRole('alert')).toHaveTextContent('https://id.example does not answer with an OpenID Connect discovery document (timeout).')
  await waitFor(() => expect(auth.getByRole('button', { name: 'Save changes' })).toHaveFocus())
})

it('the mode confirmation says what a cleared mode and None come to: no sign-in at all', async () => {
  const ui = userEvent.setup()
  const calls = settingsServer(fixture({ modeEditable: true }))
  renderWithApollo(<App />, '/admin/settings')
  const auth = await group('Sign-in')

  await ui.click(auth.getByRole('combobox', { name: 'Sign-in mode' }))
  await ui.click(await screen.findByRole('option', { name: 'Default (not set)' }))
  await ui.click(auth.getByRole('button', { name: 'Save changes' }))
  const cleared = await screen.findByRole('alertdialog', { name: 'Change the sign-in mode?' })
  expect(cleared).toHaveAccessibleDescription(/becomes the one appsettings\.json sets \(None as shipped: no sign-in, everyone sees and changes everything\) at the next restart/)
  await ui.click(within(cleared).getByRole('button', { name: 'Cancel' }))
  await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())

  await ui.click(auth.getByRole('combobox', { name: 'Sign-in mode' }))
  await ui.click(await screen.findByRole('option', { name: 'None' }))
  await ui.click(auth.getByRole('button', { name: 'Save changes' }))
  const none = await screen.findByRole('alertdialog', { name: 'Change the sign-in mode?' })
  expect(none).toHaveAccessibleDescription(/becomes None \(no sign-in: everyone sees and changes everything\) at the next restart/)
  expect(calls.UpdateSystemSettings).toBeUndefined()
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
  expect(view.getByRole('status')).toHaveTextContent('The server is back; reloading the page.')
})

it('a restart asked while the server is known to be out of reach is not sent, says so, and waits for nothing', async () => {
  const ui = userEvent.setup()
  const reload = vi.spyOn(navigation, 'reload').mockImplementation(() => undefined)
  const calls = settingsServer()
  renderWithApollo(<App />, '/admin/settings')
  const view = await page()
  await view.findByText('/data/settings.json')
  await act(() => connectivity.failed()) // a request elsewhere got no answer; the page keeps what it loaded

  await ui.click(view.getByRole('button', { name: 'Restart server' }))
  await ui.click(within(await screen.findByRole('alertdialog', { name: 'Restart the server?' })).getByRole('button', { name: 'Restart server' }))

  expect(await view.findByText('The server cannot be reached right now. This works again once you are back online.')).toBeInTheDocument()
  expect(view.queryByText('Restarting the server…')).not.toBeInTheDocument()
  expect(view.getByRole('button', { name: 'Restart server' })).toBeEnabled()
  await new Promise((resolve) => setTimeout(resolve, 20)) // what a wait would take here (probes every 1 ms, 5 ms to go)
  expect(calls.RestartServer).toBeUndefined()
  expect(reload).not.toHaveBeenCalled()
})

it('a restart whose page was left meanwhile reloads nothing when the server is back', async () => {
  const ui = userEvent.setup()
  const reload = vi.spyOn(navigation, 'reload').mockImplementation(() => undefined)
  settingsServer()
  let down = true
  let answered = false
  const { unmount } = renderWithApollo(<App />, '/admin/settings')
  const view = await page()
  server.use(
    graphql.query('Health', () => {
      if (down) return HttpResponse.error()
      answered = true
      return HttpResponse.json({ data: { health: { status: 'ok', version: '1.2.4', databaseReachable: true } } })
    }),
  )

  await ui.click(view.getByRole('button', { name: 'Restart server' }))
  await ui.click(within(await screen.findByRole('alertdialog', { name: 'Restart the server?' })).getByRole('button', { name: 'Restart server' }))
  await view.findByText('Restarting the server…')
  unmount() // gone elsewhere: whatever is open by then is not reloaded under the person's hands
  down = false

  await waitFor(() => expect(answered).toBe(true))
  await new Promise((resolve) => setTimeout(resolve, 20))
  expect(reload).not.toHaveBeenCalled()
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
  await waitFor(() => expect(view.getByRole('button', { name: 'Restart server' })).toHaveFocus()) // back where the confirmation left it
  expect(reload).not.toHaveBeenCalled()
})

it('coming back to the page while the server is out of reach says so, and shows nothing of the earlier answer', async () => {
  stubViewport('desktop')
  const ui = userEvent.setup()
  settingsServer()
  renderWithApollo(<App />, '/admin/settings')
  await (await page()).findByText('/data/settings.json')

  await ui.click(screen.getByRole('link', { name: 'Home' }))
  await act(() => connectivity.failed())
  await ui.click(screen.getByRole('link', { name: 'System settings' }))

  const view = await page()
  expect(await view.findByText('This needs a connection to the server. It works again once you are back online.')).toBeInTheDocument()
  expect(view.queryByText('/data/settings.json')).not.toBeInTheDocument()
  expect(view.queryByRole('button', { name: 'Restart server' })).not.toBeInTheDocument()
})

it('only administrators can open it', async () => {
  server.use(sessionHandler('STANDALONE', () => user()), healthHandler, ...fakeVehicleBackend().handlers)
  renderWithApollo(<App />, '/admin/settings')

  await screen.findByRole('heading', { name: 'Your vehicles' }) // bounced back to the home page
  expect(screen.queryByRole('region', { name: 'System settings' })).not.toBeInTheDocument()
})
