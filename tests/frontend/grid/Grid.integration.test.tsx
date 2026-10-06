import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { server } from '../support/server.ts'
import { fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, adminSession } from '../support/mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

const cars = [
  fakeVehicle({ id: 'a', name: 'Beta', licensePlate: 'BBB-2', fuelType: 'PETROL', ownerName: 'Zoe', refuelingCount: 5 }),
  fakeVehicle({ id: 'b', name: 'alpha', licensePlate: 'AAA-1', fuelType: 'LPG', ownerName: 'Bob', refuelingCount: 9 }),
  fakeVehicle({ id: 'c', name: 'Gamma', licensePlate: null, fuelType: 'DIESEL', ownerName: 'Amy', refuelingCount: 1 }),
]

const many = (n: number) => Array.from({ length: n }, (_, i) => fakeVehicle({ id: `m${i}`, name: `Car ${String(i + 1).padStart(2, '0')}`, refuelingCount: i }))

function setup(vehicles = cars, route = '/vehicles') {
  const backend = fakeVehicleBackend(vehicles)
  server.use(adminSession(), healthHandler, ...backend.handlers)
  const view = renderWithApollo(<App />, route)
  return { ...backend, view, ui: userEvent.setup() }
}

// Also while the Columns popover is open (it is modal: the page behind it is hidden from assistive technology meanwhile).
const headers = () => screen.getAllByRole('columnheader', { hidden: true }).map((h) => h.textContent).filter((text) => text && text !== 'Actions')
const names = () => screen.getAllByRole('rowheader').map((c) => c.textContent)
const lastRequest = (reqs: Record<string, unknown>[]) => reqs[reqs.length - 1]

const openColumns = async (ui: ReturnType<typeof userEvent.setup>) => {
  await ui.click(screen.getByRole('button', { name: 'Columns' }))
  return within(await screen.findByRole('dialog'))
}

it('asks the server for the default order, the first page and every column', async () => {
  const { state } = setup()
  await screen.findByText('Beta')

  expect(lastRequest(state.requests.Vehicles)).toEqual({
    orderBy: 'NAME',
    direction: 'ASC',
    skip: 0,
    take: 25,
    withLicensePlate: true,
    withFuelType: true,
    withOwner: true,
    withRefuelings: true,
  })
  expect(names()).toEqual(['alpha', 'Beta', 'Gamma'])
  expect(headers()).toEqual(['Name', 'License plate', 'Fuel', 'Owner', 'Refuelings'])
})

it('sorts on the server when a header is clicked, toggling the direction', async () => {
  const { ui, state } = setup()
  await screen.findByText('Beta')

  await ui.click(screen.getByRole('columnheader', { name: /^Name/ }))
  await waitFor(() => expect(names()).toEqual(['Gamma', 'Beta', 'alpha']))
  expect(lastRequest(state.requests.Vehicles)).toMatchObject({ orderBy: 'NAME', direction: 'DESC', skip: 0 })
  expect(screen.getByRole('columnheader', { name: /Name/ })).toHaveAttribute('aria-sort', 'descending')

  await ui.click(screen.getByRole('columnheader', { name: /^Refuelings/ }))
  await waitFor(() => expect(names()).toEqual(['Gamma', 'Beta', 'alpha']))
  expect(lastRequest(state.requests.Vehicles)).toMatchObject({ orderBy: 'REFUELING_COUNT', direction: 'ASC' })
  expect(screen.getByRole('columnheader', { name: /Refuelings/ })).toHaveAttribute('aria-sort', 'ascending')
})

it('sorts by owner, as the server defines it', async () => {
  const { ui, state } = setup()
  await screen.findByText('Beta')

  await ui.click(screen.getByRole('columnheader', { name: /^Owner/ }))

  await waitFor(() => expect(names()).toEqual(['Gamma', 'alpha', 'Beta']))
  expect(lastRequest(state.requests.Vehicles)).toMatchObject({ orderBy: 'OWNER', direction: 'ASC' })
})

it('hides a column by not asking the server for its fields, and remembers the choice', async () => {
  const { ui, state, view } = setup()
  await screen.findByText('Beta')

  const columns = await openColumns(ui)
  await ui.click(columns.getByRole('checkbox', { name: 'Show License plate' }))

  await waitFor(() => expect(headers()).toEqual(['Name', 'Fuel', 'Owner', 'Refuelings']))
  expect(lastRequest(state.requests.Vehicles)).toMatchObject({ withLicensePlate: false, withFuelType: true })
  expect(JSON.parse(window.localStorage.getItem('tankstat.grid.vehicles')!).hidden).toEqual(['licensePlate'])

  // A new visit (fresh app) starts from the saved choice, already on the first request.
  view.unmount()
  const again = fakeVehicleBackend(cars)
  server.use(...again.handlers)
  renderWithApollo(<App />, '/vehicles')
  await screen.findByText('Beta')
  expect(again.state.requests.Vehicles[0]).toMatchObject({ withLicensePlate: false })
  expect(headers()).not.toContain('License plate')
})

