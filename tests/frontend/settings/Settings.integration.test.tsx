import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, onTestFinished, vi } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { i18n } from '../../../src/frontend/i18n/index.ts'
import { adminSession, fakeSettingsBackend, fakeVehicle, fakeVehicleBackend, gqlError, healthHandler, renderWithApollo, sessionHandler, silenceConsoleError, stubViewport } from '../support/mocks.tsx'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

const cars = [fakeVehicle({ id: 'a', name: 'Beta', licensePlate: 'BBB-2', ownerName: 'Zoe' }), fakeVehicle({ id: 'b', name: 'alpha', licensePlate: 'AAA-1', ownerName: 'Bob' })]

/** A signed-in administrator on the vehicle grid, with the server keeping settings. */
function setup(settings = fakeSettingsBackend()) {
  stubViewport('desktop')
  const vehicles = fakeVehicleBackend(cars)
  server.use(adminSession(), healthHandler, ...settings.handlers, ...vehicles.handlers)
  const view = renderWithApollo(<App />, '/vehicles')
  return { settings, vehicles, view, ui: userEvent.setup() }
}

// Also while the Columns popover is open (it is modal: the page behind it is hidden from assistive technology meanwhile).
const headers = () => screen.getAllByRole('columnheader', { hidden: true }).map((h) => h.textContent).filter((text) => text && text !== 'Actions')
const lastRequest = (reqs: Record<string, unknown>[]) => reqs[reqs.length - 1]

it('hiding the sidebar is saved with the account and shows on another device', async () => {
  const { settings, view, ui } = setup()
  await screen.findByRole('heading', { name: 'Vehicles' })

  await ui.click(screen.getByRole('button', { name: 'Hide menu' }))

  await waitFor(() => expect(settings.state.calls.UpdateUiSettings).toEqual([{ input: { navOpen: false } }]))
  expect(window.localStorage.getItem('tankstat.nav.open')).toBe('false')

  view.unmount()
  window.localStorage.clear() // another device: the browser remembers nothing, the server does
  renderWithApollo(<App />, '/vehicles')
  await screen.findByRole('heading', { name: 'Vehicles' })
  await waitFor(() => expect(screen.queryByRole('navigation', { name: 'Main navigation' })).not.toBeInTheDocument())
})

it('a choice made before the server answers is not undone by the answer', async () => {
  stubViewport('desktop')
  const settings = fakeSettingsBackend({ navOpen: true, language: 'hu' })
  let answer!: () => void
  const held = new Promise<void>((resolve) => (answer = resolve))
  server.use(
    adminSession(),
    healthHandler,
    graphql.query('UiSettings', async () => {
      await held
      return HttpResponse.json({ data: { uiSettings: settings.state.settings } })
    }),
    ...settings.handlers,
    ...fakeVehicleBackend(cars).handlers,
  )
  const ui = userEvent.setup()
  renderWithApollo(<App />, '/vehicles')
  await screen.findByRole('heading', { name: 'Vehicles' })

  await ui.click(screen.getByRole('button', { name: 'Hide menu' })) // before the answer ("open") lands
  answer()

  await screen.findByRole('heading', { name: 'Járművek' }) // the answer has arrived: its language applied ...
  expect(screen.queryByRole('navigation', { name: 'Main navigation' })).not.toBeInTheDocument() // ... its older sidebar choice did not
  expect(settings.state.calls.UpdateUiSettings).toEqual([{ input: { navOpen: false } }])
})

