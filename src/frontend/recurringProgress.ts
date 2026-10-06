import type { RecurrenceKind, RecurrenceLimit } from './gql/generated.ts'

/** What the gauges need of a schedule. */
export interface ScheduleProgressSource {
  kind: RecurrenceKind
  intervalDistance: number | null
  lastDoneDate: string
  status: { limit: RecurrenceLimit | null; dueDate: string | null; daysLeft: number | null; distanceLeft: number | null }
}

/** How far a schedule is through one of its limits. */
export interface LimitProgress {
  limit: RecurrenceLimit
  /** The share of the interval that has passed: 0 just done, 1 due now, more once it is overdue. */
  used: number
  /** The schedule's state comes from this limit (always, for a schedule with one; the nearer one of a combined schedule). */
  deciding: boolean
}

const DAY = 86_400_000

/**
 * Where a schedule stands on each of its limits (time, distance, or both for a combined one), worked out from what the server says is left.
 * A limit it cannot tell (no odometer reading yet) is left out.
 */
export function recurringProgress(s: ScheduleProgressSource): LimitProgress[] {
  const deciding = (limit: RecurrenceLimit) => s.kind !== 'COMBINED' || s.status.limit === null || s.status.limit === limit
  const progress: LimitProgress[] = []
  if (s.kind !== 'ODOMETER' && s.status.dueDate && s.status.daysLeft != null) {
    const interval = Math.round((Date.parse(s.status.dueDate) - Date.parse(s.lastDoneDate)) / DAY)
    if (interval > 0) progress.push({ limit: 'TIME', used: (interval - s.status.daysLeft) / interval, deciding: deciding('TIME') })
  }
  if (s.kind !== 'TIME' && s.intervalDistance && s.status.distanceLeft != null) {
    progress.push({ limit: 'ODOMETER', used: (s.intervalDistance - s.status.distanceLeft) / s.intervalDistance, deciding: deciding('ODOMETER') })
  }
  return progress
}
