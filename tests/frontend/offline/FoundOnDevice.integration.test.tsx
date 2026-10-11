import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import { axe } from 'vitest-axe'
import App from '../../../src/frontend/App.tsx'
import { deviceData } from '../../../src/frontend/offline/deviceData.ts'
import { memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import type { FoundRow } from '../../../src/frontend/offline/foundRows.ts'
import { outbox } from '../../../src/frontend/offline/outbox.ts'
import { fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport } from '../support/mocks.tsx'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const row = (over: Partial<FoundRow>): FoundRow => ({
  id: 'r1', entity: 'refuelings', vehicleId: 'v1', generation: 'old', version: 1, values: {}, title: '', setAsideAt: 1, ...over,
})

/** The server, restored, lists Octavia (downloaded on this device) and the rows set aside when its history changed. */
async function setUp(found: FoundRow[]) {
  deviceData.reset(memoryStorage())
  stubViewport('desktop')
  server.use(sessionHandler('NONE', () => null), healthHandler, ...fakeVehicleBackend([fakeVehicle()]).handlers)
  await deviceData.signedIn('anonymous')
  const rows = (await deviceData.writableRows())!
  await rows.putVehicles([{ id: 'v1', name: 'Octavia', logAccess: 'DELETE', version: 1 }])
  await rows.putFound(found)
  renderWithApollo(<App />, '/sync')
  return within(await screen.findByRole('region', { name: 'Found on this device' }))
}

it('lists what the server lacks per vehicle, sends a row as a change with its own id, and forgets one after asking', async () => {
  const section = await setUp([
    row({ id: 'r1', date: '2026-09-01', values: { date: '2026-09-01', volume: 40, totalCost: 60, currency: 'EUR', odometer: 1000, isFullTank: true } }),
    row({ id: 'e1', entity: 'expenses', date: '2026-09-05', title: 'Tyres', values: { date: '2026-09-05', title: 'Tyres', amount: 100, currency: 'EUR' }, serverVersion: 1 }),
    row({ id: 'gone', entity: 'vehicles', vehicleId: 'gone', title: 'Old Golf', values: { name: 'Old Golf', fuelType: 'PETROL' } }),
  ])
  const ui = userEvent.setup()

  const octavia = within(section.getByRole('region', { name: 'Octavia' }))
  expect(octavia.getByText('Refuelling, Sep 1, 2026, €60.00')).toBeInTheDocument()
  expect(octavia.getByText('Expense, Sep 5, 2026, Tyres, €100.00')).toBeInTheDocument()
  expect(octavia.getByText('An older version is on the server')).toBeInTheDocument()
  const golf = within(section.getByRole('region', { name: 'Old Golf' })) // a vehicle the server no longer has names its own group
  expect(golf.getByText('Vehicle, Old Golf')).toBeInTheDocument()
  expect((await axe(document.body, { rules: { 'color-contrast': { enabled: false } } })).violations.map((v) => v.id)).toEqual([])

  await ui.click(octavia.getByRole('button', { name: 'Send to the server: Refuelling, Sep 1, 2026, €60.00' }))

  await waitFor(() => expect(octavia.queryByText(/Refuelling, 1 Sept/)).not.toBeInTheDocument())
  expect(screen.getByText('1 entry is waiting to be sent.')).toBeInTheDocument()
  expect(outbox.changes.map((c) => [c.entity, c.action, c.id, c.targetId, c.expectedVersion])).toEqual([['refuelings', 'add', 'r1', 'r1', undefined]])
  expect(outbox.changes[0].input).toEqual({ date: '2026-09-01', volume: 40, totalCost: 60, currency: 'EUR', odometer: 1000, isFullTank: true })

  await ui.click(octavia.getByRole('button', { name: 'Send to the server: Expense, Sep 5, 2026, Tyres, €100.00' }))
  await waitFor(() => expect(outbox.changes).toHaveLength(2))
  expect(outbox.changes[1]).toMatchObject({ entity: 'expenses', action: 'update', targetId: 'e1', expectedVersion: 1 }) // the server holds an older version: an edit of it

  await ui.click(golf.getByRole('button', { name: 'Forget all entries of Old Golf' }))
  const confirm = await screen.findByRole('alertdialog', { name: 'Forget what was found on this device?' })
  expect(within(confirm).getByText('1 entry of Old Golf is removed from this device; it is not on the server, so it is gone.')).toBeInTheDocument()
  await ui.click(within(confirm).getByRole('button', { name: 'Forget' }))

  await waitFor(() => expect(screen.queryByRole('region', { name: 'Found on this device' })).not.toBeInTheDocument()) // nothing left
  expect(screen.getByText('1 entry forgotten.')).toBeInTheDocument()
  expect(outbox.changes).toHaveLength(2)
  expect(await (await deviceData.rows())!.found()).toEqual([])
})

it('sends a whole vehicle at once, the vehicle before its entries', async () => {
  const section = await setUp([
    row({ id: 'r2', entity: 'refuelings', vehicleId: 'gone', date: '2026-09-02', values: { date: '2026-09-02' } }),
    row({ id: 'gone', entity: 'vehicles', vehicleId: 'gone', title: 'Old Golf', values: { name: 'Old Golf', fuelType: 'PETROL' } }),
    row({ id: 'r1', entity: 'refuelings', vehicleId: 'gone', date: '2026-09-01', values: { date: '2026-09-01' } }),
  ])
  const ui = userEvent.setup()

  await ui.click(section.getByRole('button', { name: 'Send all entries of Old Golf to the server' }))

  await waitFor(() => expect(outbox.changes).toHaveLength(3))
  expect(outbox.changes.map((c) => c.id)).toEqual(['gone', 'r1', 'r2'])
  expect(screen.getByText('3 entries are waiting to be sent.')).toBeInTheDocument()
})
