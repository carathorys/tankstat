import { describe, expect, it } from 'vitest'
import { tyreInputOf } from '../../../src/tyres/expenseTyreInput.ts'
import type { Tyres } from '../../../src/tyres/tyres.ts'
import { tyreLineIds, type TyreDetailsIds, type TyreDetailsState } from '../../../src/tyres/useExpenseTyreDetails.ts'
import { brokenUpSummer, fakeTyres, tyreSummer } from '../../support/tyres.ts'

// An expense's tyre details (#290) as sent: a waiting change's line ids stay with their tyres when its edit picks fewer or others (#330).

const state = (over: Partial<TyreDetailsState>): TyreDetailsState => ({ on: true, kind: 'PUNCTURE_REPAIRED', setId: 's1', whole: false, picked: [], labels: [], ...over })
const ids = (byTyre: TyreDetailsIds['byTyre'] = new Map()): TyreDetailsIds => ({ line: 'L0', lines: ['f1', 'f2', 'f3', 'f4'], newTyres: ['n1', 'n2', 'n3', 'n4'], byTyre })

describe('tyreInputOf', () => {
  const tyres = fakeTyres([brokenUpSummer()]).answer() as unknown as Tyres

  it("keeps each tyre's line id from the waiting change, by its tyre, whatever is picked besides", () => {
    const waiting = tyreLineIds({ kind: 'PUNCTURE_REPAIRED', setId: 's1', tyres: [{ tyreId: 't1', id: 'L1' }, { tyreId: 't2', id: 'L2' }] })

    expect(tyreInputOf(state({ picked: ['t2'] }), tyres, ids(waiting), '2026-10-01').tyre.tyres).toEqual([{ tyreId: 't2', id: 'L2' }])
    expect(tyreInputOf(state({ picked: ['t3', 't2'] }), tyres, ids(waiting), '2026-10-01').tyre.tyres).toEqual([{ tyreId: 't2', id: 'L2' }, { tyreId: 't3', id: 'f1' }])
  })

  it("keeps a whole set's lines by the tyres its break-up makes", () => {
    const whole = fakeTyres([tyreSummer]).answer() as unknown as Tyres
    const waiting = tyreLineIds({ kind: 'PUNCTURE_REPAIRED', setId: 's1', tyres: [{ tyreId: 'n1', id: 'L1' }, { tyreId: 'n3', id: 'L3' }] })

    expect(tyreInputOf(state({ picked: ['REAR_LEFT'] }), whole, ids(waiting), '2026-10-01').tyre.tyres).toEqual([{ tyreId: 'n3', id: 'L3' }])
  })
})
