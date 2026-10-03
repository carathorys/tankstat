import type { ReadingFieldName } from '../gql/generated.ts'
import { parseDecimal } from '../i18n/format.ts'

export interface ReadValue {
  value: string
  confidence: number
}

/** What the photos of the open dialog showed: the surest value of each name. */
export type ReadValues = Partial<Record<ReadingFieldName, ReadValue>>

interface ReadingLike {
  status: string
  values: readonly { name: ReadingFieldName; value: string; confidence: number }[]
}

/** The values of every finished reading, keeping the surest one when several photos show the same thing. */
export function mergeReadings(readings: Iterable<ReadingLike | null | undefined>): ReadValues {
  const merged: ReadValues = {}
  for (const reading of readings) {
    if (reading?.status !== 'READ') continue
    for (const v of reading.values) {
      const known = merged[v.name]
      if (!known || known.confidence < v.confidence) merged[v.name] = { value: v.value, confidence: v.confidence }
    }
  }
  return merged
}

/** Whether what is typed already says what the photo shows ("38,52" and "38.52" do, so do "huf" and "HUF"). */
export function sameValue(typed: string, read: string): boolean {
  const a = typed.trim()
  const b = read.trim()
  if (a.toLowerCase() === b.toLowerCase()) return true
  const x = parseDecimal(a)
  const y = parseDecimal(b)
  return x !== undefined && y !== undefined && x === y
}

export interface FillState<F extends string> {
  values: Record<F, string>
  /** Fields the user typed in: a photo never overwrites them, it only offers its value. */
  touched: ReadonlySet<F>
  /** Fields that hold what a photo showed (until the user changes them). */
  filled: Partial<Record<F, string>>
  /** What a photo showed where it differs from what the user typed: offered with "Use". */
  offered: Partial<Record<F, string>>
}

/**
 * Puts read values into the fields the user has not changed (their starting values, such as today's date or the last currency, count as
 * unchanged); for fields the user typed in, a different value is only offered. Returns the new state and the fields filled just now.
 */
export function applyRead<F extends string>(
  state: FillState<F>,
  fields: Partial<Record<F, ReadingFieldName>>,
  read: ReadValues,
): { state: FillState<F>; newly: F[] } {
  const values = { ...state.values }
  const filled = { ...state.filled }
  const offered = { ...state.offered }
  const newly: F[] = []
  for (const field of Object.keys(fields) as F[]) {
    const value = read[fields[field]!]?.value
    if (value === undefined) continue
    if (state.touched.has(field)) {
      if (sameValue(values[field], value)) delete offered[field]
      else offered[field] = value
      continue
    }
    if (filled[field] === value && values[field] === value) continue
    values[field] = value
    filled[field] = value
    delete offered[field]
    newly.push(field)
  }
  return { state: { ...state, values, filled, offered }, newly }
}

/** The user typed in a field: it is theirs now, and the photo's mark goes once it no longer says what the photo showed. */
export function changeField<F extends string>(state: FillState<F>, field: F, value: string): FillState<F> {
  const filled = { ...state.filled }
  const offered = { ...state.offered }
  if (filled[field] !== value) delete filled[field]
  if (offered[field] !== undefined && sameValue(value, offered[field])) delete offered[field]
  return { values: { ...state.values, [field]: value }, touched: new Set(state.touched).add(field), filled, offered }
}

/** "Use": the field takes the value the photo showed. */
export function takeOffered<F extends string>(state: FillState<F>, field: F): FillState<F> {
  const value = state.offered[field]
  if (value === undefined) return state
  const offered = { ...state.offered }
  delete offered[field]
  return { ...state, values: { ...state.values, [field]: value }, filled: { ...state.filled, [field]: value }, offered }
}
