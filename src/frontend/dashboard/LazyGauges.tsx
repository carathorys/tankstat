import { lazy, Suspense } from 'react'
import type { RecurrenceState } from '../gql/generated.ts'
import type { LimitProgress } from '../recurringProgress.ts'

const ProgressGauges = lazy(() => import('./ProgressGauges.tsx').then((m) => ({ default: m.ProgressGauges })))

/** The chart library is large: the gauges download it only when a schedule is shown (their space is kept meanwhile). */
export function LazyGauges({ progress, state }: { progress: readonly LimitProgress[]; state: RecurrenceState }) {
  if (progress.length === 0) return null
  return (
    <Suspense fallback={<div style={{ width: progress.length * 44, height: 40 }} aria-hidden />}>
      <ProgressGauges progress={progress} state={state} />
    </Suspense>
  )
}
