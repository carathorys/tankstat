import { describe, expect, it } from 'vitest'
import { memoryStorage, type LogRow, type VehicleRow } from '../../../src/frontend/offline/deviceStorage.ts'
import { answerLocally, sortLogs } from '../../../src/frontend/offline/localResolvers.ts'
import fixture from '../../backend/contracts/log-order/log-order.json'

const vehicles = new Map<string, VehicleRow>([
  ['v1', { id: 'v1', name: 'Octavia', logAccess: 'DELETE' }],
  ['v2', { id: 'v2', name: 'astra', logAccess: 'EDIT' }],
])

const refueling = (id: string, fields: Partial<LogRow> = {}): LogRow => ({
  __typename: 'Refueling',
  id,
  vehicleId: 'v1',
  date: '2026-09-01',
  volume: 40,
  totalCost: 20000,
  odometer: 1000,
  consumption: null,
  deletedAt: null,
  createdBy: { __typename: 'UserRef', id: 'u1', displayName: 'Alice', avatarUrl: null },
  pulledAt: 1,
  ...fields,
})

const ids = (rows: readonly LogRow[]) => rows.map((r) => r.id)

describe('sortLogs, like the repositories', () => {
  it('orders by the field, then the odometer the same way, then the id', () => {
    const rows = [
      refueling('b', { date: '2026-09-01', odometer: 1200 }),
      refueling('a', { date: '2026-09-01', odometer: 1200 }),
      refueling('c', { date: '2026-09-01', odometer: 1100 }),
      refueling('d', { date: '2026-09-05', odometer: 1300 }),
    ]

    expect(ids(sortLogs('refuelings', rows, 'DATE', 'DESC', vehicles))).toEqual(['d', 'a', 'b', 'c']) // the id breaks the tie upwards
    expect(ids(sortLogs('refuelings', rows, 'DATE', 'ASC', vehicles))).toEqual(['c', 'a', 'b', 'd'])
  })

  it('puts missing values first going up and last going down, but counts a missing cost, odometer or price as 0', () => {
    const rows = [refueling('a', { consumption: 6.5 }), refueling('b', { consumption: null }), refueling('c', { consumption: 5 })]
    expect(ids(sortLogs('refuelings', rows, 'CONSUMPTION', 'ASC', vehicles))).toEqual(['b', 'c', 'a'])
    expect(ids(sortLogs('refuelings', rows, 'CONSUMPTION', 'DESC', vehicles))).toEqual(['a', 'c', 'b'])

    const costs = [refueling('a', { totalCost: 10, volume: 2, odometer: 1 }), refueling('b', { totalCost: null, volume: null, odometer: 2 }), refueling('c', { totalCost: 30, volume: 2, odometer: 3 })]
    expect(ids(sortLogs('refuelings', costs, 'TOTAL_COST', 'ASC', vehicles))).toEqual(['b', 'a', 'c'])
    expect(ids(sortLogs('refuelings', costs, 'PRICE_PER_UNIT', 'DESC', vehicles))).toEqual(['c', 'a', 'b'])
  })

  it('orders names without regard to case, and an expense without a category as an empty one', () => {
    const rows = [refueling('a', { vehicleId: 'v1' }), refueling('b', { vehicleId: 'v2' })]
    expect(ids(sortLogs('refuelings', rows, 'VEHICLE', 'ASC', vehicles))).toEqual(['b', 'a']) // "astra" before "Octavia"

    const expenses = [refueling('a', { category: 'Tyres' }), refueling('b', { category: null }), refueling('c', { category: 'insurance' })]
    expect(ids(sortLogs('expenses', expenses, 'CATEGORY', 'ASC', vehicles))).toEqual(['b', 'c', 'a'])
  })
})

