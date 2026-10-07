import { describe, expect, it } from 'vitest'
import { formatRule, fromDate, narrower, parseRule } from '../../../src/frontend/offline/offlineWindow.ts'

// Local time, like the device's clock: 7 October 2026, 10:30.
const now = new Date(2026, 9, 7, 10, 30, 0)

describe('parseRule / formatRule', () => {
  it.each(['none', 'all', 'thisYear', 'thisAndLastYear', 'from:2024-02-29', 'span:P2M', 'span:P2Y6M4DT2H48M12S', 'span:PT12S'])('reads %s and writes it back', (rule) => {
    expect(formatRule(parseRule(rule)!)).toBe(rule)
  })

  it.each(['', 'everything', 'from:2024-02-30', 'from:1899-12-31', 'span:P', 'span:PT', 'span:P0D', 'span:P1W', 'span:P1.5Y', 'span:2M'])('refuses %j', (rule) => {
    expect(parseRule(rule)).toBeNull()
  })
})

describe('fromDate', () => {
  it('turns every kind of rule into the day the window starts, on the device clock', () => {
    expect(fromDate('none', now)).toBe(false)
    expect(fromDate('all', now)).toBeNull()
    expect(fromDate('thisYear', now)).toBe('2026-01-01')
    expect(fromDate('thisAndLastYear', now)).toBe('2025-01-01')
    expect(fromDate('from:2024-02-29', now)).toBe('2024-02-29')
    expect(fromDate('span:P2M', now)).toBe('2026-08-07')
    expect(fromDate('span:P2Y6M4D', now)).toBe('2024-04-03')
  })

  it('counts months back on the calendar, a shorter month ending at its last day', () => {
    expect(fromDate('span:P1M', new Date(2026, 2, 31, 12))).toBe('2026-02-28')
    expect(fromDate('span:P1Y', new Date(2024, 1, 29, 12))).toBe('2023-02-28')
    expect(fromDate('span:P13M', new Date(2026, 0, 15, 12))).toBe('2024-12-15')
  })

  it('rounds hours, minutes and seconds down to the whole day', () => {
    expect(fromDate('span:PT2H', now)).toBe('2026-10-07') // 08:30 the same day
    expect(fromDate('span:PT12H', now)).toBe('2026-10-06') // 22:30 the day before
    expect(fromDate('span:P1DT10H30M1S', now)).toBe('2026-10-05')
  })

  it('without a valid rule, takes the default of the last two months', () => {
    expect(fromDate(null, now)).toBe('2026-08-07')
    expect(fromDate('nonsense', now)).toBe('2026-08-07')
  })
})

describe('narrower', () => {
  it('tells which window takes in fewer days', () => {
    expect(narrower('2026-05-01', '2026-01-01')).toBe(true)
    expect(narrower('2026-01-01', '2026-05-01')).toBe(false)
    expect(narrower('2026-01-01', null)).toBe(true)
    expect(narrower(null, '2026-01-01')).toBe(false)
    expect(narrower(false, null)).toBe(true)
    expect(narrower('2026-01-01', false)).toBe(false)
    expect(narrower('2026-01-01', '2026-01-01')).toBe(false)
  })
})
