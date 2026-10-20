import { expect, it } from 'vitest'
import { defaultCategory, defaultTitle, MAX_EXPENSE_TITLE, preselect, sameCategory, usesDistance, type DoneItem } from '../../../src/recurringDone.ts'

const item = (id: string, state: DoneItem['status']['state'] = 'UPCOMING', over: Partial<DoneItem> = {}): DoneItem => ({ id, title: id, kind: 'TIME', category: null, status: { state }, ...over })

it('starts from the clicked schedule and the others of its category that need attention', () => {
  const items = [
    item('oil', 'DUE_SOON', { category: 'Service' }),
    item('airFilter', 'OVERDUE', { category: 'service ' }),
    item('vignette', 'DUE_SOON', { category: 'Fees' }),
    item('insurance', 'OVERDUE', { category: 'Insurance' }),
    item('wipers', 'OVERDUE'),
    item('bulbs', 'OVERDUE'),
    item('brakeFluid', 'UPCOMING', { category: 'Service' }), // not due yet: ticked only in a visit opened from it
  ]

  expect(preselect(items, 'oil')).toEqual(['oil', 'airFilter'])
  expect(preselect(items, 'brakeFluid')).toEqual(['brakeFluid', 'oil', 'airFilter'])
  expect(preselect(items, 'vignette')).toEqual(['vignette'])
  expect(preselect(items, 'wipers')).toEqual(['wipers']) // no category: alone, not with bulbs, which has none either
})

it('compares categories as typed text: trimmed, ignoring case but not accents', () => {
  // No capital I in these pairs: the comparison uses the runtime's default locale, and under a Turkish one I is the capital of ı, not of i.
  expect(sameCategory('Service', ' service ')).toBe(true)
  expect(sameCategory('Fees', ' FEES ')).toBe(true)
  expect(sameCategory('Szerviz', 'Szervíz')).toBe(false)
  expect(sameCategory('Service', 'Fees')).toBe(false)
  expect(sameCategory(null, null)).toBe(false)
  expect(sameCategory(' ', '')).toBe(false)
})

it("prefills the visit's expense with the clicked schedule's category, as only its category starts ticked", () => {
  const items = [item('Oil change', 'DUE_SOON', { category: 'Service' }), item('Vignette', 'DUE_SOON', { category: 'Fees' })]
  const ids = preselect(items, 'Vignette')
  const ticked = items.filter((i) => ids.includes(i.id)) // what the dialog starts with, in the list's order

  expect([defaultTitle(ticked), defaultCategory(ticked)]).toEqual(['Vignette', 'Fees'])
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