describe('answerLocally', () => {
  async function store(rows: LogRow[], complete = true) {
    const device = await (memoryStorage()).open('u1')
    await device.rows.putVehicles([...vehicles.values()])
    await device.rows.putLogs('refuelings', rows)
    for (const id of ['v1', 'v2']) await device.rows.putCursor({ vehicleId: id, from: null, watermark: 'w', next: null, complete, fullStartedAt: null })
    return device.rows
  }

  it('answers any page of a downloaded vehicle, with who may edit following the vehicle today', async () => {
    const rows = await store([refueling('a', { date: '2026-09-03' }), refueling('b', { date: '2026-09-02' }), refueling('c', { date: '2026-09-01' }), refueling('t', { deletedAt: '2026-09-10T00:00:00Z' })])

    const answer = await answerLocally(rows, 'Refuelings', { vehicleId: 'v1', orderBy: 'DATE', direction: 'DESC', skip: 1, take: 1 })

    expect(answer?.refuelingCount).toBe(3) // the trash is not counted
    const [row] = answer!.refuelings as Record<string, unknown>[]
    expect(row).toMatchObject({ id: 'b', canEdit: true, canDelete: true, __typename: 'Refueling' })
    expect(row).not.toHaveProperty('pulledAt')
  })

  it('answers the trash across the vehicles whose logs may be edited, and counts what may be deleted', async () => {
    const rows = await store([refueling('t1', { deletedAt: '2026-09-10T00:00:00Z' }), refueling('t2', { vehicleId: 'v2', deletedAt: '2026-09-11T00:00:00Z' })])

    const answer = await answerLocally(rows, 'RefuelingTrash', { orderBy: 'DELETED_AT', direction: 'DESC', skip: 0, take: 10 })

    expect(ids(answer?.refuelingTrash as LogRow[])).toEqual(['t2', 't1'])
    expect(answer?.refuelingTrashCount).toBe(2)
    expect(answer?.refuelingTrashDeletableCount).toBe(1) // v2's logs may only be edited
    expect((answer!.refuelingTrash as { vehicle: { name: string } }[])[0].vehicle.name).toBe('astra')
  })

  it('leaves to the answers seen what it cannot answer: a vehicle not downloaded yet, a log it does not have, another query', async () => {
    const rows = await store([refueling('a')], false)

    expect(await answerLocally(rows, 'Refuelings', { vehicleId: 'v1', orderBy: 'DATE', direction: 'DESC', skip: 0, take: 10 })).toBeUndefined()
    expect(await answerLocally(rows, 'RefuelingDetails', { id: 'nope' })).toBeUndefined()
    expect(await answerLocally(rows, 'Welcome', {})).toBeUndefined()
    expect(await answerLocally(null, 'Refuelings', { vehicleId: 'v1' })).toBeUndefined()
    expect((await answerLocally(rows, 'RefuelingDetails', { id: 'a' }))?.refueling).toMatchObject({ id: 'a' }) // a log it has, any vehicle

    await rows.putCursor({ vehicleId: 'v1', from: false, watermark: null, next: null, complete: true, fullStartedAt: null }) // window "none"
    expect(await answerLocally(rows, 'Refuelings', { vehicleId: 'v1', orderBy: 'DATE', direction: 'DESC', skip: 0, take: 10 })).toBeUndefined()
  })
})

describe('the same order as the server', () => {
  // The fixture the repositories are tested against too (Infrastructure SortParityTests): any change on either side fails one of them.
  const toRows = (list: Record<string, unknown>[]) => list.map((r) => ({ ...r, vehicleId: 'v1', pulledAt: 1 }) as LogRow)

  it.each(Object.entries(fixture.refuelingOrders))('refuelings, %s', (order, expected) => {
    const [field, direction] = order.split(' ')
    expect(ids(sortLogs('refuelings', toRows(fixture.refuelings), field, direction as 'ASC' | 'DESC', vehicles))).toEqual(expected)
  })

  it.each(Object.entries(fixture.expenseOrders))('expenses, %s', (order, expected) => {
    const [field, direction] = order.split(' ')
    expect(ids(sortLogs('expenses', toRows(fixture.expenses), field, direction as 'ASC' | 'DESC', vehicles))).toEqual(expected)
  })
})

