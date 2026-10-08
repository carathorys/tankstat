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
  /**
   * What the mutation takes (`LogRefuelingInput`, `UpdateExpenseInput`, ...); none for a restore. A trash carries the values of the edit it
   * took the place of (never sent with it), so taking the trash back (a restore, Keep, Remove) brings the edit back.
   */
  input?: Record<string, unknown>
  /** The log's version this change was made from (`version` on the server): a change made meanwhile is noticed at upload. */
  expectedVersion?: number | null
  /**
   * Sent to the server at least once, so it may have been applied with the answer lost: nothing folds into it any more (it would be answered
   * as already done, and what folded in lost); a later change of the same log follows it as a change of its own.
   */
  sent?: boolean
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
 * - an update then an update is the last one, from the first one's version; an update then a trash is the trash, from that version,
 *   keeping the edit's values so that taking the trash back brings the edit back;
 * - a trash then a restore, or a restore then a trash, is nothing (back where the server is, or the edit the trash took the place of);
 * - a second trash or restore of the same log, or an edit of one on its way to the trash, changes nothing.
 * A change that was sent already (`sent`) is never folded into: what follows it waits as a change of its own, from the version the log has
 * once it is applied. Anything else waits as it is, in order.
 */
export function collapse(existing: readonly Change[], incoming: Change): Change[] {
  const sameLog = (c: Change) => c.entity === incoming.entity && c.targetId === incoming.targetId
  const waiting = existing.filter((c) => sameLog(c) && !c.sent)
  const sentBefore = existing.filter((c) => sameLog(c) && c.sent).at(-1)
  const add = waiting.find((c) => c.action === 'add')
  const update = waiting.find((c) => c.action === 'update')
  const trash = waiting.find((c) => c.action === 'trash')
  const restore = waiting.find((c) => c.action === 'restore')
  const next = sentBefore ? { ...incoming, expectedVersion: versionAfter(sentBefore) } : incoming

  switch (incoming.action) {
    case 'add':
      return [...existing, incoming]
    case 'update':
      if (trash) return [...existing] // on its way to the trash: nothing to edit
      if (add) return replace(existing, add, { ...add, input: { ...add.input, ...pick(incoming.input, UPDATABLE[incoming.entity]) } })
      if (update) return replace(existing, update, { ...incoming, id: update.id, seq: update.seq, createdAt: update.createdAt, expectedVersion: update.expectedVersion })
      return [...existing, next]
    case 'trash':
      if (trash) return [...existing] // already waiting to go
      if (add) return existing.filter((c) => !waiting.includes(c)) // never sent: nothing to take back
      if (restore) return existing.filter((c) => c !== restore)
      if (update) return replace(existing, update, { ...incoming, id: update.id, seq: update.seq, createdAt: update.createdAt, expectedVersion: update.expectedVersion, input: update.input })
      return [...existing, next]
    case 'restore':
      if (trash) return trash.input ? replace(existing, trash, asEdit(trash)) : existing.filter((c) => c !== trash)
      if (restore) return [...existing] // already waiting to come back
      return [...existing, next]
  }
}

/** The version a log has once a change sent before is applied: an add's is the server's to say (no check), any other one is one more. */
const versionAfter = (sent: Change) => (sent.action === 'add' || sent.expectedVersion == null ? null : sent.expectedVersion + 1)

/** A trash that took the place of an edit, taken back: the edit again. */
const asEdit = (trash: Change): Change => ({ ...trash, action: 'update' })

/** Takes back one waiting change (Keep, Remove on the Sync page); a trash that took the place of an edit leaves the edit. */
export function discard(existing: readonly Change[], id: string): Change[] {
  const change = existing.find((c) => c.id === id)
  if (change?.action === 'trash' && change.input) return replace(existing, change, asEdit(change))
  return existing.filter((c) => c.id !== id)
}

const replace = (list: readonly Change[], old: Change, next: Change) => list.map((c) => (c === old ? next : c))

function pick(input: Record<string, unknown> | undefined, keys: readonly string[]) {
  return Object.fromEntries(keys.filter((k) => input && k in input).map((k) => [k, input![k]]))
}
