import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import App from '../../src/frontend/App.tsx'
import { server } from './server.ts'
import { fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler } from './mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function setup(trashed = [fakeVehicle({ id: 't1', name: 'Old Fiat' }), fakeVehicle({ id: 't2', name: 'Old Opel', licensePlate: null })]) {
  const backend = fakeVehicleBackend([fakeVehicle()], trashed)
  server.use(sessionHandler('NONE', () => null), healthHandler, ...backend.handlers)
  renderWithApollo(<App />, '/trash')
  return { ...backend, ui: userEvent.setup() }
}

it('lists trashed vehicles with their deletion time', async () => {
  setup()

  const row = (await screen.findByText('Old Fiat')).closest('tr')!
  expect(within(row).getByText('ABC-123')).toBeInTheDocument()
  expect(within(row).getByText(new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date('2026-10-01T08:00:00Z')))).toBeInTheDocument()
  expect(screen.getByText('Old Opel')).toBeInTheDocument()
})

it('shows an empty trash, and disables emptying it', async () => {
  setup([])

  await screen.findByText('The trash is empty.')
  expect(screen.getByRole('button', { name: 'Empty trash' })).toBeDisabled()
})

it('restores a vehicle, which then reappears in the vehicle list', async () => {
  const { ui, state } = setup()
  await screen.findByText('Old Fiat')

  await ui.click(screen.getByRole('button', { name: 'Restore Old Fiat' }))

  await waitFor(() => expect(screen.queryByText('Old Fiat')).not.toBeInTheDocument())
  expect(screen.getByText('Old Opel')).toBeInTheDocument()
  expect(state.calls.RestoreVehicle).toEqual([{ id: 't1' }])
  expect(state.vehicles.map((v) => v.name)).toContain('Old Fiat')
})

it('warns before permanently emptying the trash, then reports what was removed', async () => {
  const { ui, state } = setup()
  await screen.findByText('Old Fiat')

  await ui.click(screen.getByRole('button', { name: 'Empty trash' }))
  const confirm = await screen.findByRole('alertdialog')
  expect(confirm).toHaveTextContent('permanently deletes 2 vehicles')
  expect(confirm).toHaveTextContent('cannot be undone')
  await ui.click(within(confirm).getByRole('button', { name: 'Empty trash' }))

  await screen.findByText('The trash is empty.')
  expect(screen.getByText('Permanently deleted 2 vehicles.')).toBeInTheDocument()
  expect(state.calls.EmptyTrash).toHaveLength(1)
})

it('does nothing when emptying the trash is cancelled', async () => {
  const { ui, state } = setup()
  await screen.findByText('Old Fiat')

  await ui.click(screen.getByRole('button', { name: 'Empty trash' }))
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Cancel' }))

  expect(screen.getByText('Old Fiat')).toBeInTheDocument()
  expect(state.calls.EmptyTrash).toBeUndefined()
})
