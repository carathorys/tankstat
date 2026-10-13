import { renderHook } from '@testing-library/react'
import { expect, it } from 'vitest'
import { useFormat } from '../../../../src/frontend/i18n/format.ts'

it('shows a size on disk in the unit that suits it, never as 1,000 of a smaller one', () => {
  const { bytes } = renderHook(() => useFormat()).result.current

  expect([999, 999.6, 999_960, 5_300_000, 340_000_000, 999_960_000].map(bytes)).toEqual(['999 byte', '1 kB', '1 MB', '5.3 MB', '340 MB', '1 GB'])
})

it('writes a time of day the way the language does, whatever the time zone, and leaves anything else as it came', () => {
  const { timeOfDay } = renderHook(() => useFormat()).result.current

  // English writes 02:00 as 2:00 AM (with a narrow space before AM in newer data, so any space is accepted).
  expect(timeOfDay('02:00')).toMatch(/^2:00\sAM$/)
  expect(timeOfDay('2:30')).toMatch(/^2:30\sAM$/)
  expect(timeOfDay('23:59')).toMatch(/^11:59\sPM$/)
  expect(timeOfDay('02.00')).toBe('02.00')
})
