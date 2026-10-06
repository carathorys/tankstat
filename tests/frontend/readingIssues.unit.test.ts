import { describe, expect, it } from 'vitest'
import { mergeIssues, problemsToTell, type ReadingIssue } from '../../src/frontend/recognition/readingIssues.ts'
import type { ReadValues } from '../../src/frontend/recognition/readValues.ts'

const issue = (code: ReadingIssue['code'], field: ReadingIssue['field'] = null): ReadingIssue => ({ code, field })

/** A refuelling dialog: it has a volume, a total, a currency, a date and an odometer, but no unit price. */
const labels: Record<string, string> = { VOLUME: 'Volume', TOTAL: 'Total cost', CURRENCY: 'Currency', DATE: 'Date', ODOMETER: 'Odometer' }
const label = (name: string) => labels[name]

const nothing: ReadValues = {}
const odometerRead: ReadValues = { ODOMETER: { value: '123789', confidence: 0.9 } }

describe('mergeIssues', () => {
  it('keeps the reasons of finished readings only, each reason and field once, in order', () => {
    const merged = mergeIssues([
      { status: 'READ', issues: [issue('ODOMETER_BELOW_LATEST', 'ODOMETER'), issue('UNSURE', 'VOLUME')] },
      { status: 'READ', issues: [issue('UNSURE', 'VOLUME'), issue('NOTHING_LEGIBLE')] },
      { status: 'QUEUED', issues: [issue('UNRECOGNISED')] },
      { status: 'FAILED', issues: [] },
      null,
      undefined,
    ])

    expect(merged).toEqual([issue('ODOMETER_BELOW_LATEST', 'ODOMETER'), issue('UNSURE', 'VOLUME'), issue('NOTHING_LEGIBLE')])
  })

  it('copies what it keeps (a GraphQL result has more on it)', () => {
    const [kept] = mergeIssues([{ status: 'READ', issues: [{ code: 'UNSURE', field: 'TOTAL', __typename: 'ReadingIssueInfo' } as ReadingIssue] }])

    expect(Object.keys(kept!)).toEqual(['code', 'field'])
  })
})

describe('problemsToTell', () => {
  it('tells a reason about a value the dialog has a field for and no photo filled', () => {
    expect(problemsToTell([issue('ODOMETER_BELOW_LATEST', 'ODOMETER')], label, nothing)).toEqual([{ code: 'ODOMETER_BELOW_LATEST', field: undefined }])
    expect(problemsToTell([issue('NOT_UNDERSTOOD', 'TOTAL')], label, nothing)).toEqual([{ code: 'NOT_UNDERSTOOD', field: 'Total cost' }]) // this sentence starts with the field
  })

  it('leaves out a reason about a field another photo filled, and about a field the dialog does not have', () => {
    const issues = [issue('ODOMETER_FAR_ABOVE', 'ODOMETER'), issue('UNSURE', 'UNIT_PRICE'), issue('NOT_UNDERSTOOD', 'TITLE')]

    expect(problemsToTell(issues, label, odometerRead)).toEqual([])
  })

  it('tells one reason per field, a specific one before "not sure enough"', () => {
    const issues = [issue('NO_CONFIDENCE', 'ODOMETER'), issue('ODOMETER_FAR_ABOVE', 'ODOMETER'), issue('UNSURE', 'TOTAL')]

    expect(problemsToTell(issues, label, nothing)).toEqual([
      { code: 'ODOMETER_FAR_ABOVE', field: undefined },
      { code: 'UNSURE', field: 'Total cost' },
    ])
  })

  it('tells a reason about the photo as a whole only when nothing was filled in', () => {
    const issues = [issue('NOTHING_LEGIBLE'), issue('UNRECOGNISED')]

    expect(problemsToTell(issues, label, nothing)).toEqual([{ code: 'UNRECOGNISED' }]) // not recognised says more than not legible
    expect(problemsToTell(issues, label, odometerRead)).toEqual([])
  })

  it('tells the same sentence once when two fields come to it', () => {
    const twoFields = (name: string) => (name === 'VOLUME' || name === 'UNIT_PRICE' ? 'Volume and price' : undefined)

    expect(problemsToTell([issue('AMOUNTS_DO_NOT_ADD', 'VOLUME'), issue('AMOUNTS_DO_NOT_ADD', 'UNIT_PRICE')], twoFields, nothing)).toEqual([
      { code: 'AMOUNTS_DO_NOT_ADD', field: 'Volume and price' },
    ])
  })

  it('tells a reason about several values once, also where the dialog has a field for each', () => {
    const ownFields: Record<string, string> = { ...labels, VOLUME: 'Volume (L)', UNIT_PRICE: 'Price per L' }
    const issues = [issue('AMOUNTS_DO_NOT_ADD', 'VOLUME'), issue('AMOUNTS_DO_NOT_ADD', 'UNIT_PRICE'), issue('UNSURE', 'TOTAL')]

    expect(problemsToTell(issues, (name) => ownFields[name], nothing)).toEqual([
      { code: 'AMOUNTS_DO_NOT_ADD', field: 'Volume (L)' },
      { code: 'UNSURE', field: 'Total cost' },
    ])
  })
})