it("a grid's columns are saved with the account, and a browser that never saw them starts from the server's copy", async () => {
  const { settings, ui, view } = setup()
  await screen.findByText('Beta')

  await ui.click(screen.getByRole('button', { name: 'Columns' }))
  await ui.click(within(await screen.findByRole('dialog')).getByRole('checkbox', { name: 'Show License plate' }))

  await waitFor(() => expect(headers()).toEqual(['Name', 'Fuel', 'Owner', 'Refuelings']))
  await waitFor(() =>
    expect(settings.state.calls.SaveGridSettings).toEqual([
      { input: { gridId: 'vehicles', order: ['name', 'licensePlate', 'fuelType', 'owner', 'refuelings'], hidden: ['licensePlate'], pageSize: 25, sortColumn: 'name', sortDirection: 'ASC' } },
    ]),
  )

  view.unmount()
  window.localStorage.clear()
  const again = fakeVehicleBackend(cars)
  server.use(...again.handlers)
  renderWithApollo(<App />, '/vehicles')
  await screen.findByText('Beta')

  await waitFor(() => expect(headers()).toEqual(['Name', 'Fuel', 'Owner', 'Refuelings']))
  expect(lastRequest(again.state.requests.Vehicles)).toMatchObject({ withLicensePlate: false })
})

it('page size and reset reach the server too', async () => {
  const { settings, ui, vehicles } = setup(fakeSettingsBackend({ grids: [{ gridId: 'vehicles', order: ['name', 'licensePlate', 'fuelType', 'owner', 'refuelings'], hidden: ['fuelType'], pageSize: 10, sortColumn: 'name', sortDirection: 'ASC' }] }))
  await screen.findByText('Beta')
  await waitFor(() => expect(headers()).toEqual(['Name', 'License plate', 'Owner', 'Refuelings'])) // the server's copy (a device that never saw it asks once with the defaults first)
  await waitFor(() => expect(lastRequest(vehicles.state.requests.Vehicles)).toMatchObject({ withFuelType: false, take: 10 })) // the rows were asked for accordingly

  await ui.click(screen.getByRole('combobox', { name: 'Rows per page' }))
  await ui.click(await screen.findByRole('option', { name: '50' }))
  await waitFor(() => expect(settings.state.settings.grids[0].pageSize).toBe(50))

  await ui.click(screen.getByRole('button', { name: 'Columns' }))
  await ui.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Reset to defaults' }))
  await waitFor(() => expect(settings.state.calls.ResetGridSettings).toEqual([{ gridId: 'vehicles' }]))
  await waitFor(() => expect(headers()).toEqual(['Name', 'License plate', 'Fuel', 'Owner', 'Refuelings']))
})

it('a grid reset in this session opens with the defaults again, not with the server’s older copy', async () => {
  const { ui } = setup(fakeSettingsBackend({ grids: [{ gridId: 'vehicles', order: ['name', 'licensePlate', 'fuelType', 'owner', 'refuelings'], hidden: ['fuelType'], pageSize: 10, sortColumn: 'name', sortDirection: 'ASC' }] }))
  await screen.findByText('Beta')
  await waitFor(() => expect(headers()).toEqual(['Name', 'License plate', 'Owner', 'Refuelings']))

  await ui.click(screen.getByRole('button', { name: 'Columns' }))
  await ui.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Reset to defaults' }))
  await ui.keyboard('{Escape}')
  await waitFor(() => expect(headers()).toEqual(['Name', 'License plate', 'Fuel', 'Owner', 'Refuelings']))

  // Away and back: the grid starts afresh from what this session knows.
  const nav = screen.getByRole('navigation', { name: 'Main navigation' })
  await ui.click(within(nav).getByRole('link', { name: 'Home' }))
  await screen.findByRole('heading', { name: 'Your vehicles' })
  await ui.click(within(screen.getByRole('navigation', { name: 'Main navigation' })).getByRole('link', { name: 'Vehicles' }))
  await screen.findByText('Beta')

  expect(headers()).toEqual(['Name', 'License plate', 'Fuel', 'Owner', 'Refuelings'])
})

