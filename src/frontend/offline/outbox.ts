import { useSyncExternalStore } from 'react'
import { collapse, discard, markOf, type Change, type LogEntity, type PendingMark } from './changes.ts'
import { deviceData } from './deviceData.ts'
import type { RowStore } from './deviceStorage.ts'

/**
 * The changes this device keeps until the server can take them, in the open user's database (`deviceStorage.ts`), so they survive a
 * restart and another account signing in never sees them. A new change folds into the waiting ones (`collapse`). Changes are made by the
 * user on screen, so they are kept in whichever user's data is open, before the server confirmed who it is too (an offline start).
 * Nothing sends them yet.
 */
let changes: Change[] = []
let rows: RowStore | null = null
let nextSeq = 1
let version = 0
let loading: Promise<void> = Promise.resolve()
const listeners = new Set<() => void>()

function notify() {
  version++
  listeners.forEach((listener) => listener())
}

async function load() {
  rows = await deviceData.rows()
  changes = rows ? await rows.changes().catch(() => []) : []
  nextSeq = changes.reduce((max, c) => Math.max(max, c.seq), 0) + 1
  notify()
}

deviceData.onStoreChange(() => {
  loading = load()
})
loading = load()

/** Writes what differs between two lists of changes. */
async function persist(before: readonly Change[], after: readonly Change[]) {
  if (!rows) return
  const kept = new Set(after.map((c) => c.id))
  const old = new Map(before.map((c) => [c.id, c]))
  await rows.deleteChanges(before.filter((c) => !kept.has(c.id)).map((c) => c.id))
  await rows.putChanges(after.filter((c) => old.get(c.id) !== c))
}

export type ChangeDraft = Omit<Change, 'seq' | 'createdAt'>

export const outbox = {
  /** The changes waiting, oldest first. */
  get changes(): readonly Change[] {
    return changes
  },

  /** Keeps a change on the device; it folds into the ones waiting for the same log. */
  async enqueue(draft: ChangeDraft): Promise<void> {
    await loading
    if (!rows) throw new Error('No user data is open on this device.')
    const next = collapse(changes, { ...draft, seq: nextSeq++, createdAt: Date.now() })
    await persist(changes, next)
    changes = next
    notify()
  },

  /** Takes back one waiting change. */
  async discard(id: string): Promise<void> {
    await loading
    const next = discard(changes, id)
    await persist(changes, next)
    changes = next
    notify()
  },

  /** How a log is marked while a change of it waits; null when none does. */
  markOf(entity: LogEntity, id: string): PendingMark | null {
    const change = changes.find((c) => c.entity === entity && c.targetId === id)
    return change ? markOf(change) : null
  },

  /** The vehicles with a change waiting: the device answers their logs itself, so what was changed shows. */
  vehicleIds(): Set<string> {
    return new Set(changes.map((c) => c.vehicleId))
  },

  count(vehicleId?: string): number {
    return vehicleId ? changes.filter((c) => c.vehicleId === vehicleId).length : changes.length
  },

  subscribe(listener: () => void): () => void {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },

  /** Read again from the open user's data (tests: after a reset or a restart). */
  reload(): Promise<void> {
    loading = load()
    return loading
  },
}

const useVersion = () => useSyncExternalStore(outbox.subscribe, () => version)

/** How a log is marked while a change of it waits (new, changed, to be removed, restored), re-rendering when it changes. */
export function usePendingMark(entity: LogEntity, id: string): PendingMark | null {
  useVersion()
  return outbox.markOf(entity, id)
}

/** How many changes wait (for a vehicle, or in all). */
export function usePendingCount(vehicleId?: string): number {
  useVersion()
  return outbox.count(vehicleId)
}
