import { describe, expect, it } from 'vitest'
import { createApolloClient } from '../../../src/frontend/apolloClient.ts'
import { baseOf, changedOnServer, same, situationOf, valuesOf, type Current } from '../../../src/frontend/offline/conflicts.ts'
import { RefuelingChangeValuesFragmentDoc, type RefuelingChangeValuesFragment } from '../../../src/frontend/gql/generated.ts'

const now = (state: 'LIVE' | 'TRASHED', lastChange: string | null, entity: Record<string, unknown> = {}): NonNullable<Current> =>
  ({ state, version: 2, changedAt: null, lastChange, changedBy: null, vehicle: null, refueling: null, expense: null, schedule: null, ...entity }) as NonNullable<Current>

describe('valuesOf', () => {
  it('takes what a change can set, in its input’s shape, and only what the entry holds', () => {
    expect(valuesOf('vehicles', { __typename: 'Vehicle', id: 'v1', version: 3, name: 'Golf', units: { __typename: 'MeasurementUnits', distance: 'KILOMETERS', volume: 'LITERS' } }))
      .toEqual({ name: 'Golf', units: { distance: 'KILOMETERS', volume: 'LITERS' } })
    expect(valuesOf('refuelings', { volume: 40, note: null, consumption: 6.1 })).toEqual({ volume: 40, note: null })
    expect(valuesOf('expenses', null)).toEqual({})
  })
})

describe('same', () => {
  it('counts blank and null alike, typed and stored numbers alike, and objects by their fields', () => {
    expect(same('', null)).toBe(true)
    expect(same(undefined, null)).toBe(true)
    expect(same('40', 40)).toBe(true)
    expect(same(' 40.5 ', 40.5)).toBe(true)
    expect(same('40', 41)).toBe(false)
    expect(same('2026-09-01', '2026-09-01')).toBe(true)
    expect(same({ distance: 'KILOMETERS', volume: 'LITERS', __typename: 'MeasurementUnits' }, { distance: 'KILOMETERS', volume: 'LITERS' })).toBe(true)
    expect(same({ distance: 'MILES' }, { distance: 'KILOMETERS' })).toBe(false)
    expect(same(false, null)).toBe(false)
  })
})

describe('changedOnServer', () => {
  it('lists what changed since the values the change was made from, only fields both know', () => {
    const current = now('LIVE', 'EDITED', { refueling: { volume: 41, note: 'Anna', odometer: 1000, date: '2026-09-01' } })
    expect(changedOnServer('refuelings', { volume: 40, note: null, odometer: '1000' }, current)).toEqual(['volume', 'note'])
    expect(changedOnServer('refuelings', undefined, current)).toEqual([])
    expect(changedOnServer('refuelings', { volume: 40 }, null)).toEqual([])
  })
})

describe('situationOf', () => {
  const trash = { action: 'trash' as const, entity: 'refuelings' as const }
  const edit = { action: 'update' as const, entity: 'refuelings' as const }

  it('says where a trash stands: changed, trashed and back, already in the trash, or gone', () => {
    expect(situationOf(trash, now('LIVE', 'EDITED'), 'sync.versionMismatch')).toBe('changed')
    expect(situationOf(trash, now('LIVE', 'RESTORED'), 'sync.versionMismatch')).toBe('restored')
    expect(situationOf(trash, now('TRASHED', 'TRASHED'), 'refueling.notFound')).toBe('alreadyTrashed')
    expect(situationOf(trash, null, 'refueling.notFound')).toBe('gone')
  })

  it('says where an edit stands; one a rule refused, or another kind of change, is decided as before', () => {
    expect(situationOf(edit, now('TRASHED', 'TRASHED'), 'refueling.notFound')).toBe('trashedMeanwhile')
    expect(situationOf(edit, now('LIVE', 'EDITED'), 'sync.versionMismatch')).toBe('edited')
    expect(situationOf(edit, null, 'refueling.notFound')).toBe('gone')
    expect(situationOf(edit, now('LIVE', 'EDITED'), 'odometer.belowPrevious')).toBeNull()
    expect(situationOf(edit, null, 'odometer.belowPrevious')).toBeNull()
    expect(situationOf({ action: 'add', entity: 'refuelings' }, null, 'odometer.belowPrevious')).toBeNull()
  })
})

describe('baseOf', () => {
  it('reads what the screen holds of the entry, only at the version the change is made from', () => {
    const client = createApolloClient('http://localhost/graphql')
    client.cache.writeFragment({
      id: client.cache.identify({ __typename: 'Refueling', id: 'r1' }),
      fragment: RefuelingChangeValuesFragmentDoc,
      // As the server answers it (the generated types leave __typename out).
      data: { __typename: 'Refueling', version: 2, date: '2026-09-01', volume: 40, totalCost: 60, currency: 'EUR', odometer: 1000, isFullTank: true, missedPreviousFillUp: false, note: null } as RefuelingChangeValuesFragment,
    })

    expect(baseOf(client, 'refuelings', 'r1', 2)).toMatchObject({ volume: 40, note: null, currency: 'EUR' })
    expect(baseOf(client, 'refuelings', 'r1', 1)).toBeUndefined() // another version: its values are not known
    expect(baseOf(client, 'refuelings', 'r2', 2)).toBeUndefined()
    expect(baseOf(client, 'refuelings', 'r1', null)).toBeUndefined()
  })
})
