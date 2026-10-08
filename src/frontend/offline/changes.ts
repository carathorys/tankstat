/**
 * Changes kept on this device until the server can take them (the outbox, `outbox.ts`). Pure: what a change is, and how a new one folds
 * into the ones already waiting, so the server only ever gets what is left to do (`collapse`).
 */
export type LogEntity = 'refuelings' | 'expenses'
/** What a change can concern: the logs, a vehicle, a recurring expense (schedule). */
export type ChangeEntity = LogEntity | 'vehicles' | 'recurring'
/**
 * `trash` of a schedule deletes it for good (schedules have no trash); `markDone` is a service visit of one or more schedules; `addPhoto`
 * and `removePhoto` change the photos of a saved log (input `{ key }`, a photo kept on this device, or `{ imageId }`).
 */
export type ChangeAction = 'add' | 'update' | 'trash' | 'restore' | 'markDone' | 'addPhoto' | 'removePhoto'

export interface Change {
  /** The change's own id; for an add, the id of what it adds (the client chose it, the server keeps it). */
  id: string
  /** The order changes were made in (the server takes them in this order). */
  seq: number
  createdAt: number
  entity: ChangeEntity
  action: ChangeAction
  vehicleId: string
  /** What it changes: the entity's id (an add's own id); for a visit, the id of the expense it logs (or its own id). */
  targetId: string
  /** A visit's schedules. */
  targetIds?: string[]
  /** What the mutation takes (`LogRefuelingInput`, `UpdateExpenseInput`, ...); none for a trash or a restore. */
  input?: Record<string, unknown>
  /** The log's version this change was made from (`version` on the server): a change made meanwhile is noticed at upload. */
  expectedVersion?: number | null
}

/** How the device marks something with a change waiting. */
export type PendingMark = 'new' | 'changed' | 'deleted' | 'restored' | 'done'

const MARKS: Record<ChangeAction, PendingMark> = {
  add: 'new', update: 'changed', trash: 'deleted', restore: 'restored', markDone: 'done', addPhoto: 'changed', removePhoto: 'changed',
}
export const markOf = (change: Change): PendingMark => MARKS[change.action]

/** The prefix of a photo kept on this device (`keptPhotos.ts`) where a draft's id would be. */
export const KEPT = 'local:'
export const isKept = (id: unknown): id is string => typeof id === 'string' && id.startsWith(KEPT)

/** The photos kept on this device that a change carries: those picked for a new log or a visit, or one added to a saved log. */
export function keptPhotosOf(change: Change): string[] {
  const ids = Array.isArray(change.input?.photoIds) ? (change.input.photoIds as unknown[]) : []
  return [...ids, change.action === 'addPhoto' ? change.input?.key : undefined].filter(isKept)
}

/**
 * The changes that can be edited before they are sent or applied (Waiting to sync): adds and edits of logs, vehicles and schedules. A
 * trash, a restore, a visit or a photo is only kept, removed, applied or discarded.
 */
export const canEdit = (change: Change) => change.action === 'add' || change.action === 'update'

const isPhoto = (c: Change) => c.action === 'addPhoto' || c.action === 'removePhoto'

