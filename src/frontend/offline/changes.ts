/**
 * Changes kept on this device until the server can take them (the outbox, `outbox.ts`). Pure: what a change is, and how a new one folds
 * into the ones already waiting, so the server only ever gets what is left to do (`collapse`).
 */
export type LogEntity = 'refuelings' | 'expenses'
export type ChangeAction = 'add' | 'update' | 'trash' | 'restore'

export interface Change {
  /** The change's own id; for an add, the id of what it adds (the client chose it, the server keeps it). */
  id: string
  /** The order changes were made in (the server takes them in this order). */
  seq: number
  createdAt: number
  entity: LogEntity
  action: ChangeAction
  vehicleId: string
  /** What it changes: the log's id (an add's own id). */
  targetId: string
  /** What the mutation takes (`LogRefuelingInput`, `UpdateExpenseInput`, ...); none for a trash or a restore. */
  input?: Record<string, unknown>
  /** The log's version this change was made from (`version` on the server): a change made meanwhile is noticed at upload. */
  expectedVersion?: number | null
}

/** How the device marks something with a change waiting. */
export type PendingMark = 'new' | 'changed' | 'deleted' | 'restored'

export const markOf = (change: Change): PendingMark =>
  change.action === 'add' ? 'new' : change.action === 'update' ? 'changed' : change.action === 'trash' ? 'deleted' : 'restored'

/** The fields an update carries over onto an add that is still waiting (the add's own id and vehicle stay). */
const UPDATABLE: Record<LogEntity, readonly string[]> = {
  refuelings: ['date', 'volume', 'totalCost', 'currency', 'odometer', 'isFullTank', 'missedPreviousFillUp', 'note'],
  expenses: ['date', 'title', 'category', 'amount', 'currency', 'odometer', 'note'],
}

/**
 * The changes waiting after `incoming` joins them. Changes of one log fold into one:
 * - an add then an update is the add with the new values; an add then a trash is nothing at all;
 * - an update then an update is the last one, from the first one's version; an update then a trash is the trash, from that version;
 * - a trash then a restore, or a restore then a trash, is nothing (back where the server is);
 * - a second trash or restore of the same log, or an edit of one on its way to the trash, changes nothing.
 * Anything else waits as it is, in order.
 */
export function collapse(existing: readonly Change[], incoming: Change): Change[] {
  const sameLog = (c: Change) => c.entity === incoming.entity && c.targetId === incoming.targetId
  const waiting = existing.filter(sameLog)
  const others = existing.filter((c) => !sameLog(c))
  const add = waiting.find((c) => c.action === 'add')
  const update = waiting.find((c) => c.action === 'update')
  const trash = waiting.find((c) => c.action === 'trash')
  const restore = waiting.find((c) => c.action === 'restore')

  switch (incoming.action) {
    case 'add':
      return [...existing, incoming]
    case 'update':
      if (trash) return [...existing] // on its way to the trash: nothing to edit
      if (add) return replace(existing, add, { ...add, input: { ...add.input, ...pick(incoming.input, UPDATABLE[incoming.entity]) } })
      if (update) return replace(existing, update, { ...incoming, id: update.id, seq: update.seq, createdAt: update.createdAt, expectedVersion: update.expectedVersion })
      return [...existing, incoming]
    case 'trash':
      if (trash) return [...existing] // already waiting to go
      if (add) return others // never sent: nothing to take back
      if (restore) return existing.filter((c) => c !== restore)
      if (update) return replace(existing, update, { ...incoming, id: update.id, seq: update.seq, createdAt: update.createdAt, expectedVersion: update.expectedVersion })
      return [...existing, incoming]
    case 'restore':
      if (trash) return existing.filter((c) => c !== trash)
      if (restore) return [...existing] // already waiting to come back
      return [...existing, incoming]
  }
}

/** Takes back one waiting change (Keep, Remove on the Sync page). */
export const discard = (existing: readonly Change[], id: string): Change[] => existing.filter((c) => c.id !== id)

const replace = (list: readonly Change[], old: Change, next: Change) => list.map((c) => (c === old ? next : c))

function pick(input: Record<string, unknown> | undefined, keys: readonly string[]) {
  return Object.fromEntries(keys.filter((k) => input && k in input).map((k) => [k, input![k]]))
}
