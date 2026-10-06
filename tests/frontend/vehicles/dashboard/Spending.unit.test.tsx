import { renderHook } from '@testing-library/react'
import { expect, it } from 'vitest'
import { useFormat } from '../../../../src/frontend/i18n/format.ts'
import { spentText } from '../../../../src/frontend/spending.ts'

const format = () => renderHook(() => useFormat()).result.current

it('shows every currency that was spent in, the main one first, never converted', () => {
  const spending = [
    { currency: 'HUF', thisMonth: 52000, lastMonth: 0 },
    { currency: 'EUR', thisMonth: 73.9, lastMonth: 0 },
    { currency: 'CHF', thisMonth: 0, lastMonth: 30 },
  ]

  expect(spentText(spending, 'thisMonth', format())).toMatch(/^HUF\s?52,000(\.00)? · €73\.90$/)
  expect(spentText(spending, 'lastMonth', format())).toMatch(/^CHF\s?30\.00$/)
})

it("shows the main currency's zero when nothing was spent, and nothing without any currency", () => {
  expect(spentText([{ currency: 'HUF', thisMonth: 0, lastMonth: 0 }], 'thisMonth', format())).toMatch(/^HUF\s?0(\.00)?$/)
  expect(spentText([], 'thisMonth', format())).toBeNull()
})
