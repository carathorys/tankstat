import { expect, it } from 'vitest'
import { defaultCategory, defaultTitle, MAX_EXPENSE_TITLE, preselect, usesDistance, type DoneItem } from '../../../../src/frontend/recurringDone.ts'

const item = (id: string, state: DoneItem['status']['state'] = 'UPCOMING', over: Partial<DoneItem> = {}): DoneItem => ({ id, title: id, kind: 'TIME', category: null, status: { state }, ...over })

it('starts from the clicked schedule and everything else that needs attention', () => {
  const items = [item('tyres', 'OVERDUE'), item('oil', 'DUE_SOON'), item('wipers'), item('fuelFilter')]

  expect(preselect(items, 'wipers')).toEqual(['wipers', 'tyres', 'oil'])
  expect(preselect(items, 'oil')).toEqual(['oil', 'tyres'])
})

it("names the visit's expense after the schedules, as the server would, cut to fit", () => {
  expect(defaultTitle([item('Oil change'), item('Oil filter')])).toBe('Oil change, Oil filter')
  const long = defaultTitle([item('a'.repeat(70)), item('b'.repeat(70))])
  expect(long).toHaveLength(MAX_EXPENSE_TITLE)
  expect(long.endsWith('…')).toBe(true)

  expect(defaultCategory([item('a'), item('b', 'UPCOMING', { category: ' Service ' }), item('c', 'UPCOMING', { category: 'Fees' })])).toBe('Service')
  expect(defaultCategory([item('a')])).toBe('')
})

it('needs the odometer as soon as one of them counts distance', () => {
  expect(usesDistance([item('a'), item('b')])).toBe(false)
  expect(usesDistance([item('a'), item('b', 'UPCOMING', { kind: 'COMBINED' })])).toBe(true)
})
