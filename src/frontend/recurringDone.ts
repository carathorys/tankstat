import type { RecurrenceKind, RecurrenceState } from './gql/generated.ts'

/** The longest expense title the server takes (`Expense.MaxTitleLength`). */
export const MAX_EXPENSE_TITLE = 120

/** A schedule as the "Mark as done" dialog needs it. */
export interface DoneItem {
  id: string
  title: string
  kind: RecurrenceKind
  category?: string | null
  status: { state: RecurrenceState }
}

/** The ticked schedules' titles joined, cut to fit an expense title (the server's `RecurringDoneDefaults.Title`). */
export function defaultTitle(items: readonly { title: string }[]): string {
  const joined = items.map((i) => i.title).join(', ')
  return joined.length <= MAX_EXPENSE_TITLE ? joined : `${joined.slice(0, MAX_EXPENSE_TITLE - 1).replace(/[ ,]+$/, '')}…`
}

/** Their common category, or the first one given; empty when none has one (the server's `RecurringDoneDefaults.Category`). */
export function defaultCategory(items: readonly { category?: string | null }[]): string {
  return items.map((i) => i.category?.trim() ?? '').find((c) => c !== '') ?? ''
}

/** Whether the odometer is needed: distance counts for at least one of them. */
export const usesDistance = (items: readonly { kind: RecurrenceKind }[]) => items.some((i) => i.kind !== 'TIME')

/** What a dialog opened from one schedule starts with ticked: that one, and everything else that is due soon or overdue (one visit). */
export function preselect(items: readonly DoneItem[], openedFrom: string): string[] {
  return [openedFrom, ...items.filter((i) => i.id !== openedFrom && i.status.state !== 'UPCOMING').map((i) => i.id)]
}
