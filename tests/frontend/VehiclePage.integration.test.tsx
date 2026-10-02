import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../src/frontend/App.tsx'
import { server } from './server.ts'
import { fakeLogBackend, fakeRefueling, fakeVehicle, fakeVehicleBackend, adminSession, healthHandler, person, renderWithApollo, sessionHandler, stubViewport } from './mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

const logs = [
  fakeRefueling({ id: 'r1', date: '2026-08-01', volume: 40, totalCost: 20000, odometer: 11000 }),
  fakeRefueling({ id: 'r2', date: '2026-09-01', volume: 41.5, totalCost: 22000, odometer: 12000, isFullTank: false, note: 'Holiday' }),
]

function setup(vehicle = fakeVehicle(), initial = logs, route = '/vehicles/v1?tab=refuelings', admin = false) {
  stubViewport('desktop')
  const backend = fakeLogBackend(vehicle, initial)
  server.use(admin ? adminSession() : sessionHandler('NONE', () => null), healthHandler, ...backend.handlers)
  renderWithApollo(<App />, route)
  return { ...backend, ui: userEvent.setup() }
}

const choose = async (ui: ReturnType<typeof userEvent.setup>, control: string, option: string) => {
  await ui.click(screen.getByRole('combobox', { name: control }))
  await ui.click(await screen.findByRole('option', { name: option }))
}

it('shows the vehicle with its logs formatted in its own units', async () => {
  setup(fakeVehicle({ units: { distance: 'MILES', volume: 'US_GALLONS' } }))

  const row = (await screen.findByText(/Sep 1, 2026/)).closest('tr')!
  expect(within(row).getByText('41.5 gal')).toBeInTheDocument()
  expect(within(row).getByText(/12,000 mi/)).toBeInTheDocument()
  expect(within(row).getByText('Partial')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'Octavia' })).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Back to home' })).toHaveAttribute('href', '/') // the vehicle list is for administrators
})

it('administrators go back to the full vehicle list', async () => {
  setup(fakeVehicle(), logs, '/vehicles/v1?tab=refuelings', true)

  expect(await screen.findByRole('link', { name: 'Back to vehicles' })).toHaveAttribute('href', '/vehicles')
})

it('shows the price per unit and the cost in the currency it was paid in', async () => {
  setup()

  const row = (await screen.findByText(/Sep 1, 2026/)).closest('tr')!
  expect(within(row).getByText(/22,000/)).toBeInTheDocument()
  expect(within(row).getByText(/530/)).toBeInTheDocument() // 22000 / 41.5 per liter
})

it('shows the stored consumption per 100 units, and a dash where there is none', async () => {
  setup(fakeVehicle(), [
    fakeRefueling({ id: 'r1', date: '2026-08-01', odometer: 11000, consumption: null }),
    fakeRefueling({ id: 'r2', date: '2026-09-01', odometer: 12000, consumption: 6.667 }),
  ])

  const row = (await screen.findByText(/Sep 1, 2026/)).closest('tr')!
  expect(within(row).getByText('6.67 L/100 km')).toBeInTheDocument()
  expect(within((await screen.findByText(/Aug 1, 2026/)).closest('tr')!).getAllByText('–').length).toBeGreaterThan(0)
})

it('shows miles per gallon where the vehicle uses miles and gallons', async () => {
  setup(fakeVehicle({ units: { distance: 'MILES', volume: 'US_GALLONS' } }), [fakeRefueling({ id: 'r2', date: '2026-09-01', consumption: 8 })])

  expect(await screen.findByText('12.5 mpg (US)')).toBeInTheDocument()
})

it('asks the server for the consumption and sorts by it on the server', async () => {
  const { ui, state } = setup()
  await screen.findByText(/Sep 1, 2026/)
  expect(state.requests.at(-1)).toMatchObject({ withConsumption: true })

  await ui.click(screen.getByRole('button', { name: /^Sort by Consumption/ }))

  await waitFor(() => expect(state.requests.at(-1)).toMatchObject({ orderBy: 'CONSUMPTION', direction: 'ASC' }))
})

it('sorts the logs on the server, newest first by default', async () => {
  const { ui, state } = setup()
  await screen.findByText(/Sep 1, 2026/)
  expect(screen.getAllByRole('rowheader').map((c) => c.textContent)).toEqual(['Sep 1, 2026', 'Aug 1, 2026'])
  expect(state.requests.at(-1)).toMatchObject({ vehicleId: 'v1', orderBy: 'DATE', direction: 'DESC', skip: 0 })

  await ui.click(screen.getByRole('button', { name: /^Sort by Odometer/ }))

  await waitFor(() => expect(state.requests.at(-1)).toMatchObject({ orderBy: 'ODOMETER', direction: 'ASC' }))
})

