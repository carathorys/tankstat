import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, beforeEach, expect, it, vi } from 'vitest'
import { RESTART_WAIT } from '../../../src/frontend/admin/restartFlow.ts'
import App from '../../../src/frontend/App.tsx'
import en from '../../../src/frontend/i18n/locales/en.json'
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
  defaultValue: null,
  ...over,
})

const MODES = ['None', 'Standalone', 'Oidc', 'ProxyHeader']
const LEVELS = ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical', 'None']

/**
 * The server's answer: the sign-in mode (Standalone unless `mode` says another) held by the environment, a list and a secret from the file,
 * the admin password switch waiting for a restart (the sign-in is read at start), the defaults applying at once.
 */
function fixture({ modeEditable = false, mode = 'Standalone' }: { modeEditable?: boolean; mode?: string } = {}) {
  return {
    __typename: 'SystemSettingsInfo',
    file: { __typename: 'SettingsFileInfo', path: '/data/settings.json', exists: true, ignored: false, problems: [] },
    restartPending: true,
    groups: [
      {
        __typename: 'SystemSettingsGroup',
        name: 'AUTH', appliesAtOnce: false,
        settings: [
          setting(
            modeEditable
              ? { key: 'Auth:Mode', kind: 'CHOICE', choices: MODES, value: mode, isSet: true, source: 'SHIPPED' }
              : { key: 'Auth:Mode', kind: 'CHOICE', choices: MODES, value: mode, isSet: true, source: 'ENVIRONMENT', editable: false },
          ),
          setting({ key: 'Auth:PublicUrl' }),
          setting({ key: 'Auth:AdminEmails', kind: 'LIST', value: 'a@example.com\nb@example.com', isSet: true, source: 'FILE' }),
          setting({ key: 'Auth:Oidc:ClientSecret', kind: 'SECRET', isSecret: true, isSet: true, source: 'FILE' }),
          setting({ key: 'Auth:Standalone:AllowAdminSetPassword', kind: 'BOOLEAN', restartPending: true }),
        ],
      },
      { __typename: 'SystemSettingsGroup', name: 'DEFAULTS', appliesAtOnce: true, settings: [setting({ key: 'Defaults:Currency', value: 'HUF', isSet: true, source: 'FILE' })] },
      { __typename: 'SystemSettingsGroup', name: 'LOGGING', appliesAtOnce: true, settings: [setting({ key: 'Logging:LogLevel:Default', kind: 'CHOICE', choices: LEVELS, value: 'Information', isSet: true, source: 'SHIPPED' })] },
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

const page = async () => within(await screen.findByRole('region', { name: 'Server settings' }))

/** A group's form, its tab chosen first (only the chosen tab shows its form; a tab with unsaved changes says so after its name). */
async function group(name: string) {
  const tab = await screen.findByRole('tab', { name: new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}( \\(not saved\\))?$`) })
  if (tab.getAttribute('aria-selected') !== 'true') await userEvent.setup().click(tab)
  return within(await screen.findByRole('region', { name }))
}

it('shows every group, where each value comes from, and what waits for a restart', async () => {
  settingsServer()
  renderWithApollo(<App />, '/admin/settings')

  const view = await page()
  expect(await view.findByText('1 setting waits for a restart.')).toBeInTheDocument()
  expect(view.getByText('/data/settings.json')).toBeInTheDocument()
  const auth = await group('Sign-in')
  expect(auth.getByText('Set by the environment: change it there.')).toBeInTheDocument() // read-only: its variable wins
  // The variable, and what the setting does, are behind its question mark: on hover, focus or a tap.
  await userEvent.setup().hover(auth.getByRole('button', { name: 'About Sign-in mode' }))
  expect(await screen.findByRole('tooltip')).toHaveTextContent(/^How users sign in: None .*Environment variable: Auth__Mode$/)
  expect(auth.getByRole('button', { name: 'About Sign-in mode' })).toHaveAccessibleDescription(/Environment variable: Auth__Mode$/)
  expect(auth.queryByRole('combobox', { name: 'Sign-in mode' })).not.toBeInTheDocument()
  expect(auth.getByText('Standalone')).toBeInTheDocument()
  expect(auth.getByLabelText('Public address')).toHaveValue('')
  expect(auth.getByLabelText('Administrators (OIDC and proxy sign-in)')).toHaveValue('a@example.com\nb@example.com')
  expect(auth.getByRole('switch', { name: 'Administrators may set passwords' })).toHaveAccessibleDescription(/takes effect after a restart/)
  expect(auth.getByText('Changes here take effect after a restart of the server.')).toBeInTheDocument()
  expect(auth.getByLabelText('OIDC client secret')).toHaveValue('') // never shown back
  expect(auth.getByLabelText('OIDC client secret')).toHaveAccessibleDescription(/Set\. Leave blank to keep it\./)
  expect(auth.getByRole('switch', { name: 'Administrators may set passwords' })).not.toBeChecked()
  const defaults = await group('Defaults for new vehicles')
  expect(defaults.getByLabelText('Currency')).toHaveValue('HUF')
  expect(defaults.getByLabelText('Currency')).not.toHaveAccessibleDescription(/restart/) // the defaults apply at once: nothing of them waits
  expect(defaults.getByText('Changes here take effect as soon as they are saved.')).toBeInTheDocument()
  expect(defaults.getByRole('button', { name: 'Save changes' })).toBeDisabled() // nothing changed yet
  const logging = await group('Logging')
  expect(logging.getByRole('combobox', { name: 'Everything else' })).toHaveTextContent('Information')
  expect(logging.getByRole('combobox', { name: 'Everything else' })).toHaveAccessibleDescription('the shipped default') // a choice's hint too; its variable is behind the question mark
})

it('shows values as the server reads them however they are written, and sends a decimal as the server parses it', async () => {
  const ui = userEvent.setup()
  const calls = settingsServer({
    ...fixture(),
    groups: [
      {
        __typename: 'SystemSettingsGroup',
        name: 'SMTP', appliesAtOnce: false,
        // A spelling the server still takes but no longer offers.
        settings: [setting({ key: 'Smtp:Security', kind: 'CHOICE', choices: ['Auto', 'None', 'StartTls', 'StartTlsWhenAvailable', 'SslOnConnect'], value: 'Ssl', isSet: true, source: 'FILE' })],
      },
      {
        __typename: 'SystemSettingsGroup',
        name: 'PHOTO_READING', appliesAtOnce: false,
        settings: [
          setting({ key: 'Recognition:OpenAiCompatible:LogTraffic', kind: 'BOOLEAN', value: 'True', isSet: true, source: 'FILE' }), // a JSON true in the file, as .NET reads it
          setting({ key: 'Recognition:MinConfidence', kind: 'DECIMAL', value: '0.5', isSet: true, source: 'FILE' }),
        ],
      },
      { __typename: 'SystemSettingsGroup', name: 'LOGGING', appliesAtOnce: true, settings: [setting({ key: 'Logging:LogLevel:Tankstat', kind: 'CHOICE', choices: LEVELS, value: 'debug', isSet: true, source: 'FILE' })] },
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

it('says a saved setting is in effect when the server says nothing of it waits', async () => {
  const ui = userEvent.setup()
  const saved = fixture()
  saved.groups[1].settings[0] = setting({ key: 'Defaults:Currency', value: 'USD', isSet: true, source: 'FILE' })
  settingsServer(fixture(), saved)
  renderWithApollo(<App />, '/admin/settings')

  const defaults = await group('Defaults for new vehicles')
  await ui.clear(defaults.getByLabelText('Currency'))
  await ui.type(defaults.getByLabelText('Currency'), 'USD')
  await ui.click(defaults.getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByText('Saved. It is in effect now.')).toBeInTheDocument()
  expect(screen.queryByText('Saved. It takes effect after a restart.')).not.toBeInTheDocument()
})

it('a save starts the form afresh, even when its answer looks as before', async () => {
  const ui = userEvent.setup()
  const calls = settingsServer(fixture({ mode: 'Oidc' })) // the OIDC secret is used only then
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
  const calls = settingsServer(fixture({ mode: 'Oidc' })) // the OIDC secret is used only then
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
  await ui.click(screen.getByRole('link', { name: 'Server settings' }))

  const view = await page()
  expect(await view.findByText('This needs a connection to the server. It works again once you are back online.')).toBeInTheDocument()
  expect(view.queryByText('/data/settings.json')).not.toBeInTheDocument()
  expect(view.queryByRole('button', { name: 'Restart server' })).not.toBeInTheDocument()
})

it('only administrators can open it', async () => {
  server.use(sessionHandler('STANDALONE', () => user()), healthHandler, ...fakeVehicleBackend().handlers)
  renderWithApollo(<App />, '/admin/settings')

  await screen.findByRole('heading', { name: 'Your vehicles' }) // bounced back to the home page
  expect(screen.queryByRole('region', { name: 'Server settings' })).not.toBeInTheDocument()
})

/** A server whose sign-in mode can be changed here, with the settings each mode uses and the mail server's. */
function modesFixture(mode = 'Standalone') {
  const base = fixture({ modeEditable: true, mode })
  return {
    ...base,
    groups: [
      {
        ...base.groups[0],
        settings: [
          ...base.groups[0].settings,
          setting({ key: 'Auth:AccessTokenMinutes', kind: 'INTEGER', value: '15', isSet: true, source: 'SHIPPED' }),
          setting({ key: 'Auth:Oidc:Authority', value: 'https://id.example', isSet: true, source: 'FILE' }),
        ],
      },
      {
        __typename: 'SystemSettingsGroup',
        name: 'SMTP', appliesAtOnce: false,
        settings: [setting({ key: 'Smtp:Host', value: 'mail.example', isSet: true, source: 'FILE' }), setting({ key: 'Smtp:Port', kind: 'INTEGER', value: '587', isSet: true, source: 'SHIPPED' })],
      },
      ...base.groups.slice(1),
    ],
  }
}

it('shows a tab per group, opens the one the address names, and keeps what was typed in a tab while another is open', async () => {
  const ui = userEvent.setup()
  settingsServer()
  renderWithApollo(<App />, '/admin/settings?tab=defaults')

  const tabs = await screen.findAllByRole('tab')
  expect(tabs.map((t) => t.textContent)).toEqual(['Sign-in', 'Defaults for new vehicles', 'Logging'])
  expect(screen.getByRole('tab', { name: 'Defaults for new vehicles' })).toHaveAttribute('aria-selected', 'true')
  expect(screen.queryByRole('region', { name: 'Sign-in' })).not.toBeInTheDocument() // only the chosen tab shows its form

  const defaults = await group('Defaults for new vehicles')
  await ui.type(defaults.getByLabelText('Currency'), 'X')
  expect(screen.getByRole('tab', { name: 'Defaults for new vehicles (not saved)' })).toBeInTheDocument()
  await group('Logging')
  expect(screen.queryByRole('region', { name: 'Defaults for new vehicles' })).not.toBeInTheDocument()

  const again = await group('Defaults for new vehicles')
  expect(again.getByLabelText('Currency')).toHaveValue('HUFX')
  expect(again.getByRole('button', { name: 'Save changes' })).toBeEnabled()
})

/** A section's header (a button that folds it), found by its title; its name goes on with what the section says of itself. */
const sectionHeader = (view: ReturnType<typeof within>, title: string) => view.getByRole('button', { name: new RegExp(`^${title.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}`) })

it('lays a tab out in sections, folded ones say what they hold is not used and why, and a section with unsaved changes says so', async () => {
  const ui = userEvent.setup()
  settingsServer(modesFixture('Standalone'))
  renderWithApollo(<App />, '/admin/settings')

  const auth = await group('Sign-in')
  expect(auth.getAllByRole('heading', { level: 3 }).map((h) => h.textContent?.trim())).toEqual([
    'Sessions (Standalone and OIDC)',
    'Standalone sign-in',
    'OpenID Connect (OIDC) Not used while Sign-in mode is Standalone; only with Oidc.',
    expect.stringMatching(/^Administrators from the provider/),
  ])
  expect(sectionHeader(auth, 'Standalone sign-in')).toHaveAttribute('aria-expanded', 'true') // used: open
  const oidc = sectionHeader(auth, 'OpenID Connect (OIDC)')
  expect(oidc).toHaveAttribute('aria-expanded', 'false') // not used: folded, the reason in its header
  expect(oidc).toHaveAccessibleName('OpenID Connect (OIDC) Not used while Sign-in mode is Standalone; only with Oidc.')
  // The header controls its panel's region, which takes that id from it: no other element carries the id too (axe does not catch that).
  expect(document.getElementById(oidc.getAttribute('aria-controls')!)).toHaveAttribute('role', 'region')
  const ids = [...document.body.querySelectorAll('[id]')].map((e) => e.id)
  expect(ids.filter((id, i) => ids.indexOf(id) !== i)).toEqual([])
  expect(auth.getByLabelText('OIDC authority')).not.toHaveAccessibleDescription(/Not used/) // said once, in the header

  await ui.click(oidc) // can still be opened, to see what is set
  expect(oidc).toHaveAttribute('aria-expanded', 'true')
  expect(auth.getByLabelText('OIDC authority')).toBeDisabled()
  expect(auth.getByLabelText('OIDC authority')).toHaveValue('https://id.example')
  expect(auth.getByRole('checkbox', { name: 'Remove the stored value' })).toBeDisabled()

  await ui.type(auth.getByLabelText('Public address'), 'https://tank.example')
  expect(sectionHeader(auth, 'Standalone sign-in')).toHaveAccessibleName('Standalone sign-in changes not saved yet')
  await ui.click(sectionHeader(auth, 'Standalone sign-in'))
  expect(sectionHeader(auth, 'Standalone sign-in')).toHaveAttribute('aria-expanded', 'false') // folded by hand, it still says so
  expect(sectionHeader(auth, 'Standalone sign-in')).toHaveAccessibleName('Standalone sign-in changes not saved yet')
})

it('settings the sign-in mode leaves unused are shown but cannot be changed, and say why, in every group, as soon as the mode is chosen', async () => {
  const ui = userEvent.setup()
  settingsServer(modesFixture('Standalone'))
  renderWithApollo(<App />, '/admin/settings')

  // Standalone: its own settings and the sessions' are used, the OIDC and proxy ones not.
  let auth = await group('Sign-in')
  expect(auth.getByLabelText('Public address')).toBeEnabled()
  expect(auth.getByRole('switch', { name: 'Administrators may set passwords' })).toBeEnabled()
  expect(auth.getByLabelText('Access cookie (minutes)')).toBeEnabled()
  expect(auth.getByLabelText('OIDC authority')).toBeDisabled()
  expect(auth.getByLabelText('OIDC client secret')).toBeDisabled()
  expect(auth.getByLabelText('Administrators (OIDC and proxy sign-in)')).toBeDisabled()
  expect(sectionHeader(auth, 'Administrators from the provider')).toHaveAccessibleName(/Not used while Sign-in mode is Standalone; only with Oidc or ProxyHeader\.$/)
  expect((await group('E-mail')).getByLabelText('Port')).toBeEnabled()

  // None, chosen and not saved yet: everything that needs a sign-in greys out and folds at once, the e-mail tab as a whole.
  auth = await group('Sign-in')
  await ui.click(auth.getByRole('combobox', { name: 'Sign-in mode' }))
  await ui.click(await screen.findByRole('option', { name: 'None' }))
  expect(auth.getByLabelText('Public address')).toBeDisabled()
  expect(auth.getByLabelText('Administrators may set passwords')).toBeDisabled() // folded away, so found by its label
  expect(sectionHeader(auth, 'Standalone sign-in')).toHaveAttribute('aria-expanded', 'false')
  expect(sectionHeader(auth, 'Standalone sign-in')).toHaveAccessibleName('Standalone sign-in Not used while Sign-in mode is None; only with Standalone.')
  expect(sectionHeader(auth, 'Sessions (Standalone and OIDC)')).toHaveAccessibleName(/only with Standalone or Oidc\.$/)
  expect(auth.getByRole('combobox', { name: 'Sign-in mode' })).not.toHaveAttribute('aria-disabled', 'true') // the way back
  expect(auth.getByRole('button', { name: 'Save changes' })).toBeEnabled()

  const mail = await group('E-mail')
  expect(mail.getByRole('note')).toHaveTextContent('None of these settings is used now: Not used while Sign-in mode is None; only with Standalone. You can still see what they are set to.')
  expect(sectionHeader(mail, 'Connection to the mail server')).toHaveAccessibleName('Connection to the mail server Not used now') // the reason is said once, above
  expect(sectionHeader(mail, 'Connection to the mail server')).toHaveAttribute('aria-expanded', 'false')
  expect(mail.getByLabelText('Mail server')).toBeDisabled()
  expect(mail.getByLabelText('Mail server')).toHaveValue('mail.example') // still there
  expect(mail.getByLabelText('Port')).toBeDisabled()
  expect((await group('Defaults for new vehicles')).getByLabelText('Currency')).toBeEnabled() // used in every mode

  // Back to Standalone: all of it can be changed again, and opens again.
  auth = await group('Sign-in')
  await ui.click(auth.getByRole('combobox', { name: 'Sign-in mode' }))
  await ui.click(await screen.findByRole('option', { name: 'Standalone' }))
  expect(auth.getByLabelText('Public address')).toBeEnabled()
  expect(sectionHeader(auth, 'Standalone sign-in')).toHaveAttribute('aria-expanded', 'true')
  expect((await group('E-mail')).queryByRole('note')).not.toBeInTheDocument()
})

it('a mode the settings file holds, cleared to the default, greys nothing out: what is below the file is not known here', async () => {
  const ui = userEvent.setup()
  const base = modesFixture('Standalone')
  base.groups[0].settings[0] = setting({ key: 'Auth:Mode', kind: 'CHOICE', choices: MODES, value: 'Standalone', isSet: true, source: 'FILE', defaultValue: 'None' })
  settingsServer(base)
  renderWithApollo(<App />, '/admin/settings')
  const auth = await group('Sign-in')

  await ui.click(auth.getByRole('combobox', { name: 'Sign-in mode' }))
  await ui.click(await screen.findByRole('option', { name: 'Default (not set)' }))

  expect(auth.getByLabelText('OIDC authority')).toBeEnabled()
  expect(auth.getByLabelText('Public address')).toBeEnabled()
})

it('a mode only the shipped file sets stays what it is when cleared, so it still decides', async () => {
  const ui = userEvent.setup()
  settingsServer(modesFixture('Standalone')) // the mode comes from appsettings.json: clearing it here changes nothing
  renderWithApollo(<App />, '/admin/settings')
  const auth = await group('Sign-in')

  await ui.click(auth.getByRole('combobox', { name: 'Sign-in mode' }))
  await ui.click(await screen.findByRole('option', { name: 'Default (not set)' }))

  expect(auth.getByLabelText('OIDC authority')).toBeDisabled()
  expect(auth.getByLabelText('Public address')).toBeEnabled()
})

it('a setting nobody set shows the value it runs with, its built-in default, and leaving it there saves nothing', async () => {
  const ui = userEvent.setup()
  const base = modesFixture('Standalone')
  base.groups[0].settings = base.groups[0].settings.map((s) =>
    s.key === 'Auth:AccessTokenMinutes' ? setting({ key: s.key, kind: 'INTEGER', defaultValue: '15' }) : s.key === 'Auth:Standalone:AllowAdminSetPassword' ? setting({ key: s.key, kind: 'BOOLEAN', defaultValue: 'false' }) : s,
  )
  const calls = settingsServer(base)
  renderWithApollo(<App />, '/admin/settings')
  const auth = await group('Sign-in')

  expect(auth.getByLabelText('Access cookie (minutes)')).toHaveValue('15')
  expect(auth.getByLabelText('Access cookie (minutes)')).toHaveAccessibleDescription('the built-in default')
  expect(auth.getByRole('button', { name: 'Save changes' })).toBeDisabled() // shown, not changed
  await ui.clear(auth.getByLabelText('Access cookie (minutes)'))
  expect(auth.getByRole('button', { name: 'Save changes' })).toBeDisabled() // emptied: nothing is set, so nothing to clear
  await ui.type(auth.getByLabelText('Access cookie (minutes)'), '30')
  await ui.click(auth.getByRole('button', { name: 'Save changes' }))

  await waitFor(() => expect(calls.UpdateSystemSettings).toEqual([{ changes: [{ key: 'Auth:AccessTokenMinutes', value: '30', clear: false }] }]))
})

it('a mode the environment holds decides as well, and the read-only settings say they are unused too', async () => {
  const base = fixture({ mode: 'None' })
  settingsServer({
    ...base,
    groups: [{ ...base.groups[0], settings: [...base.groups[0].settings, setting({ key: 'Auth:Oidc:Authority', value: 'https://id.example', isSet: true, source: 'ENVIRONMENT', editable: false })] }],
  })
  renderWithApollo(<App />, '/admin/settings')

  const auth = await group('Sign-in')
  expect(auth.getByLabelText('Public address')).toBeDisabled()
  expect(sectionHeader(auth, 'OpenID Connect (OIDC)')).toHaveAccessibleName('OpenID Connect (OIDC) Not used while Sign-in mode is None; only with Oidc.')
  expect(auth.getAllByText('Set by the environment: change it there.')).toHaveLength(2) // the mode and the authority; the reason is in the header
})

it('no section is named like a setting in it: a panel is named by its header, and two names alike would be one for a screen reader', () => {
  const labels = new Set<string>(Object.values(en.systemSettings.fields))
  expect(Object.values(en.systemSettings.sections).filter((title) => labels.has(title))).toEqual([])
})

it('a mail server left empty leaves the rest of the e-mail settings unused, and photo reading without a provider its own', async () => {
  const ui = userEvent.setup()
  const base = modesFixture('Standalone')
  settingsServer({
    ...base,
    groups: [
      { __typename: 'SystemSettingsGroup', name: 'SMTP', appliesAtOnce: false, settings: [setting({ key: 'Smtp:Host' }), setting({ key: 'Smtp:Port', kind: 'INTEGER', value: '587', isSet: true, source: 'SHIPPED' })] },
      {
        __typename: 'SystemSettingsGroup',
        name: 'PHOTO_READING', appliesAtOnce: false,
        settings: [
          setting({ key: 'Recognition:Provider', kind: 'CHOICE', choices: ['None', 'OpenAiCompatible'], defaultValue: 'None' }), // set nowhere: its built-in None
          setting({ key: 'Recognition:OpenAiCompatible:Model', value: 'llava', isSet: true, source: 'FILE' }),
        ],
      },
      base.groups[0],
    ],
  })
  renderWithApollo(<App />, '/admin/settings')

  const mail = await group('E-mail')
  expect(mail.getByLabelText('Mail server')).toBeEnabled()
  expect(mail.getByLabelText('Port')).toBeDisabled()
  expect(mail.getByLabelText('Port')).toHaveAccessibleDescription('the shipped default Not used while Mail server is empty.') // the badge first, as it is seen
  await ui.type(mail.getByLabelText('Mail server'), 'mail.example')
  expect(mail.getByLabelText('Port')).toBeEnabled()

  const photos = await group('Photo reading')
  expect(photos.getByLabelText('Model')).toBeDisabled() // no provider set anywhere: the built-in None
  expect(sectionHeader(photos, 'Model server')).toHaveAccessibleName('Model server Not used while Provider is None; only with OpenAiCompatible.')
  await ui.click(photos.getByRole('combobox', { name: 'Provider' })) // above the sections, never folded
  await ui.click(await screen.findByRole('option', { name: 'OpenAiCompatible' }))
  expect(photos.getByLabelText('Model')).toBeEnabled()
  expect(sectionHeader(photos, 'Model server')).toHaveAttribute('aria-expanded', 'true')
})
