import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { ReadingFieldName } from '../gql/generated.ts'
import { problemsToTell, type ReadingExplanation } from './readingIssues.ts'
import { applyRead, changeField, takeOffered, type FillState, type ReadValues } from './readValues.ts'

export interface ReadFill<F extends string> {
  values: Record<F, string>
  /** The field's value came from a photo (and still says what the photo showed). */
  isFilled: (field: F) => boolean
  /** What a photo showed where it differs from what the user typed. */
  offered: (field: F) => string | undefined
  change: (field: F, value: string) => void
  /** Takes the offered value into the field. */
  use: (field: F) => void
  /** For the live region: what was just filled in, or that nothing could be filled in. */
  announcement: string
  /** Why the photos gave less than they might have, as sentences for the live region. */
  problems: string[]
}

/** The reasons the photos gave less than they might have, and what a sentence about the odometer can say of the vehicle's latest reading (already formatted). */
export type ReadExplanation = ReadingExplanation & { last?: string }

/**
 * The fields of a form that photos can fill in: controlled values that take what the photos showed as it arrives, without ever
 * overwriting what the user typed (they get "The photo shows … · Use" instead).
 *
 * @param fields  which value of a reading goes into which field
 * @param labels  the fields' labels, for the announcement
 * @param done    every reading the dialog waited for has finished (if none showed anything, that is announced)
 * @param explain why the photos gave less than they might have: told next to what was filled in, or instead of it
 */
export function useReadFill<F extends string>(
  initial: Record<F, string>,
  fields: Partial<Record<F, ReadingFieldName>>,
  read: ReadValues,
  labels: Record<F, string>,
  done: boolean,
  explain: ReadExplanation = { issues: [], failed: false },
): ReadFill<F> {
  const { t } = useTranslation()
  const [state, setState] = useState<FillState<F>>(() => ({ values: initial, touched: new Set<F>(), filled: {}, offered: {} }))
  const [applied, setApplied] = useState('{}')
  const [announced, setAnnounced] = useState('')

  // New readings are applied while rendering (React's way of adjusting state to changed input), so the fields never show stale values.
  const key = JSON.stringify(read)
  if (key !== applied) {
    setApplied(key)
    const { state: next, newly } = applyRead(state, fields, read)
    setState(next)
    if (newly.length > 0) setAnnounced(t('reading.announce', { fields: newly.map((f) => labels[f]).join(', ') }))
  }

  const labelOf = (name: ReadingFieldName) => {
    const field = (Object.keys(fields) as F[]).find((f) => fields[f] === name)
    return field === undefined ? undefined : labels[field]
  }
  const nothingFilled = key === '{}'
  const problems = [
    ...problemsToTell(explain.issues, labelOf, read).map((p) =>
      t(`reading.issues.${p.code}`, { field: p.field ?? '', last: explain.last ? ` (${explain.last})` : '' }),
    ),
    ...(explain.failed && nothingFilled ? [t('reading.failed')] : []),
  ]

  return {
    values: state.values,
    isFilled: (field) => state.filled[field] !== undefined,
    offered: (field) => state.offered[field],
    change: (field, value) => setState((s) => changeField(s, field, value)),
    use: (field) => setState((s) => takeOffered(s, field)),
    announcement: announced || (done && nothingFilled ? t(problems.length > 0 ? 'reading.nothingFilled' : 'reading.nothing') : ''),
    problems,
  }
}
