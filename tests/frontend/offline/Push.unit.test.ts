import { describe, expect, it } from 'vitest'
import type { Change } from '../../../src/frontend/offline/changes.ts'
import { batches, toChangeInput } from '../../../src/frontend/offline/push.ts'

let seq = 0
const change = (over: Partial<Change>): Change => ({ id: `c${++seq}`, seq, createdAt: seq, entity: 'refuelings', action: 'add', vehicleId: 'v1', targetId: `t${seq}`, ...over })

describe('toChangeInput', () => {
  it('sends each change as its one operation, with only what that input takes', () => {
    const add = change({ id: 'r1', targetId: 'r1', input: { id: 'r1', vehicleId: 'v1', date: '2026-10-01', volume: 30, totalCost: 90, currency: 'EUR', odometer: 2000, isFullTank: true, photoIds: [], unitPrice: 3 } })
    expect(toChangeInput(add)).toEqual({
      id: 'r1', expectedVersion: null,
      logRefueling: { id: 'r1', vehicleId: 'v1', date: '2026-10-01', volume: 30, totalCost: 90, currency: 'EUR', odometer: 2000, isFullTank: true, photoIds: [] },
    })
    expect(toChangeInput(change({ id: 'x', action: 'trash', targetId: 'r9', expectedVersion: 4 }))).toEqual({ id: 'x', expectedVersion: 4, deleteRefueling: 'r9' })
    expect(toChangeInput(change({ id: 'u', entity: 'vehicles', action: 'update', targetId: 'v1', expectedVersion: 2, input: { id: 'v1', name: 'Golf', fuelType: 'PETROL', units: { distance: 'KILOMETERS', volume: 'LITERS' } } })))
      .toEqual({ id: 'u', expectedVersion: 2, updateVehicle: { id: 'v1', name: 'Golf', fuelType: 'PETROL', units: { distance: 'KILOMETERS', volume: 'LITERS' } } })
    expect(toChangeInput(change({ id: 'd', entity: 'recurring', action: 'markDone', targetId: 'e1', targetIds: ['s1', 's2'], input: { ids: ['s1'], date: '2026-10-02', amount: 50, currency: 'EUR', title: 'Oil', category: '', expenseId: 'e1', photoIds: [] } })))
      .toEqual({ id: 'd', expectedVersion: null, markRecurringExpensesDone: { ids: ['s1', 's2'], date: '2026-10-02', amount: 50, currency: 'EUR', title: 'Oil', category: '', expenseId: 'e1', photoIds: [] } })
    expect(toChangeInput(change({ id: 's', entity: 'recurring', action: 'trash', targetId: 's3' }))).toEqual({ id: 's', expectedVersion: null, deleteRecurringExpense: 's3' })
  })
})

describe('batches', () => {
  it('sends the vehicles added here first, the rest in order, and never a change with the add it depends on', () => {
    const log = change({ id: 'log', vehicleId: 'golf' })
    const other = change({ id: 'other', action: 'trash', targetId: 'r1' })
    const golf = change({ id: 'golf', entity: 'vehicles', action: 'add', vehicleId: 'golf', targetId: 'golf' })
    const schedule = change({ id: 's1', entity: 'recurring', action: 'add', targetId: 's1' })
    const visit = change({ id: 'visit', entity: 'recurring', action: 'markDone', targetId: 'e1', targetIds: ['s1'] })

    expect(batches([log, other, golf, schedule, visit]).map((b) => b.map((c) => c.id))).toEqual([['golf'], ['log', 'other', 's1'], ['visit']])
  })

  it('cuts requests at the size given', () => {
    const many = Array.from({ length: 5 }, () => change({ action: 'trash' }))
    expect(batches(many, 2).map((b) => b.length)).toEqual([2, 2, 1])
  })
})