describe('the changes waiting on this device', () => {
  it('lay over the rows: an add is a row, an update its values, a restore takes it out of the trash, a trash leaves it', async () => {
    const { outbox } = await import('../../../src/frontend/offline/outbox.ts')
    const { deviceData } = await import('../../../src/frontend/offline/deviceData.ts')
    const { withChanges } = await import('../../../src/frontend/offline/localResolvers.ts')
    deviceData.reset(memoryStorage())
    await deviceData.signedIn('u1')
    await outbox.reload()
    await outbox.enqueue({ id: 'n', entity: 'refuelings', action: 'add', vehicleId: 'v1', targetId: 'n', input: { id: 'n', date: '2026-10-02', volume: 20, totalCost: 50, currency: 'EUR', odometer: 1500 } })
    await outbox.enqueue({ id: 'u', entity: 'refuelings', action: 'update', vehicleId: 'v1', targetId: 'a', input: { id: 'a', volume: 10, totalCost: 25, date: '2026-09-01' }, expectedVersion: 1 })
    await outbox.enqueue({ id: 'r', entity: 'refuelings', action: 'restore', vehicleId: 'v1', targetId: 't', expectedVersion: 2 })
    await outbox.enqueue({ id: 'x', entity: 'refuelings', action: 'trash', vehicleId: 'v1', targetId: 'b', expectedVersion: 1 })

    const rows = withChanges('refuelings', [refueling('a'), refueling('b'), refueling('t', { deletedAt: '2026-09-10T00:00:00Z' })], 'v1')
    const byId = new Map(rows.map((r) => [r.id, r]))

    expect(byId.get('n')).toMatchObject({ __typename: 'Refueling', volume: 20, pricePerUnit: 2.5, consumption: null, version: 0, reviewState: 'NONE' })
    expect(byId.get('a')).toMatchObject({ volume: 10, pricePerUnit: 2.5 })
    expect(byId.get('t')?.deletedAt).toBeNull()
    expect(byId.get('b')?.deletedAt).toBeNull() // waiting to be trashed: still listed (marked)
    expect(outbox.markOf('refuelings', 'b')).toBe('deleted')
  })

  it('a restore counts as a save, and an add the server has after all keeps what the server said of it', async () => {
    const { outbox } = await import('../../../src/frontend/offline/outbox.ts')
    const { deviceData } = await import('../../../src/frontend/offline/deviceData.ts')
    const { withChanges } = await import('../../../src/frontend/offline/localResolvers.ts')
    deviceData.reset(memoryStorage())
    await deviceData.signedIn('u1')
    await outbox.reload()
    await outbox.enqueue({ id: 'r', entity: 'refuelings', action: 'restore', vehicleId: 'v1', targetId: 't', expectedVersion: 2 })
    // The add reached the server but its answer did not come back; the next download brought the row.
    await outbox.enqueue({ id: 'n', entity: 'refuelings', action: 'add', vehicleId: 'v1', targetId: 'n', input: { id: 'n', volume: 20 }, sent: true })

    const rows = withChanges('refuelings', [refueling('t', { deletedAt: '2026-09-10T00:00:00Z', version: 2 }), refueling('n', { volume: 20, version: 3, consumption: 6.1 })], 'v1')
    const byId = new Map(rows.map((r) => [r.id, r]))

    expect(byId.get('t')).toMatchObject({ deletedAt: null, version: 3 }) // an edit made now is made from the version the restore leaves
    expect(byId.get('n')).toMatchObject({ volume: 20, version: 3, consumption: 6.1 })
  })
})

describe('vehicles added on this device', () => {
  it('are counted on every page of the home list but listed at the end of the last only, and never twice', async () => {
    const { outbox } = await import('../../../src/frontend/offline/outbox.ts')
    const { deviceData } = await import('../../../src/frontend/offline/deviceData.ts')
    deviceData.reset(memoryStorage())
    await deviceData.signedIn('u1')
    await outbox.reload()
    const card = (id: string) => ({ __typename: 'Vehicle', id, name: id, recurring: [] })
    const pages: Record<number, Record<string, unknown>> = {
      0: { myVehicles: Array.from({ length: 2 }, (_, i) => card(`v${i}`)), myVehicleCount: 3, vehicleTotal: 3 },
      2: { myVehicles: [card('v2'), card('lost')], myVehicleCount: 4, vehicleTotal: 4 }, // the server has one whose answer was lost
    }
    for (const id of ['golf', 'lost']) await outbox.enqueue({ id, entity: 'vehicles', action: 'add', vehicleId: id, targetId: id, input: { id, name: id, fuelType: 'PETROL' } })
    const welcome = async (skip: number) =>
      (await answerLocally(await deviceData.rows(), 'Welcome', { search: null, skip, take: 2 }, async () => pages[skip])) as { myVehicles: { id: string }[]; myVehicleCount: number }

    const first = await welcome(0)
    expect(first.myVehicles.map((v) => v.id)).toEqual(['v0', 'v1']) // the next page is asked with skip 2, the server's own
    expect(first.myVehicleCount).toBe(5)
    expect((await welcome(2)).myVehicles.map((v) => v.id)).toEqual(['v2', 'lost', 'golf'])
  })
})