it('the language is saved with the account and applied on another device; an unknown code is ignored', async () => {
  const { settings, ui, view } = setup()
  await screen.findByRole('heading', { name: 'Vehicles' })

  await ui.click(screen.getByRole('button', { name: 'Language' }))
  await ui.click(await screen.findByRole('menuitemradio', { name: 'Magyar' }))

  await screen.findByRole('heading', { name: 'Járművek' })
  await waitFor(() => expect(settings.state.calls.UpdateUiSettings).toEqual([{ input: { language: 'hu' } }]))

  view.unmount()
  await i18n.changeLanguage('en') // another device starts in English ...
  const second = renderWithApollo(<App />, '/vehicles')
  await screen.findByRole('heading', { name: 'Járművek' }) // ... and follows the account
  expect(document.documentElement.lang).toBe('hu')

  second.unmount()
  settings.state.settings.language = 'de' // not a language of this app
  await i18n.changeLanguage('en')
  renderWithApollo(<App />, '/vehicles')
  await screen.findByRole('heading', { name: 'Vehicles' })
  expect(document.documentElement.lang).toBe('en')
})

it('the colour mode is saved with the account and applied on another device', async () => {
  const { settings, ui, view } = setup()
  await screen.findByRole('heading', { name: 'Vehicles' })
  expect(document.documentElement).toHaveClass('dark') // nothing chosen yet

  await ui.click(screen.getByRole('button', { name: 'Appearance' }))
  expect(await screen.findByRole('menuitemradio', { name: 'Colour mode: Dark' })).toHaveAttribute('aria-checked', 'true')
  await ui.click(screen.getByRole('menuitemradio', { name: 'Colour mode: Light' }))

  expect(document.documentElement).toHaveClass('light')
  expect(window.localStorage.getItem('tankstat.colorMode')).toBe('light')
  await waitFor(() => expect(settings.state.calls.UpdateUiSettings).toEqual([{ input: { colorMode: 'LIGHT' } }]))

  view.unmount()
  window.localStorage.clear() // another device: the browser remembers nothing, the server does
  renderWithApollo(<App />, '/vehicles')
  await screen.findByRole('heading', { name: 'Vehicles' })
  await waitFor(() => expect(window.localStorage.getItem('tankstat.colorMode')).toBe('light'))
  expect(document.documentElement).toHaveClass('light')
})

it('the surface style is switched at once, saved with the account and applied on another device', async () => {
  const { settings, ui, view } = setup()
  await screen.findByRole('heading', { name: 'Vehicles' })
  expect(document.documentElement).not.toHaveClass('surface-opaque') // glossy: no class

  await ui.click(screen.getByRole('button', { name: 'Appearance' }))
  expect(await screen.findByRole('menuitemradio', { name: 'Surfaces: Glossy' })).toHaveAttribute('aria-checked', 'true')
  await ui.click(screen.getByRole('menuitemradio', { name: 'Surfaces: Opaque' }))

  expect(document.documentElement).toHaveClass('surface-opaque')
  expect(document.documentElement).toHaveClass('dark') // the colour mode is untouched
  expect(window.localStorage.getItem('tankstat.surface')).toBe('opaque')
  await waitFor(() => expect(settings.state.calls.UpdateUiSettings).toEqual([{ input: { surface: 'OPAQUE' } }]))

  view.unmount()
  window.localStorage.clear() // another device: the browser remembers nothing, the server does
  document.documentElement.classList.remove('surface-opaque')
  renderWithApollo(<App />, '/vehicles')
  await screen.findByRole('heading', { name: 'Vehicles' })
  await waitFor(() => expect(document.documentElement).toHaveClass('surface-opaque'))
  expect(window.localStorage.getItem('tankstat.surface')).toBe('opaque')
})

