import { describe, expect, it } from 'vitest'
import type { TimelineChoices } from '../../../src/settings/timelineChoices.ts'
import { beforeTracking, boundariesOf, dayOf, draggedDay, gapsOf, isoOf, marksOf, periodsOf, pxPerDay, shownSets, spanOf, ticksOf } from '../../../src/tyres/timeline/model.ts'
import type { Tyres } from '../../../src/tyres/tyres.ts'

// The tyre timeline's arithmetic (#332): periods, gaps, swap boundaries, the range, the scale and the filters.

const installation = (id: string, setId: string, from: string, until: string | null) => ({
  installation: { id, setId, from, until, pairOneFront: true, version: 1 },
  distance: { distance: 1000, approximate: false, measure: 'MEASURED' as const },
})
const set = (id: string, season: 'SUMMER' | 'WINTER' | 'ALL_SEASON', installations: ReturnType<typeof installation>[], over: Record<string, unknown> = {}) => ({
  onTheCar: installations.some((i) => i.installation.until === null),
  reminder: null,
  set: { id, vehicleId: 'v1', name: id, season, size: null, since: '2023-03-01', madeWeek: null, madeYear: null, roughDistance: 0, roughUntil: null, retiredOn: null, isRetired: false, brokenUpOn: null, version: 1, usualMonths: null, reminderMonth: null, reminderDismissedFor: null, ...over },
  figures: { distance: 0, approximate: false, measured: 0, pairOneFront: 0, pairOneRear: 0, pairsUnknown: 0, suggestion: null },
  spent: [], repairs: 0, installations, tyres: [],
})
const line = (id: string, kind: Tyres['entries'][number]['kind'], setId: string | null, day: string) => ({
  id, kind, setId, installationId: kind === 'SWAP' || kind === 'POSITIONS_CHANGED' ? id : null, day, odometer: null, note: null, version: 1,
  expenseId: null, amount: null, currency: null, category: null, expenseTrashed: false, tyreId: null, groupId: null,
})
// Winter on, summer, a fortnight on the spare, a gap of two weeks, winter, summer, winter on the car.
const tyres = {
  vehicleId: 'v1', hasReadings: true, readings: [], expenseCategory: null, spentAll: [], spentNoSet: [],
  entries: [line('i2', 'SWAP', 's', '2025-04-15'), line('i3', 'SWAP', 'a', '2025-11-08'), line('i4', 'SWAP', 'w', '2025-12-06'), line('i5', 'SWAP', 's', '2026-03-28'), line('i6', 'SWAP', 'w', '2026-11-02'),
    line('m1', 'PUNCTURE_REPAIRED', 's', '2025-07-19'), line('m2', 'NOTE', null, '2025-07-20')],
  sets: [
    set('s', 'SUMMER', [installation('i5', 's', '2026-03-28', '2026-11-02'), installation('i2', 's', '2025-04-15', '2025-11-08')], { roughDistance: 30_000, roughUntil: '2025-04-15' }),
    set('w', 'WINTER', [installation('i6', 'w', '2026-11-02', null), installation('i4', 'w', '2025-12-06', '2026-03-28'), installation('i1', 'w', '2024-11-05', '2025-04-15')], { since: '2022-11-01', roughDistance: 20_000 }),
    set('a', 'ALL_SEASON', [installation('i3', 'a', '2025-11-08', '2025-11-22')], { since: '2025-11-01' }),
    set('o', 'WINTER', [], { since: '2019-11-01', isRetired: true, retiredOn: '2022-11-01', roughDistance: 40_000, roughUntil: '2022-11-01' }),
  ],
} as unknown as Tyres
const choices = (over: Partial<TimelineChoices> = {}): TimelineChoices => ({ range: 'ALL', layers: ['BEFORE_TRACKING', 'GAPS', 'MARKS', 'TABLE'], hiddenSets: [], ...over })
const today = dayOf('2026-11-16')