it('adds a refuelling: starts from today, the last reading as a hint and the usual currency', async () => {
  const { ui, state } = setup()
  await screen.findByText(/Sep 1, 2026/)

  await ui.click(screen.getByRole('button', { name: 'Add refuelling' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add refuelling' })
  await within(dialog).findByText(/Last reading: 12,000 km on Sep 1, 2026/)
  expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF')
  expect(within(dialog).getByLabelText('Date')).toHaveValue(new Date().toLocaleDateString('sv-SE'))
  await ui.type(within(dialog).getByLabelText(/^Volume/), '38,2') // a comma is fine
  await ui.type(within(dialog).getByLabelText('Total cost'), '19100')
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '12450')
  await ui.click(within(dialog).getByRole('button', { name: 'Add refuelling' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.LogRefueling).toEqual([
    { input: expect.objectContaining({ vehicleId: 'v1', volume: 38.2, totalCost: 19100, currency: 'HUF', odometer: 12450, isFullTank: true, note: null }) },
  ])
  await screen.findByText(/12,450 km/)
})

it('blocks missing and malformed numbers in the form without calling the server', async () => {
  const { ui, state } = setup()
  await screen.findByText(/Sep 1, 2026/)

  await ui.click(screen.getByRole('button', { name: 'Add refuelling' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add refuelling' })
  await ui.type(within(dialog).getByLabelText(/^Volume/), 'lots')
  await ui.click(within(dialog).getByRole('button', { name: 'Add refuelling' }))

  expect(await within(dialog).findByText('Enter a number, for example 12.5.')).toBeInTheDocument()
  expect(within(dialog).getByText('Total cost is required')).toBeInTheDocument()
  expect(state.calls.LogRefueling).toBeUndefined()
})

it('shows the odometer rule from the server in the form and keeps the dialog open', async () => {
  const { ui, state } = setup()
  state.failWith = { message: 'too low', key: 'odometer.belowPrevious', args: { previous: '12,000 km', date: '2026-09-01' } }
  await screen.findByText(/Sep 1, 2026/)

  await ui.click(screen.getByRole('button', { name: 'Add refuelling' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add refuelling' })
  await ui.type(within(dialog).getByLabelText(/^Volume/), '30')
  await ui.type(within(dialog).getByLabelText('Total cost'), '15000')
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '100')
  await ui.click(within(dialog).getByRole('button', { name: 'Add refuelling' }))

  expect(await within(dialog).findByRole('alert')).toHaveTextContent('The odometer cannot be lower than 12,000 km, the reading on 2026-09-01.')
  expect(screen.getByRole('dialog')).toBeInTheDocument()
})

it('edits a refuelling starting from its real values', async () => {
  const { ui, state } = setup()
  await screen.findByText(/Sep 1, 2026/)

  await ui.click(screen.getByRole('button', { name: 'Edit the refuelling of Sep 1, 2026' }))
  const dialog = await screen.findByRole('dialog', { name: 'Edit refuelling' })
  await waitFor(() => expect(within(dialog).getByLabelText('Total cost')).toHaveValue('22000'))
  expect(within(dialog).getByLabelText('Note (optional)')).toHaveValue('Holiday')
  expect(within(dialog).getByRole('switch', { name: /Full tank/ })).not.toBeChecked()
  await ui.clear(within(dialog).getByLabelText('Total cost'))
  await ui.type(within(dialog).getByLabelText('Total cost'), '22500')
  await ui.click(within(dialog).getByRole('button', { name: 'Save changes' }))

  await waitFor(() => expect(state.calls.UpdateRefueling).toEqual([{ input: expect.objectContaining({ id: 'r2', totalCost: 22500, volume: 41.5, odometer: 12000, isFullTank: false, note: 'Holiday' }) }]))
})

it('moves a refuelling to the trash after a confirmation, not before', async () => {
  const { ui, state } = setup()
  await screen.findByText(/Sep 1, 2026/)

  await ui.click(screen.getByRole('button', { name: 'Delete the refuelling of Sep 1, 2026' }))
  expect(state.calls.DeleteRefueling).toBeUndefined()
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Move to trash' }))

  await waitFor(() => expect(screen.queryByText(/Sep 1, 2026/)).not.toBeInTheDocument())
  expect(state.calls.DeleteRefueling).toEqual([{ id: 'r2' }])
})

it('logs only view access: no add button, no edit or delete', async () => {
  setup(fakeVehicle({ canEdit: false, logAccess: 'VIEW' }), logs.map((l) => ({ ...l, canEdit: false })))

  await screen.findByText(/Sep 1, 2026/)

  expect(screen.queryByRole('button', { name: 'Add refuelling' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /^Edit the refuelling/ })).not.toBeInTheDocument()
  expect(screen.getAllByText('view only')).toHaveLength(2)
})

it('a log grantee works with the logs but cannot reach the vehicle settings or sharing', async () => {
  setup(fakeVehicle({ canEdit: false, logAccess: 'EDIT' }))

  await screen.findByText(/Sep 1, 2026/)

  expect(screen.getByRole('button', { name: 'Add refuelling' })).toBeInTheDocument()
  expect(screen.queryByRole('tab', { name: /Sharing/ })).not.toBeInTheDocument()
  expect(screen.getByText(/can work with this vehicle's logs, but not change the vehicle itself/)).toBeInTheDocument()
})

it('explains an unknown or inaccessible vehicle', async () => {
  setup(fakeVehicle(), logs, '/vehicles/nope')

  await screen.findByText(/does not exist, or you do not have access/)
})

it('opens the tab named in the address and keeps the choice in it', async () => {
  const { ui } = setup(fakeVehicle(), logs, '/vehicles/v1?tab=details')

  expect(await screen.findByRole('tab', { name: /Details/, selected: true })).toBeInTheDocument()
  await ui.click(screen.getByRole('tab', { name: /Refuelings/ }))
  expect(await screen.findByRole('button', { name: 'Add refuelling' })).toBeInTheDocument()
})

it('lists, adds, changes and revokes who can work with the logs', async () => {
  const { ui, state } = setup(fakeVehicle(), logs, '/vehicles/v1?tab=sharing')
  await screen.findByText('Nobody has been given access to the logs yet.')

  await choose(ui, 'Person', 'Bob')
  await choose(ui, 'Access', 'Add, change and delete logs permanently')
  await ui.click(screen.getByRole('button', { name: 'Give access' }))

  await screen.findByRole('combobox', { name: 'Access of Bob' })
  expect(state.calls.SetVehicleLogAccess).toEqual([{ input: { vehicleId: 'v1', userId: person('Bob').id, level: 'DELETE' } }])
  expect(screen.getByRole('combobox', { name: 'Access of Bob' })).toHaveTextContent('delete')

  await choose(ui, 'Access of Bob', 'Add and change logs')
  await waitFor(() => expect(state.grants[0]?.level).toBe('EDIT'))

  await ui.click(screen.getByRole('button', { name: 'Remove access of Bob' }))
  await screen.findByText('Nobody has been given access to the logs yet.')
  expect(state.calls.SetVehicleLogAccess?.at(-1)).toEqual({ input: { vehicleId: 'v1', userId: person('Bob').id, level: 'NONE' } })
})

it('the details tab shows the units and, for editors, the picture controls', async () => {
  setup(fakeVehicle({ units: { distance: 'MILES', volume: 'IMPERIAL_GALLONS' } }), logs, '/vehicles/v1?tab=details')

  expect(await screen.findByText('Imperial gallons')).toBeInTheDocument()
  expect(screen.getByText('Miles')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Choose a picture' })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Remove picture' })).not.toBeInTheDocument()
})

function setupDetails(vehicle = fakeVehicle()) {
  stubViewport('desktop')
  const vehicles = fakeVehicleBackend([vehicle])
  server.use(sessionHandler('NONE', () => null), healthHandler, ...vehicles.handlers, ...fakeLogBackend(vehicle, logs).handlers)
  renderWithApollo(<App />, '/vehicles/v1?tab=details')
  return { ...vehicles, ui: userEvent.setup() }
}

it('moves the vehicle to the trash from its details, after a confirmation, and goes back home', async () => {
  const { ui, state } = setupDetails()

  await ui.click(await screen.findByRole('button', { name: 'Move to trash' }))
  const dialog = await screen.findByRole('alertdialog', { name: 'Move Octavia to the trash?' })
  expect(state.calls.DeleteVehicle).toBeUndefined() // nothing happens before the confirmation
  await ui.click(within(dialog).getByRole('button', { name: 'Move to trash' }))

  await screen.findByRole('heading', { name: 'Your vehicles' })
  expect(state.calls.DeleteVehicle).toEqual([{ id: 'v1' }])
  expect(state.trash.map((v) => v.id)).toEqual(['v1'])
})

it('keeps the vehicle when the confirmation is cancelled', async () => {
  const { ui, state } = setupDetails()

  await ui.click(await screen.findByRole('button', { name: 'Move to trash' }))
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Cancel' }))

  expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  expect(state.calls.DeleteVehicle).toBeUndefined()
  expect(screen.getByRole('heading', { name: 'Octavia' })).toBeInTheDocument()
})

it('offers no trash button to someone who may only view the vehicle', async () => {
  setupDetails(fakeVehicle({ canEdit: false, logAccess: 'VIEW' }))

  await screen.findByRole('heading', { name: 'Octavia' })

  expect(screen.queryByRole('button', { name: 'Move to trash' })).not.toBeInTheDocument()
})