it('a surface style chosen before the server answers is not undone by the answer', async () => {
  stubViewport('desktop')
  const settings = fakeSettingsBackend({ surface: 'OPAQUE', language: 'hu' })
  let answer!: () => void
  const held = new Promise<void>((resolve) => (answer = resolve))
  server.use(
    adminSession(),
    healthHandler,
    graphql.query('UiSettings', async () => {
      await held
      return HttpResponse.json({ data: { uiSettings: settings.state.settings } })
    }),
    ...settings.handlers,
    ...fakeVehicleBackend(cars).handlers,
  )
  const ui = userEvent.setup()
  renderWithApollo(<App />, '/vehicles')
  await screen.findByRole('heading', { name: 'Vehicles' })

  await ui.click(screen.getByRole('button', { name: 'Appearance' }))
  await ui.click(await screen.findByRole('menuitemradio', { name: 'Surfaces: Transparent' }))
  answer()

  await screen.findByRole('heading', { name: 'Járművek' }) // the answer has arrived: its language applied ...
  expect(document.documentElement).toHaveClass('surface-transparent') // ... its older surface style did not
  expect(settings.state.calls.UpdateUiSettings).toEqual([{ input: { surface: 'TRANSPARENT' } }])
  expect(document.documentElement).not.toHaveClass('surface-opaque')
})

it('a colour mode chosen before the server answers is not undone by the answer', async () => {
  stubViewport('desktop')
  const settings = fakeSettingsBackend({ colorMode: 'LIGHT', language: 'hu' })
  let answer!: () => void
  const held = new Promise<void>((resolve) => (answer = resolve))
  server.use(
    adminSession(),
    healthHandler,
    graphql.query('UiSettings', async () => {
      await held
      return HttpResponse.json({ data: { uiSettings: settings.state.settings } })
    }),
    ...settings.handlers,
    ...fakeVehicleBackend(cars).handlers,
  )
  const ui = userEvent.setup()
  renderWithApollo(<App />, '/vehicles')
  await screen.findByRole('heading', { name: 'Vehicles' })

  await ui.click(screen.getByRole('button', { name: 'Appearance' }))
  await ui.click(await screen.findByRole('menuitemradio', { name: 'Colour mode: System' }))
  answer()

  await screen.findByRole('heading', { name: 'Járművek' }) // the answer has arrived: its language applied ...
  expect(window.localStorage.getItem('tankstat.colorMode')).toBe('system') // ... its older colour mode did not
  expect(settings.state.calls.UpdateUiSettings).toEqual([{ input: { colorMode: 'SYSTEM' } }])
})

it('System follows the device: light on a device set to light', async () => {
  stubViewport('desktop', { scheme: 'light' })
  server.use(adminSession(), healthHandler, ...fakeSettingsBackend().handlers, ...fakeVehicleBackend(cars).handlers)
  const ui = userEvent.setup()
  renderWithApollo(<App />, '/vehicles')
  await screen.findByRole('heading', { name: 'Vehicles' })
  expect(document.documentElement).toHaveClass('dark') // the app's own default, whatever the device

  await ui.click(screen.getByRole('button', { name: 'Appearance' }))
  await ui.click(await screen.findByRole('menuitemradio', { name: 'Colour mode: System' }))

  expect(document.documentElement).toHaveClass('light')
})

it('a visitor who is not signed in changes only the browser', async () => {
  stubViewport('desktop')
  const settings = fakeSettingsBackend()
  server.use(sessionHandler('STANDALONE', () => null), healthHandler, ...settings.handlers)
  const ui = userEvent.setup()
  renderWithApollo(<App />, '/')
  await screen.findByRole('heading', { name: 'Sign in' })

  await ui.click(screen.getByRole('button', { name: 'Language' }))
  await ui.click(await screen.findByRole('menuitemradio', { name: 'Magyar' }))

  await screen.findByRole('heading', { name: 'Bejelentkezés' })
  await ui.click(screen.getByRole('button', { name: 'Megjelenés' }))
  await ui.click(await screen.findByRole('menuitemradio', { name: 'Színmód: Világos' }))

  expect(document.documentElement).toHaveClass('light')
  expect(settings.state.requests.UiSettings).toBe(0)
  expect(settings.state.calls.UpdateUiSettings).toBeUndefined()
})