describe('the periods, the gaps and the swaps', () => {
  const periods = periodsOf(tyres)

  it('lists every period oldest first, and a day as a number goes back to itself', () => {
    expect(periods.map((p) => p.id)).toEqual(['i1', 'i2', 'i3', 'i4', 'i5', 'i6'])
    expect(isoOf(dayOf('2026-02-28') + 1)).toBe('2026-03-01')
    expect(periods.at(-1)).toMatchObject({ setId: 'w', until: null })
  })

  it('a gap runs from the day a set came off to the day the next went on; nothing after the last period is one', () => {
    expect(gapsOf(periods).map((g) => [isoOf(g.from), isoOf(g.until), g.before.id, g.after.id])).toEqual([['2025-11-22', '2025-12-06', 'i3', 'i4']])
    expect(gapsOf(periods.slice(0, 3))).toEqual([]) // the spare came off and nothing went on: the end, not a gap
    expect(gapsOf(periodsOf({ ...tyres, sets: tyres.sets.filter((s) => s.set.id !== 'a') } as Tyres)).map((g) => isoOf(g.from))).toEqual(['2025-11-08'])
  })

  it('a hidden set leaves no gap: the gaps come from all the periods', () => {
    const hidden = choices({ hiddenSets: ['a'] })
    expect(shownSets(tyres, hidden).map((s) => s.set.id)).toEqual(['s', 'w'])
    expect(gapsOf(periods)).toHaveLength(1) // worked out before the filter, so the spare's fortnight is not one
  })

  it('a boundary is a period starting the day another ends, with the line of the one that starts', () => {
    const swaps = boundariesOf(periods, tyres.entries)
    expect(swaps.map((b) => [isoOf(b.day), b.before.setId, b.after.setId, b.line.id])).toEqual([
      ['2025-04-15', 'w', 's', 'i2'], ['2025-11-08', 's', 'a', 'i3'], ['2026-03-28', 'w', 's', 'i5'], ['2026-11-02', 's', 'w', 'i6'],
    ])
    expect(swaps.some((b) => b.after.id === 'i4')).toBe(false) // after the gap: nothing came off that day
  })

  it('a change of positions inside one set is a boundary too', () => {
    const rotated = { ...tyres, entries: [line('r2', 'POSITIONS_CHANGED', 's', '2026-06-01')], sets: [set('s', 'SUMMER', [installation('r2', 's', '2026-06-01', null), installation('r1', 's', '2026-03-28', '2026-06-01')])] } as unknown as Tyres
    expect(boundariesOf(periodsOf(rotated), rotated.entries)).toMatchObject([{ before: { id: 'r1', setId: 's' }, after: { id: 'r2', setId: 's' }, line: { kind: 'POSITIONS_CHANGED' } }])
  })
})

describe('what is drawn around the periods', () => {
  const periods = periodsOf(tyres)
  const of = (id: string) => tyres.sets.find((s) => s.set.id === id)!

  it('the time before tracking runs from in use since to where the rough figure stops, or to the first period', () => {
    expect(beforeTracking(of('s'), periods)).toEqual({ from: dayOf('2023-03-01'), until: dayOf('2025-04-15') })
    expect(beforeTracking(of('w'), periods)).toEqual({ from: dayOf('2022-11-01'), until: dayOf('2024-11-05') }) // no day for the figure: up to its first period
    expect(beforeTracking(of('a'), periods)).toBeNull() // no rough figure
    expect(beforeTracking(of('o'), periods)).toEqual({ from: dayOf('2019-11-01'), until: dayOf('2022-11-01') })
  })

  it('retired sets show only when asked for, and the marks are a set\'s lines that are no swap', () => {
    expect(shownSets(tyres, choices()).map((s) => s.set.id)).toEqual(['s', 'w', 'a'])
    expect(shownSets(tyres, choices({ layers: ['RETIRED_SETS'] })).map((s) => s.set.id)).toEqual(['s', 'w', 'a', 'o'])
    expect(marksOf(tyres.entries, 's').map((e) => e.id)).toEqual(['m1'])
  })
})

describe('the range and the scale', () => {
  const periods = periodsOf(tyres)

  it('the whole range starts at the oldest set\'s month, the others a whole number of months back, all ending after next month', () => {
    expect([isoOf(spanOf('ALL', tyres, periods, today).start), isoOf(spanOf('ALL', tyres, periods, today).end)]).toEqual(['2019-11-01', '2027-01-01'])
    expect(isoOf(spanOf('TWO_YEARS', tyres, periods, today).start)).toBe('2024-11-01')
    expect(isoOf(spanOf('TWELVE_MONTHS', tyres, periods, today).start)).toBe('2025-11-01')
  })

  it('the picture fits its room at 1×, never under the minimum, and zoom multiplies it', () => {
    const year = { start: dayOf('2025-11-01'), end: dayOf('2026-11-01') }
    expect(pxPerDay(730, year, 1)).toBe(2)
    expect(pxPerDay(730, year, 8)).toBe(16)
    expect(pxPerDay(100, year, 1)).toBe(0.55)
  })

  it('ticks as many months as have room for their names: every month, quarter, half year or year, the years marked', () => {
    const year = { start: dayOf('2025-11-01'), end: dayOf('2027-01-01') }
    const wide = ticksOf(year, 2)
    expect(wide).toHaveLength(14)
    expect(wide.filter((t) => t.year).map((t) => isoOf(t.day))).toEqual(['2026-01-01'])
    expect(ticksOf(year, 0.8).map((t) => t.month)).toEqual([1, 4, 7, 10]) // a phone at 1×: a month is 24 px, a quarter has room
    const all = spanOf('ALL', tyres, periods, today)
    expect(ticksOf(all, 0.55).every((t) => (t.month - 1) % 3 === 0)).toBe(true)
    expect(ticksOf(all, 0.55).filter((t) => t.year)).toHaveLength(7) // 2020 to 2026
    expect(ticksOf(all, 0.1).every((t) => t.year)).toBe(true)
  })

  it('a drag moves the swap by whole days', () => {
    expect(isoOf(draggedDay(dayOf('2026-11-02'), -18, 2))).toBe('2026-10-24')
    expect(draggedDay(100, 0.4, 2)).toBe(100)
  })
})
