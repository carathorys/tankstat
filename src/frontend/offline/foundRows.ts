/**
 * What this device held when the server's history changed (a restore took the data back to a backup: `Session.instance.generation`): the
 * downloaded vehicles, logs and schedules this account may make again (its own vehicles with their schedules, the entries it logged) are
 * set aside before the device starts afresh. The downloads that follow judge them where they cover them (the vehicle downloaded in full,
 * a log inside its window): the ones the server no longer has, or has at an older version, are listed on Waiting to sync as *Found on this
 * device*; the others go; one no download covers yet is not listed (nothing can be said of it). Nothing is sent by itself: the person sends
 * a row to the server (an add with the row's own id and values, or an edit against the server's version) or forgets it. Pure where it can
 * be (`compare`); the store is the user's `found` table, which the wipe spares.
 */
import type { ApolloClient } from '@apollo/client'
import { type ChangeEntity } from './changes.ts'
import { valuesOf } from './conflicts.ts'
import { deviceData } from './deviceData.ts'
import type { LogRow, PullCursor, RowStore, VehicleRow } from './deviceStorage.ts'
import { keptBy } from './offlineLink.ts'
import { outbox } from './outbox.ts'
import { keepChange } from './submitChange.ts'
import { uuidV4 } from './uuid.ts'

export interface FoundRow {
  /** The row's own id (the vehicle's, the log's, the schedule's). */
  id: string
  entity: ChangeEntity
  vehicleId: string
  /** The generation the row belonged to. */
  generation: string
  /** The version this device held. */
  version: number
  /** The version the server holds now, when it has the row at an older one (then it is sent as an edit); none when the server lacks it. */
  serverVersion?: number
  /** A download covered it, so what the list says of it is known: only such a row is listed. */
  checked?: boolean
  /** The row's values in the shape of the entity's input (what an add or an edit sends). */
  values: Record<string, unknown>
  /** What the list shows: a log's date, a vehicle's or schedule's name. */
  date?: string
  title: string
  setAsideAt: number
}

/** What the server has now, by id, for the compare: the version it holds, or nothing. */
export type Held = (entity: ChangeEntity, id: string) => number | undefined

type Kept = (operationName: string, variables: Record<string, unknown>) => Promise<Record<string, unknown> | undefined>

const titleOf = (entity: ChangeEntity, row: Record<string, unknown>): string => {
  if (entity === 'vehicles') return String(row.name ?? '')
  if (entity === 'recurring') return String(row.title ?? '')
  if (entity === 'expenses') return String(row.title ?? '')
  return typeof row.note === 'string' && row.note ? row.note : ''
}

function found(entity: ChangeEntity, vehicleId: string, row: Record<string, unknown>, generation: string, at: number): FoundRow | null {
  const id = typeof row.id === 'string' ? row.id : null
  if (!id || row.deletedAt) return null // what was in the trash is not worth bringing back
  return {
    id, entity, vehicleId, generation,
    version: typeof row.version === 'number' ? row.version : 0,
    values: valuesOf(entity, row),
    date: typeof row.date === 'string' ? row.date : undefined,
    title: titleOf(entity, row),
    setAsideAt: at,
  }
}

/**
 * Whether this account made it, so may make it again: a vehicle it owns, an entry it logged (one that names nobody counts: sign-in is off,
 * or the account is gone). Sent by another account, it would be made theirs, and the one that made it could not send it any more.
 */
const mine = (by: unknown, me: string | null) => {
  const id = (by as { id?: unknown } | null | undefined)?.id
  return typeof id !== 'string' || id === me
}

/** The schedules of a vehicle as this device kept them (the `RecurringExpenses` answer); undefined when none is kept. */
async function schedulesOf(kept: Kept, vehicleId: string): Promise<Record<string, unknown>[] | undefined> {
  const recurring = ((await kept('RecurringExpenses', { vehicleId }))?.vehicle as { recurring?: unknown } | undefined)?.recurring
  return Array.isArray(recurring) ? (recurring as Record<string, unknown>[]) : undefined
}

/** The logs as stored, by vehicle: never a tab's copies in memory, which another tab's download or wipe may have left behind. */
async function logsByVehicle(rows: RowStore, kind: 'refuelings' | 'expenses'): Promise<Map<string, LogRow[]>> {
  const byVehicle = new Map<string, LogRow[]>()
  for (const log of await rows.allLogs(kind)) {
    const list = byVehicle.get(log.vehicleId)
    if (list) list.push(log)
    else byVehicle.set(log.vehicleId, [log])
  }
  return byVehicle
}

/** Whether a log of this date is inside the window the vehicle's logs are downloaded from: only there does a download say anything of it. */
const inWindow = (cursor: PullCursor, date: string | undefined) => cursor.from === null || (cursor.from !== false && date !== undefined && date >= cursor.from)

/**
 * Which rows set aside the server lacks, or holds at an older version than this device did (those stay, judged), and which it has again
 * (those go). A row the downloads do not cover (`covered`) stays as it is: nothing can be said about it yet.
 */
export function compare(rows: readonly FoundRow[], held: Held, covered: (row: FoundRow) => boolean): { keep: FoundRow[]; drop: string[] } {
  const keep: FoundRow[] = []
  const drop: string[] = []
  for (const row of rows) {
    if (!covered(row)) {
      keep.push(row)
      continue
    }
    const server = held(row.entity, row.id)
    if (server !== undefined && server >= row.version) drop.push(row.id)
    else keep.push({ ...row, serverVersion: server, checked: true })
  }
  return { keep, drop }
}

