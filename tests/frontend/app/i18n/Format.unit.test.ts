import { renderHook } from '@testing-library/react'
import { expect, it } from 'vitest'
import { useFormat } from '../../../../src/frontend/i18n/format.ts'

it('shows a size on disk in the unit that suits it, never as 1,000 of a smaller one', () => {
  const { bytes } = renderHook(() => useFormat()).result.current

  expect([999, 999.6, 999_960, 5_300_000, 340_000_000, 999_960_000].map(bytes)).toEqual(['999 byte', '1 kB', '1 MB', '5.3 MB', '340 MB', '1 GB'])
})