it('never hides the name column', async () => {
  const { ui } = setup()
  await screen.findByText('Beta')

  const columns = await openColumns(ui)

  expect(columns.getByRole('checkbox', { name: 'Show Name' })).toBeDisabled()
})

it('reorders columns and remembers the order', async () => {
  const { ui } = setup()
  await screen.findByText('Beta')

  const columns = await openColumns(ui)
  await ui.click(columns.getByRole('button', { name: 'Move Owner up' }))
  await ui.click(columns.getByRole('button', { name: 'Move Owner up' }))

  await waitFor(() => expect(headers()).toEqual(['Name', 'Owner', 'License plate', 'Fuel', 'Refuelings']))
  expect(JSON.parse(window.localStorage.getItem('tankstat.grid.vehicles')!).order).toEqual(['name', 'owner', 'licensePlate', 'fuelType', 'refuelings'])
  expect(columns.getByRole('button', { name: 'Move Name up' })).toBeDisabled()
  expect(columns.getByRole('button', { name: 'Move Refuelings down' })).toBeDisabled()
})

it('resets columns, order and paging to the defaults', async () => {
  const { ui } = setup()
  await screen.findByText('Beta')
  const columns = await openColumns(ui)
  await ui.click(columns.getByRole('checkbox', { name: 'Show Fuel' }))
  await ui.click(columns.getByRole('button', { name: 'Move Owner up' }))

  await ui.click(columns.getByRole('button', { name: 'Reset to defaults' }))

  await waitFor(() => expect(headers()).toEqual(['Name', 'License plate', 'Fuel', 'Owner', 'Refuelings']))
  expect(window.localStorage.getItem('tankstat.grid.vehicles')).toBeNull()
})

it('pages on the server and shows the range', async () => {
  const { ui, state } = setup(many(30))
  await screen.findByText('Car 01')

  expect(screen.getByText('1–25 of 30')).toBeInTheDocument()
  expect(names()).toHaveLength(25)
  expect(screen.getByRole('button', { name: 'Previous page' })).toBeDisabled()

  await ui.click(screen.getByRole('button', { name: 'Next page' }))
  await screen.findByText('26–30 of 30')
  expect(lastRequest(state.requests.Vehicles)).toMatchObject({ skip: 25, take: 25 })
  expect(names()).toEqual(['Car 26', 'Car 27', 'Car 28', 'Car 29', 'Car 30'])
  expect(screen.getByRole('button', { name: 'Next page' })).toBeDisabled()

  await ui.click(screen.getByRole('button', { name: 'Previous page' }))
  await screen.findByText('1–25 of 30')
})

it('changes the page size, starting again from the first page, and remembers it', async () => {
  const { ui, state } = setup(many(30))
  await screen.findByText('Car 01')
  await ui.click(screen.getByRole('button', { name: 'Next page' }))
  await screen.findByText('26–30 of 30')

  await ui.click(screen.getByRole('combobox', { name: 'Rows per page' }))
  await ui.click(await screen.findByRole('option', { name: '10' }))

  await screen.findByText('1–10 of 30')
  expect(lastRequest(state.requests.Vehicles)).toMatchObject({ skip: 0, take: 10 })
  expect(JSON.parse(window.localStorage.getItem('tankstat.grid.vehicles')!).pageSize).toBe(10)
})

it('steps back a page when the last row of the last page is deleted', async () => {
  const { ui } = setup(many(26))
  await screen.findByText('Car 01')
  await ui.click(screen.getByRole('button', { name: 'Next page' }))
  await screen.findByText('26–26 of 26')

  await ui.click(screen.getByRole('button', { name: 'Delete Car 26' }))
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Move to trash' }))

  await screen.findByText('1–25 of 25')
  expect(screen.getByText('Car 25')).toBeInTheDocument()
})

it('a row being trashed fades out while the server removes it', async () => {
  const { ui } = setup()
  await screen.findByText('Beta')
  let answer!: () => void
  const held = new Promise<void>((resolve) => (answer = resolve))
  server.use(graphql.mutation('DeleteVehicle', async () => void (await held))) // then on to the vehicle backend

  await ui.click(screen.getByRole('button', { name: 'Delete Beta' }))
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Move to trash' }))

  const row = () => screen.queryByRole('rowheader', { name: /Beta/ })?.closest<HTMLElement>('[role="row"]')
  await waitFor(() => expect(row()).toHaveClass('tk-leaving'))
  answer()
  await waitFor(() => expect(row()).toBeFalsy())
  expect(document.querySelector('.tk-leaving')).toBeNull()
})

