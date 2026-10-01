import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import App from '../../src/frontend/App.tsx'
import { server } from './server.ts'
import { fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler } from './mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function setup(vehicles = [fakeVehicle(), fakeVehicle({ id: 'v2', name: 'Bike', licensePlate: null, fuelType: 'PETROL' })]) {
  const backend = fakeVehicleBackend(vehicles)
  server.use(sessionHandler('NONE', () => null), healthHandler, ...backend.handlers)
  renderWithApollo(<App />, '/vehicles')
  return { ...backend, ui: userEvent.setup() }
}

/** Radix Select renders its options in a portal on the document body. */
const choose = async (ui: ReturnType<typeof userEvent.setup>, control: string, option: string) => {
  await ui.click(screen.getByRole('combobox', { name: control }))
  await ui.click(await screen.findByRole('option', { name: option }))
}

it('lists vehicles with their details', async () => {
  setup()

  const row = (await screen.findByText('Octavia')).closest('tr')!
  expect(within(row).getByText('ABC-123')).toBeInTheDocument()
  expect(within(row).getByText('Diesel')).toBeInTheDocument()
  expect(within(row).getByText('Alice')).toBeInTheDocument()
  expect(within(screen.getByText('Bike').closest('tr')!).getByText('Petrol')).toBeInTheDocument()
})

it('shows a hint when there are no vehicles', async () => {
  setup([])

  await screen.findByText(/No vehicles yet/)
})

it('adds a vehicle through the dialog', async () => {
  const { ui, state } = setup([])
  await screen.findByText(/No vehicles yet/)

  await ui.click(screen.getByRole('button', { name: 'Add vehicle' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add vehicle' })
  await ui.type(within(dialog).getByLabelText('Name'), 'Golf')
  await ui.type(within(dialog).getByLabelText(/License plate/), 'xy-99')
  await choose(ui, 'Fuel', 'LPG')
  await ui.click(within(dialog).getByRole('button', { name: 'Add vehicle' }))

  await screen.findByText('Golf')
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  expect(state.calls.AddVehicle).toEqual([{ input: { name: 'Golf', licensePlate: 'xy-99', fuelType: 'LPG' } }])
})

it('sends an empty plate as null', async () => {
  const { ui, state } = setup([])
  await screen.findByText(/No vehicles yet/)

  await ui.click(screen.getByRole('button', { name: 'Add vehicle' }))
  await ui.type(await screen.findByLabelText('Name'), 'Scooter')
  await ui.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Add vehicle' }))

  await screen.findByText('Scooter')
  expect(state.calls.AddVehicle).toEqual([{ input: { name: 'Scooter', licensePlate: null, fuelType: 'PETROL' } }])
})

it('blocks an empty name in the form, without calling the server', async () => {
  const { ui, state } = setup([])
  await screen.findByText(/No vehicles yet/)

  await ui.click(screen.getByRole('button', { name: 'Add vehicle' }))
  await ui.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Add vehicle' }))

  expect(await screen.findByText('Name is required')).toBeInTheDocument()
  expect(screen.getByRole('dialog')).toBeInTheDocument()
  expect(state.calls.AddVehicle).toBeUndefined()
})

it('keeps the dialog open and shows the server message when adding fails', async () => {
  const { ui, state } = setup([])
  state.failWith = { message: 'A vehicle with this name already exists.' }
  await screen.findByText(/No vehicles yet/)

  await ui.click(screen.getByRole('button', { name: 'Add vehicle' }))
  await ui.type(await screen.findByLabelText('Name'), 'Golf')
  await ui.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Add vehicle' }))

  const dialog = screen.getByRole('dialog')
  expect(await within(dialog).findByRole('alert')).toHaveTextContent('A vehicle with this name already exists.')
})

it('starts from the current values each time the edit dialog is opened', async () => {
  const { ui } = setup()
  await screen.findByText('Octavia')

  await ui.click(screen.getByRole('button', { name: 'Edit Octavia' }))
  await ui.clear(await screen.findByLabelText('Name'))
  await ui.type(screen.getByLabelText('Name'), 'Scrapped draft')
  await ui.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }))
  await ui.click(screen.getByRole('button', { name: 'Edit Octavia' }))

  expect(await screen.findByLabelText('Name')).toHaveValue('Octavia')
})

it('cancelling the add dialog changes nothing', async () => {
  const { ui, state } = setup([])
  await screen.findByText(/No vehicles yet/)

  await ui.click(screen.getByRole('button', { name: 'Add vehicle' }))
  await ui.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }))

  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  expect(state.calls.AddVehicle).toBeUndefined()
})

it('edits a vehicle, starting from its current values', async () => {
  const { ui, state } = setup()
  await screen.findByText('Octavia')

  await ui.click(screen.getByRole('button', { name: 'Edit Octavia' }))
  const dialog = await screen.findByRole('dialog', { name: 'Edit vehicle' })
  const name = within(dialog).getByLabelText('Name')
  expect(name).toHaveValue('Octavia')
  expect(within(dialog).getByLabelText(/License plate/)).toHaveValue('ABC-123')
  expect(within(dialog).getByRole('combobox', { name: 'Fuel' })).toHaveTextContent('Diesel')
  await ui.clear(name)
  await ui.type(name, 'Superb')
  await choose(ui, 'Fuel', 'Petrol')
  await ui.click(within(dialog).getByRole('button', { name: 'Save changes' }))

  await screen.findByText('Superb')
  expect(screen.queryByText('Octavia')).not.toBeInTheDocument()
  expect(state.calls.UpdateVehicle).toEqual([{ input: { id: 'v1', name: 'Superb', licensePlate: 'ABC-123', fuelType: 'PETROL' } }])
})

it('asks for confirmation, then moves the vehicle to the trash', async () => {
  const { ui, state } = setup()
  await screen.findByText('Octavia')

  await ui.click(screen.getByRole('button', { name: 'Delete Octavia' }))
  const confirm = await screen.findByRole('alertdialog')
  expect(confirm).toHaveTextContent('Move Octavia to the trash?')
  await ui.click(within(confirm).getByRole('button', { name: 'Move to trash' }))

  await waitFor(() => expect(screen.queryByText('Octavia')).not.toBeInTheDocument())
  expect(screen.getByText('Bike')).toBeInTheDocument()
  expect(state.calls.DeleteVehicle).toEqual([{ id: 'v1' }])
  expect(state.trash.map((v) => v.name)).toEqual(['Octavia'])
})

it('keeps the vehicle when the deletion is cancelled', async () => {
  const { ui, state } = setup()
  await screen.findByText('Octavia')

  await ui.click(screen.getByRole('button', { name: 'Delete Octavia' }))
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Cancel' }))

  expect(screen.getByText('Octavia')).toBeInTheDocument()
  expect(state.calls.DeleteVehicle).toBeUndefined()
})

it('offers no changes for vehicles the user may only view', async () => {
  setup([fakeVehicle({ canEdit: false, ownerName: 'Bob' }), fakeVehicle({ id: 'v2', name: 'Mine' })])

  await screen.findByText('Mine')
  expect(screen.getByText('view only')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Edit Octavia' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Delete Octavia' })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Edit Mine' })).toBeInTheDocument()
})