/** The fields an update carries over onto an add that is still waiting (the add's own id and vehicle stay). */
const UPDATABLE: Record<ChangeEntity, readonly string[]> = {
  refuelings: ['date', 'volume', 'totalCost', 'currency', 'odometer', 'isFullTank', 'missedPreviousFillUp', 'note'],
  expenses: ['date', 'title', 'category', 'amount', 'currency', 'odometer', 'note'],
  vehicles: ['name', 'licensePlate', 'fuelType', 'units'],
  recurring: ['title', 'category', 'note', 'kind', 'intervalMonths', 'intervalDistance', 'lastDoneDate', 'lastDoneOdometer', 'warnDays', 'warnDistance'],
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
  // A visit never folds: each one is a visit of its own.
  if (incoming.action === 'markDone') return [...existing, incoming]
  const sameLog = (c: Change) => c.action !== 'markDone' && !isPhoto(c) && c.entity === incoming.entity && c.targetId === incoming.targetId
  const photosOfLog = (c: Change) => isPhoto(c) && c.entity === incoming.entity && c.targetId === incoming.targetId
  const waiting = existing.filter(sameLog)
  const others = existing.filter((c) => !sameLog(c))
  const add = waiting.find((c) => c.action === 'add')
  const update = waiting.find((c) => c.action === 'update')
  const trash = waiting.find((c) => c.action === 'trash')
  const restore = waiting.find((c) => c.action === 'restore')

  switch (incoming.action) {
    case 'addPhoto': {
      if (trash) return [...existing] // on its way to the trash: a photo could not be added to it
      const key = incoming.input?.key
      if (add) return replace(existing, add, { ...add, input: { ...add.input, photoIds: [...((add.input?.photoIds as string[] | undefined) ?? []), key] } })
      return [...existing, incoming]
    }
    case 'removePhoto': {
      const imageId = incoming.input?.imageId
      if (isKept(imageId)) {
        // A photo kept here never reached the server: taking it back is enough.
        if (add) return replace(existing, add, { ...add, input: { ...add.input, photoIds: ((add.input?.photoIds as string[] | undefined) ?? []).filter((id) => id !== imageId) } })
        return existing.filter((c) => !(photosOfLog(c) && c.action === 'addPhoto' && c.input?.key === imageId))
      }
      if (trash || existing.some((c) => photosOfLog(c) && c.action === 'removePhoto' && c.input?.imageId === imageId)) return [...existing]
      return [...existing, incoming]
    }
    case 'add':
      return [...existing, incoming]
    case 'update':
      if (trash) return [...existing] // on its way to the trash: nothing to edit
      if (add) return replace(existing, add, { ...add, input: { ...add.input, ...pick(incoming.input, UPDATABLE[incoming.entity]) } })
      if (update) return replace(existing, update, { ...incoming, id: update.id, seq: update.seq, createdAt: update.createdAt, expectedVersion: update.expectedVersion })
      return [...existing, incoming]
    case 'trash': {
      if (trash) return [...existing] // already waiting to go
      if (add) return withoutDependents(others, incoming).filter((c) => !photosOfLog(c)) // never sent: nothing to take back, nor anything made to it
      // Photos of a log in the trash can be neither added nor removed: those changes go (a restore does not bring them back).
      const kept = existing.filter((c) => !photosOfLog(c))
      if (restore) return kept.filter((c) => c !== restore)
      if (update) return replace(kept, update, { ...incoming, id: update.id, seq: update.seq, createdAt: update.createdAt, expectedVersion: update.expectedVersion })
      return [...kept, incoming]
    }
    case 'restore':
      if (trash) return existing.filter((c) => c !== trash)
      if (restore) return [...existing] // already waiting to come back
      return [...existing, incoming]
  }
}

/**
 * What else goes with an add that is taken back: a vehicle's logs, schedules and visits (they were made to a vehicle that never reached the
 * server), or a schedule's place in the visits waiting (a visit left with none is dropped).
 */
function withoutDependents(list: readonly Change[], removed: Change): Change[] {
  if (removed.entity === 'vehicles') return list.filter((c) => c.vehicleId !== removed.targetId)
  if (removed.entity !== 'recurring') return [...list]
  return list.flatMap((c) => {
    if (c.action !== 'markDone' || !c.targetIds?.includes(removed.targetId)) return [c]
    const left = c.targetIds.filter((id) => id !== removed.targetId)
    return left.length > 0 ? [{ ...c, targetIds: left, input: { ...c.input, ids: left } }] : []
  })
}

/** Takes back one waiting change (Keep, Remove on the Sync page); an add taken back takes what was made to it along, as a trash would. */
export function discard(existing: readonly Change[], id: string): Change[] {
  const removed = existing.find((c) => c.id === id)
  const rest = existing.filter((c) => c.id !== id)
  return removed?.action === 'add' ? withoutDependents(rest, removed) : rest
}

const replace = (list: readonly Change[], old: Change, next: Change) => list.map((c) => (c === old ? next : c))

function pick(input: Record<string, unknown> | undefined, keys: readonly string[]) {
  return Object.fromEntries(keys.filter((k) => input && k in input).map((k) => [k, input![k]]))
}