it('refreshes on demand', async () => {
  const { ui, state } = setup()
  await screen.findByText('Beta')
  const before = state.requests.Vehicles.length
  state.vehicles.push({ ...fakeVehicle({ id: 'z', name: 'Zeta' }), deletedAt: '' })

  await ui.click(screen.getByRole('button', { name: 'Refresh' }))

  await screen.findByText('Zeta')
  expect(state.requests.Vehicles.length).toBeGreaterThan(before)
})

it('loads fresh data every time the page is opened again from the menu', async () => {
  const { ui, state } = setup()
  await screen.findByText('Beta')

  await ui.click(screen.getByRole('link', { name: 'Trash' }))
  await screen.findByRole('heading', { name: 'Trash' })
  state.vehicles.push({ ...fakeVehicle({ id: 'z', name: 'Added elsewhere' }), deletedAt: '' }) // e.g. by another user
  await ui.click(screen.getByRole('link', { name: 'Vehicles' }))

  await screen.findByText('Added elsewhere')
})

it('starts with the essential columns on a phone, and the user can still add the rest', async () => {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query.includes('max-width: 767px'),
    media: query,
    onchange: null,
    addEventListener() {},
    removeEventListener() {},
    addListener() {},
    removeListener() {},
    dispatchEvent: () => false,
  }))
  const { ui, state } = setup()
  await screen.findByText('Beta')

  expect(headers()).toEqual(['Name', 'Fuel'])
  expect(state.requests.Vehicles[0]).toMatchObject({ withLicensePlate: false, withOwner: false, withRefuelings: false, withFuelType: true })

  const columns = await openColumns(ui)
  await ui.click(columns.getByRole('checkbox', { name: 'Show Owner' }))
  await waitFor(() => expect(headers()).toEqual(['Name', 'Fuel', 'Owner']))
})

it('ignores damaged or outdated saved settings', async () => {
  window.localStorage.setItem('tankstat.grid.vehicles', '{not json')
  const { view } = setup()
  await screen.findByText('Beta')
  expect(headers()).toEqual(['Name', 'License plate', 'Fuel', 'Owner', 'Refuelings'])
  view.unmount()

  window.localStorage.setItem(
    'tankstat.grid.vehicles',
    JSON.stringify({ order: ['gone', 'owner'], hidden: ['name', 'gone'], pageSize: 7, sortField: 'NOPE', sortDirection: 'SIDEWAYS' }),
  )
  const again = fakeVehicleBackend(cars)
  server.use(...again.handlers)
  renderWithApollo(<App />, '/vehicles')
  await screen.findByText('Beta')

  expect(headers()).toEqual(['Owner', 'Name', 'License plate', 'Fuel', 'Refuelings']) // unknown ids dropped, missing ones appended
  expect(again.state.requests.Vehicles[0]).toMatchObject({ orderBy: 'NAME', direction: 'ASC', take: 25 })
})

it('edits with the real values even when columns are hidden (the grid did not load them)', async () => {
  const { ui, state } = setup()
  await screen.findByText('Beta')
  const columns = await openColumns(ui)
  await ui.click(columns.getByRole('checkbox', { name: 'Show License plate' }))
  await ui.click(columns.getByRole('checkbox', { name: 'Show Fuel' }))
  await ui.keyboard('{Escape}')
  await waitFor(() => expect(headers()).toEqual(['Name', 'Owner', 'Refuelings']))

  await ui.click(screen.getByRole('button', { name: 'Edit Beta' }))
  const dialog = await screen.findByRole('dialog', { name: 'Edit vehicle' })
  await waitFor(() => expect(within(dialog).getByLabelText('Name')).toHaveValue('Beta'))
  expect(within(dialog).getByLabelText(/License plate/)).toHaveValue('BBB-2')
  expect(within(dialog).getByRole('combobox', { name: 'Fuel' })).toHaveTextContent('Petrol')
  await ui.click(within(dialog).getByRole('button', { name: 'Save changes' }))

  await waitFor(() => expect(state.calls.UpdateVehicle).toEqual([{ input: { id: 'a', name: 'Beta', licensePlate: 'BBB-2', fuelType: 'PETROL', units: { distance: 'KILOMETERS', volume: 'LITERS' } } }]))
})

it('the trash grid starts newest-deleted first and keeps its own settings', async () => {
  const backend = fakeVehicleBackend(cars, [fakeVehicle({ id: 't1', name: 'Old Fiat' })])
  server.use(adminSession(), healthHandler, ...backend.handlers)
  renderWithApollo(<App />, '/trash')
  await screen.findByText('Old Fiat')

  expect(backend.state.requests.Trash[0]).toMatchObject({ orderBy: 'DELETED_AT', direction: 'DESC', withDeletedAt: true })
  expect(headers()).toEqual(['Name', 'License plate', 'Fuel', 'Owner', 'Deleted'])
  expect(window.localStorage.getItem('tankstat.grid.vehicles')).toBeNull()
})