it('a surface style a visitor chooses on the sign-in screen stays in the browser', async () => {
  stubViewport('desktop')
  const settings = fakeSettingsBackend()
  server.use(sessionHandler('STANDALONE', () => null), healthHandler, ...settings.handlers)
  onTestFinished(() => document.documentElement.classList.remove('surface-opaque'))
  const ui = userEvent.setup()
  renderWithApollo(<App />, '/')
  await screen.findByRole('heading', { name: 'Sign in' })

  await ui.click(screen.getByRole('button', { name: 'Appearance' }))
  await ui.click(await screen.findByRole('menuitemradio', { name: 'Surfaces: Opaque' }))

  expect(document.documentElement).toHaveClass('surface-opaque')
  expect(window.localStorage.getItem('tankstat.surface')).toBe('opaque')
  expect(settings.state.requests.UiSettings).toBe(0)
  expect(settings.state.calls.UpdateUiSettings).toBeUndefined()
})

it('a save that fails stays in the browser and is only reported to the console', async () => {
  const consoleError = silenceConsoleError()
  const { ui } = setup()
  server.use(graphql.mutation('SaveGridSettings', () => new HttpResponse(null, { status: 500 })))
  await screen.findByText('Beta')

  await ui.click(screen.getByRole('button', { name: 'Columns' }))
  await ui.click(within(await screen.findByRole('dialog')).getByRole('checkbox', { name: 'Show License plate' }))

  await waitFor(() => expect(headers()).toEqual(['Name', 'Fuel', 'Owner', 'Refuelings']))
  expect(JSON.parse(window.localStorage.getItem('tankstat.grid.vehicles')!).hidden).toEqual(['licensePlate'])
  await waitFor(() => expect(consoleError).toHaveBeenCalledWith(expect.stringContaining('SaveGridSettings'), expect.anything()))
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
})

it('a save the server refuses leaves a note in the console, nothing more', async () => {
  const consoleWarn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
  onTestFinished(() => {
    consoleWarn.mockRestore()
  })
  const { ui } = setup()
  server.use(graphql.mutation('SaveGridSettings', () => HttpResponse.json(gqlError('Page size out of range', 'VALIDATION_FAILED', 'settings.pageSizeInvalid', { min: 1, max: 500 }))))
  await screen.findByText('Beta')

  await ui.click(screen.getByRole('combobox', { name: 'Rows per page' }))
  await ui.click(await screen.findByRole('option', { name: '50' }))

  await waitFor(() => expect(consoleWarn).toHaveBeenCalledWith('A settings save was refused', ['settings.pageSizeInvalid']))
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  expect(JSON.parse(window.localStorage.getItem('tankstat.grid.vehicles')!).pageSize).toBe(50) // the browser keeps the choice
})

it('changes made while a save is on its way are sent after it, the newest only', async () => {
  const { settings, ui } = setup()
  let release!: () => void
  const held = new Promise<void>((resolve) => (release = resolve))
  let saves = 0
  server.use(
    graphql.mutation('SaveGridSettings', async ({ variables }) => {
      saves++
      if (saves === 1) await held // the first save hangs ...
      return HttpResponse.json({ data: { saveGridSettings: variables.input } })
    }),
  )
  await screen.findByText('Beta')

  await ui.click(screen.getByRole('button', { name: 'Columns' }))
  const dialog = await screen.findByRole('dialog')
  await ui.click(within(dialog).getByRole('checkbox', { name: 'Show License plate' })) // ... while two more changes are made
  await ui.click(within(dialog).getByRole('checkbox', { name: 'Show Fuel' }))
  await ui.click(within(dialog).getByRole('checkbox', { name: 'Show Owner' }))
  expect(saves).toBe(1)

  release()

  await waitFor(() => expect(saves).toBe(2)) // one request for the two, with the latest value
  await new Promise((resolve) => setTimeout(resolve, 50))
  expect(saves).toBe(2)
  expect(settings.state.calls.SaveGridSettings).toBeUndefined() // the fake's own handler was replaced; the requests above are the record
})