describe('a visit waiting on this device', () => {
  it('with an amount shows its expense, new, in the vehicle\'s expenses', async () => {
    const { outbox } = await import('../../../src/frontend/offline/outbox.ts')
    const { deviceData } = await import('../../../src/frontend/offline/deviceData.ts')
    const { withChanges } = await import('../../../src/frontend/offline/localResolvers.ts')
    deviceData.reset(memoryStorage())
    await deviceData.signedIn('u1')
    await outbox.reload()
    const input = { ids: ['s1', 's2'], date: '2026-10-05', amount: 120, currency: 'EUR', title: 'Oil, Filter', category: '', odometer: 1500, expenseId: 'e9', photoIds: [] }
    await outbox.enqueue({ id: 'visit', entity: 'recurring', action: 'markDone', vehicleId: 'v1', targetId: 'e9', targetIds: input.ids, input })
    await outbox.enqueue({ id: 'visit2', entity: 'recurring', action: 'markDone', vehicleId: 'v1', targetId: 'visit2', targetIds: ['s3'], input: { ids: ['s3'], date: '2026-10-05', amount: null } })

    const rows = withChanges('expenses', [], 'v1')

    expect(rows).toHaveLength(1) // a visit without an amount logs no expense
    expect(rows[0]).toMatchObject({ __typename: 'Expense', id: 'e9', title: 'Oil, Filter', amount: 120, category: null, version: 1 }) // as the server will create it
    expect(outbox.markOf('expenses', 'e9')).toBe('new')
    expect(outbox.markOf('recurring', 's2')).toBe('done')
  })

  it('moves its schedules on as the server will: no longer due, the new baseline, one version more', async () => {
    const { outbox } = await import('../../../src/frontend/offline/outbox.ts')
    const { deviceData } = await import('../../../src/frontend/offline/deviceData.ts')
    deviceData.reset(memoryStorage())
    await deviceData.signedIn('u1')
    await outbox.reload()
    const status = { __typename: 'RecurrenceStatusInfo', state: 'OVERDUE', limit: 'TIME', dueDate: '2026-09-01', dueOdometer: null, daysLeft: -30, distanceLeft: null }
    const schedule = (id: string) => ({ __typename: 'RecurringExpenseInfo', id, version: 3, title: id, lastDoneDate: '2025-09-01', lastDoneOdometer: 1000, status })
    const seen = { vehicle: { __typename: 'Vehicle', id: 'v1', recurring: [schedule('s1'), schedule('s2')] } }
    await outbox.enqueue({ id: 'visit', entity: 'recurring', action: 'markDone', vehicleId: 'v1', targetId: 'visit', targetIds: ['s1'], input: { ids: ['s1'], date: '2026-10-05', odometer: 1500 } })

    const answer = await answerLocally(await deviceData.rows(), 'RecurringExpenses', { vehicleId: 'v1' }, async (name) => (name === 'RecurringExpenses' ? seen : undefined))
    const [s1, s2] = (answer!.vehicle as { recurring: Record<string, unknown>[] }).recurring

    expect(s1).toMatchObject({ lastDoneDate: '2026-10-05', lastDoneOdometer: 1500, version: 4, status: { state: 'UPCOMING' } }) // not preselected again
    expect(s2).toMatchObject({ lastDoneDate: '2025-09-01', version: 3, status: { state: 'OVERDUE' } })
  })
})