/** Oldest first within a vehicle, the vehicle itself before its rows, so neighbours exist when the server takes them. */
export function inSendOrder(rows: readonly FoundRow[]): FoundRow[] {
  const rank = (row: FoundRow) => (row.entity === 'vehicles' ? 0 : row.entity === 'recurring' ? 1 : 2)
  return [...rows].sort((a, b) => a.vehicleId.localeCompare(b.vehicleId) || rank(a) - rank(b) || (a.date ?? '').localeCompare(b.date ?? '') || a.id.localeCompare(b.id))
}

export const foundRows = {
  /** Before the device starts afresh: what it holds of the old history, added to what may already be set aside (never cleared: two tabs may do this at once). */
  async setAside(rows: RowStore, kept: Kept, generation: string, now = Date.now()): Promise<number> {
    const me = deviceData.user
    const list: FoundRow[] = []
    const add = (row: FoundRow | null) => {
      if (row) list.push(row)
    }
    const logs = { refuelings: await logsByVehicle(rows, 'refuelings'), expenses: await logsByVehicle(rows, 'expenses') }
    for (const vehicle of await rows.vehicles()) {
      const owned = mine(vehicle.owner, me)
      if (owned) add(found('vehicles', vehicle.id, vehicle as VehicleRow, generation, now))
      for (const kind of ['refuelings', 'expenses'] as const)
        for (const log of logs[kind].get(vehicle.id) ?? []) if (mine(log.createdBy, me)) add(found(kind, vehicle.id, log, generation, now))
      if (owned) for (const schedule of (await schedulesOf(kept, vehicle.id)) ?? []) add(found('recurring', vehicle.id, schedule, generation, now))
    }
    await rows.putFound(list)
    return list.length
  },

  /** After a download: the rows it covers are judged, and what the server has again leaves the list. */
  async reconcile(rows: RowStore, kept: Kept): Promise<void> {
    const all = await rows.found()
    if (all.length === 0) return
    const vehicles = new Map((await rows.vehicles()).map((v) => [v.id, v]))
    const cursors = new Map((await rows.cursors()).map((c) => [c.vehicleId, c]))
    const logs = new Map<string, LogRow>()
    for (const kind of ['refuelings', 'expenses'] as const) for (const log of await rows.allLogs(kind)) logs.set(log.id, log)
    const schedules = new Map<string, Record<string, unknown>>()
    const schedulesKnown = new Set<string>()
    for (const vehicleId of new Set(all.filter((r) => r.entity === 'recurring').map((r) => r.vehicleId))) {
      const list = vehicles.has(vehicleId) ? await schedulesOf(kept, vehicleId) : undefined
      if (!list) continue
      schedulesKnown.add(vehicleId)
      for (const schedule of list) if (typeof schedule.id === 'string') schedules.set(schedule.id, schedule)
    }
    const version = (row: Record<string, unknown> | undefined) => (typeof row?.version === 'number' ? row.version : row ? 0 : undefined)
    const held: Held = (entity, id) => (entity === 'vehicles' ? version(vehicles.get(id)) : entity === 'recurring' ? version(schedules.get(id)) : version(logs.get(id)))
    const covered = (row: FoundRow) => {
      if (!vehicles.has(row.vehicleId)) return true // the server no longer lists the vehicle: gone, with everything below it
      const cursor = cursors.get(row.vehicleId)
      if (cursor?.complete !== true) return false // not downloaded in full yet
      if (row.entity === 'vehicles') return true
      if (row.entity === 'recurring') return schedulesKnown.has(row.vehicleId)
      return inWindow(cursor, row.date)
    }
    const { keep, drop } = compare(all, held, covered)
    const before = new Map(all.map((r) => [r.id, r]))
    const changed = keep.filter((row) => before.get(row.id)?.serverVersion !== row.serverVersion || before.get(row.id)?.checked !== row.checked)
    if (drop.length > 0) await rows.deleteFound(drop)
    if (changed.length > 0) await rows.putFound(changed)
    if (drop.length > 0 || changed.length > 0) deviceData.foundChanged()
  },

  async list(): Promise<FoundRow[]> {
    return (await deviceData.rows())?.found() ?? []
  },

  async forget(ids: string[]): Promise<void> {
    if (ids.length === 0) return
    await (await deviceData.rows())?.deleteFound(ids)
    deviceData.foundChanged()
  },

  /**
   * Queues rows for the server as changes waiting on this device: an add with the row's own id and values (the server creates it under
   * that id, or answers from its ledger when it knows the id), or, when the server holds an older version, an edit against that version.
   * The push sends them like any other change; what the server refuses parks, as always. A row with a change of its own waiting here (made
   * since, so newer) stays listed: sent now, it would fold into that change and take its place. Returns how many were queued.
   */
  async send(client: ApolloClient, rows: readonly FoundRow[]): Promise<number> {
    const waiting = new Set(outbox.changes.flatMap((c) => [c.targetId, ...(c.targetIds ?? [])]))
    const ready = inSendOrder(rows).filter((row) => !waiting.has(row.id))
    for (const row of ready) {
      await keepChange(client, row.serverVersion === undefined
        ? { id: row.id, entity: row.entity, action: 'add', vehicleId: row.vehicleId, targetId: row.id, input: row.values }
        : { id: uuidV4(), entity: row.entity, action: 'update', vehicleId: row.vehicleId, targetId: row.id, input: row.values, expectedVersion: row.serverVersion }, { refetch: false })
    }
    await foundRows.forget(ready.map((r) => r.id))
    if (ready.length > 0) void client.refetchQueries({ include: 'active' }).catch(() => undefined) // once, not for every row
    return ready.length
  },
}

/** The kept answers of the open user's data, for the schedules. */
export const keptAnswers: Kept = keptBy(deviceData)
