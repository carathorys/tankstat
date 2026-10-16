import { describe, expect, it } from 'vitest'
import { barPlaces, chartAxes, shortPeriodLabels, type Series } from '../../../../src/frontend/dashboard/chartFormat.ts'

const series = (kind: string, currency: string | null, values: Record<string, number | null>): Series => ({
  kind,
  currency,
  points: Object.entries(values).map(([key, value]) => ({ key, value })),
})

describe('shortPeriodLabels', () => {
  it('names a month alone, with its year under it on the first and on every January, so more of them fit on an axis', () => {
    const labels = shortPeriodLabels(['2025-11', '2025-12', '2026-01', '2026-02'], 'en')
    expect([...labels.values()]).toEqual(['Nov\n2025', 'Dec', 'Jan\n2026', 'Feb'])
  })

  it('in Hungarian too', () => {
    const labels = shortPeriodLabels(['2025-12', '2026-01', '2026-03'], 'hu')
    expect([...labels.values()]).toEqual(['dec.\n2025', 'jan.\n2026', 'márc.'])
  })

  it('shortens a quarter, and leaves a year or a category as it is', () => {
    expect([...shortPeriodLabels(['2025-Q4', '2026-Q1'], 'en').values()]).toEqual(['Q4 ’25', 'Q1 ’26'])
    expect([...shortPeriodLabels(['2025', '2026'], 'en').values()]).toEqual(['2025', '2026'])
    expect([...shortPeriodLabels(['Service', ''], 'en').values()]).toEqual(['Service', ''])
  })
})

describe('chartAxes', () => {
  const huf = series('fuel', 'HUF', { '2026-01': 38000 })
  const eur = series('fuel', 'EUR', { '2026-01': 54 })

  it('gives every currency of a chart of costs a scale of its own, and shows none of them when there are several', () => {
    const axes = chartAxes({ unit: 'CURRENCY', series: [huf, series('expenses', 'HUF', { '2026-01': 4000 }), eur] })

    expect(axes.axes).toEqual([
      { id: 'currency:HUF', currency: 'HUF' },
      { id: 'currency:EUR', currency: 'EUR' },
    ])
    expect([axes.axisOf(huf), axes.axisOf(eur)]).toEqual(['currency:HUF', 'currency:EUR'])
    expect(axes.shown).toBe(false)
  })

  it('shows the axis of a single currency, in that currency', () => {
    expect(chartAxes({ unit: 'CURRENCY', series: [huf] })).toMatchObject({ axes: [{ id: 'currency:HUF', currency: 'HUF' }], shown: true })
  })

  it('shares one axis for any other unit', () => {
    const axes = chartAxes({ unit: 'VOLUME', series: [series('value', null, { '2026-01': 40 })] })
    expect(axes).toMatchObject({ axes: [{ id: 'value', currency: null }], shown: true })
  })
})

describe('barPlaces', () => {
  const periods = ['2026-01', '2026-02', '2026-03']
  const fuelHuf = series('fuel', 'HUF', { '2026-01': 38000, '2026-02': 0, '2026-03': 37000 })
  const expensesHuf = series('expenses', 'HUF', { '2026-01': 4000, '2026-02': 0, '2026-03': 0 })
  const fuelEur = series('fuel', 'EUR', { '2026-01': 54, '2026-02': 120, '2026-03': 0 })

  it('stacked: a bar per currency the period has, side by side, and the top of each stack is rounded', () => {
    const places = barPlaces({ series: [fuelHuf, expensesHuf, fuelEur] }, periods, true)

    expect(places.groups).toBe(2)
    expect([places.group(fuelHuf), places.group(expensesHuf), places.group(fuelEur)]).toEqual([0, 0, 1])
    // January has both currencies: HUF first (fuel at the bottom, expenses on top), EUR beside it.
    expect(places.at(fuelHuf, 0)).toEqual({ place: 0, of: 2, top: false })
    expect(places.at(expensesHuf, 0)).toEqual({ place: 0, of: 2, top: true })
    expect(places.at(fuelEur, 0)).toEqual({ place: 1, of: 2, top: true })
    // February has EUR only: one bar, in the middle, and nothing where HUF would be.
    expect(places.at(fuelHuf, 1)).toBeNull()
    expect(places.at(fuelEur, 1)).toEqual({ place: 0, of: 1, top: true })
    // March has HUF fuel only: it is the top of its stack.
    expect(places.at(fuelHuf, 2)).toEqual({ place: 0, of: 1, top: true })
  })

  it('not stacked: a bar per series the period has', () => {
    const places = barPlaces({ series: [fuelHuf, expensesHuf, fuelEur] }, periods, false)

    expect(places.groups).toBe(3)
    expect([places.at(fuelHuf, 0), places.at(expensesHuf, 0), places.at(fuelEur, 0)]).toEqual([
      { place: 0, of: 3, top: true },
      { place: 1, of: 3, top: true },
      { place: 2, of: 3, top: true },
    ])
    expect(places.at(fuelEur, 1)).toEqual({ place: 0, of: 1, top: true })
  })

  it('finds a value by its period, whatever order the series lists its points in', () => {
    const shuffled = series('fuel', 'EUR', { '2026-03': 10, '2026-01': 0 })
    expect(barPlaces({ series: [shuffled] }, periods, true).at(shuffled, 2)).toEqual({ place: 0, of: 1, top: true })
    expect(barPlaces({ series: [shuffled] }, periods, true).at(shuffled, 0)).toBeNull()
  })
})
