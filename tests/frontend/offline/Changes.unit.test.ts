import { describe, expect, it } from 'vitest'
import { collapse, discard, markOf, type Change } from '../../../src/frontend/offline/changes.ts'

let seq = 0
const change = (action: Change['action'], targetId = 'r1', over: Partial<Change> = {}): Change => ({
  id: `${action}-${++seq}`,
  seq,
  createdAt: seq,
  entity: 'refuelings',
  action,
  vehicleId: 'v1',
  targetId,
  ...over,
})
const add = (id = 'r1', input: Record<string, unknown> = { id, vehicleId: 'v1', date: '2026-10-01', volume: 40, totalCost: 100, currency: 'EUR' }) =>
  change('add', id, { id, input })
const fold = (...changes: Change[]) => changes.reduce<Change[]>((list, c) => collapse(list, c), [])

describe('collapse', () => {
  it('an add then an update is the add with the new values', () => {
    const [only, ...rest] = fold(add(), change('update', 'r1', { input: { id: 'r1', date: '2026-10-02', volume: 41, note: 'x' } }))
    expect(rest).toEqual([])
    expect(only).toMatchObject({ action: 'add', id: 'r1', input: { id: 'r1', vehicleId: 'v1', date: '2026-10-02', volume: 41, note: 'x', totalCost: 100 } })
  })

  it('an add then a trash is nothing at all, whatever came in between', () => {
    expect(fold(add(), change('update', 'r1', { input: { volume: 2 } }), change('trash'))).toEqual([])
  })

  it('updates fold into the last, made from the first one’s version', () => {
    const first = change('update', 'r1', { input: { volume: 1 }, expectedVersion: 3 })
    const [only, ...rest] = fold(first, change('update', 'r1', { input: { volume: 2 }, expectedVersion: 4 }))
    expect(rest).toEqual([])
    expect(only).toMatchObject({ id: first.id, seq: first.seq, action: 'update', input: { volume: 2 }, expectedVersion: 3 })
  })

  it('an update then a trash is the trash, from the update’s version', () => {
    const update = change('update', 'r1', { input: { volume: 1 }, expectedVersion: 3 })
    const [only] = fold(update, change('trash', 'r1', { expectedVersion: 9 }))
    expect(only).toMatchObject({ id: update.id, action: 'trash', expectedVersion: 3 })
  })

  it('a trash then a restore, or a restore then a trash, is nothing', () => {
    expect(fold(change('trash'), change('restore'))).toEqual([])
    expect(fold(change('restore'), change('trash'))).toEqual([])
  })

  it('a second trash or restore, or an edit of a log on its way to the trash, changes nothing', () => {
    const trash = change('trash', 'r1', { expectedVersion: 2 })
    expect(fold(trash, change('trash'))).toEqual([trash])
    expect(fold(trash, change('update', 'r1', { input: { volume: 3 } }))).toEqual([trash])
    const restore = change('restore', 'r2')
    expect(fold(restore, change('restore', 'r2'))).toEqual([restore])
  })

  it('keeps other logs’ changes, and their order', () => {
    const a = add('r1')
    const b = change('trash', 'r2')
    const c = change('update', 'r3', { input: { volume: 1 } })
    expect(fold(a, b, c, change('update', 'r1', { input: { volume: 9 } })).map((x) => x.targetId)).toEqual(['r1', 'r2', 'r3'])
    expect(fold(a, b, c, change('trash', 'r1')).map((x) => x.targetId)).toEqual(['r2', 'r3'])
  })

  it('tells a refuelling from an expense with the same id', () => {
    const list = fold(add('x'), change('trash', 'x', { entity: 'expenses' }))
    expect(list.map((c) => `${c.entity}:${c.action}`)).toEqual(['refuelings:add', 'expenses:trash'])
  })
})

describe('discard and markOf', () => {
  it('takes back one change, and says how its log is marked', () => {
    const list = fold(add('r1'), change('trash', 'r2'))
    expect(discard(list, 'r1').map((c) => c.targetId)).toEqual(['r2'])
    expect(list.map(markOf)).toEqual(['new', 'deleted'])
    expect(markOf(change('update'))).toBe('changed')
    expect(markOf(change('restore'))).toBe('restored')
  })
})
