import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { server } from '../support/server.ts'
import { fakeLogBackend, fakeRefueling, fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport } from '../support/mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

function setup(route = '/trash?tab=refuelings', trashedLogs = [fakeRefueling({ id: 'r1', date: '2026-08-01' }), fakeRefueling({ id: 'r2', date: '2026-09-01' })]) {
  stubViewport('desktop')
  const logs = fakeLogBackend(fakeVehicle(), [])
  logs.state.trash = trashedLogs.map((l) => ({ ...l, deletedAt: '2026-10-01T08:00:00Z' }))
  const vehicles = fakeVehicleBackend([fakeVehicle()], [fakeVehicle({ id: 't1', name: 'Old Fiat' })])
  server.use(sessionHandler('NONE', () => null), healthHandler, ...logs.handlers, ...vehicles.handlers)
  renderWithApollo(<App />, route)
  return { logs, vehicles, ui: userEvent.setup() }
}

it('has a tab for vehicles and one for refuelings, and the choice is in the address', async () => {
  const { ui } = setup('/trash')
  await screen.findByText('Old Fiat')

  await ui.click(screen.getByRole('tab', { name: /Refuelings/ }))

  await screen.findByText(/Aug 1, 2026/)
  expect(screen.getByRole('tab', { name: /Refuelings/, selected: true })).toBeInTheDocument()
})

it('lists trashed refuelings with their vehicle and amounts in the vehicle units', async () => {
  setup()

  const row = (await screen.findByText(/Sep 1, 2026/)).closest<HTMLElement>('[role="row"]')!
  expect(within(row).getByText('Octavia')).toBeInTheDocument()
  expect(within(row).getByText('40.5 L')).toBeInTheDocument()
})

it('restores a refuelling', async () => {
  const { ui, logs } = setup()
  await screen.findByText(/Aug 1, 2026/)

  await ui.click(screen.getByRole('button', { name: 'Restore Aug 1, 2026' }))

  await waitFor(() => expect(logs.state.calls.RestoreRefueling).toEqual([{ id: 'r1' }]))
  await waitFor(() => expect(screen.queryByText(/Aug 1, 2026/)).not.toBeInTheDocument())
})

it('empties the refuelling trash only after a warning, and reports the count', async () => {
  const { ui, logs } = setup()
  await screen.findByText(/Aug 1, 2026/)

  await ui.click(screen.getByRole('button', { name: 'Empty trash' }))
  const dialog = await screen.findByRole('alertdialog')
  expect(dialog).toHaveTextContent('permanently deletes 2 refuelings')
  expect(logs.state.calls.EmptyRefuelingTrash).toBeUndefined()
  await ui.click(within(dialog).getByRole('button', { name: 'Empty trash' }))

  await screen.findByText('Permanently deleted 2 refuelings.')
  expect(await screen.findByText('No refuelings in the trash.')).toBeInTheDocument()
})

it('only offers to delete what the person may delete for good, and says what stays', async () => {
  const { ui, vehicles } = setup('/trash')
  vehicles.state.trashDeletable = 0
  await screen.findByText('Old Fiat')
  await ui.click(screen.getByRole('button', { name: 'Refresh' }))
  await waitFor(() => expect(screen.getByRole('button', { name: 'Empty trash' })).toBeDisabled())

  vehicles.state.trash.push({ ...fakeVehicle({ id: 't2', name: 'Old Opel' }), deletedAt: '2026-10-01T08:00:00Z' })
  vehicles.state.trashDeletable = 1
  await ui.click(screen.getByRole('button', { name: 'Refresh' }))
  await screen.findByText('Old Opel')
  await ui.click(screen.getByRole('button', { name: 'Empty trash' }))

  expect(await screen.findByRole('alertdialog')).toHaveTextContent('permanently deletes 1 item you are allowed to delete; items you may only restore stay in the trash')
})
