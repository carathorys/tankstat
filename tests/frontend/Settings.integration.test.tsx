import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../src/frontend/App.tsx'
import { i18n } from '../../src/frontend/i18n/index.ts'
import { adminSession, fakeSettingsBackend, fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, silenceConsoleError, stubViewport } from './mocks.tsx'
import { server } from './server.ts'

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

const headers = () => screen.getAllByRole('columnheader').map((h) => h.textContent).filter((text) => text && text !== 'Actions')
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
  const settings = fakeSettingsBackend({ navOpen: true })
  server.use(
    adminSession(),
    healthHandler,
    graphql.query('UiSettings', async () => {
      await delay(300)
      return HttpResponse.json({ data: { uiSettings: settings.state.settings } })
    }),
    ...settings.handlers,
    ...fakeVehicleBackend(cars).handlers,
  )
  const ui = userEvent.setup()
  renderWithApollo(<App />, '/vehicles')
  await screen.findByRole('heading', { name: 'Vehicles' })

  await ui.click(screen.getByRole('button', { name: 'Hide menu' })) // before the slow answer ("open") lands
  await new Promise((resolve) => setTimeout(resolve, 450))

  expect(screen.queryByRole('navigation', { name: 'Main navigation' })).not.toBeInTheDocument()
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
  const { settings, ui } = setup(fakeSettingsBackend({ grids: [{ gridId: 'vehicles', order: ['name', 'licensePlate', 'fuelType', 'owner', 'refuelings'], hidden: ['fuelType'], pageSize: 10, sortColumn: 'name', sortDirection: 'ASC' }] }))
  await screen.findByText('Beta')
  expect(headers()).toEqual(['Name', 'License plate', 'Owner', 'Refuelings']) // the server's copy, applied before the first request

  await ui.click(screen.getByRole('combobox', { name: 'Rows per page' }))
  await ui.click(await screen.findByRole('option', { name: '50' }))
  await waitFor(() => expect(settings.state.settings.grids[0].pageSize).toBe(50))

  await ui.click(screen.getByRole('button', { name: 'Columns' }))
  await ui.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Reset to defaults' }))
  await waitFor(() => expect(settings.state.calls.ResetGridSettings).toEqual([{ gridId: 'vehicles' }]))
  await waitFor(() => expect(headers()).toEqual(['Name', 'License plate', 'Fuel', 'Owner', 'Refuelings']))
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
