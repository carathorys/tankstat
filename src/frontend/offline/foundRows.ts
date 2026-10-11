/**
 * What this device held when the server's history changed (a restore took the data back to a backup: `Session.instance.generation`): the
 * downloaded vehicles, logs and schedules are set aside before the device starts afresh, and once the new download is through, the ones
 * the server no longer has (or has at an older version) are listed on Waiting to sync as *Found on this device*. Nothing is sent by
 * itself: the person sends a row to the server (an add with the row's own id and values, or an edit against the server's version) or
 * forgets it. Pure where it can be (`compare`); the store is the user's `found` table, which the wipe spares.
 */
import type { ApolloClient } from '@apollo/client'
import { type ChangeEntity, type LogEntity } from './changes.ts'
import { valuesOf } from './conflicts.ts'
import { deviceData } from './deviceData.ts'
import type { LogRow, RowStore, VehicleRow } from './deviceStorage.ts'
import { snapshotKey } from './snapshotPolicy.ts'
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

/** The schedules of a vehicle as this device kept them (the `RecurringExpenses` answer). */
async function schedulesOf(kept: Kept, vehicleId: string): Promise<Record<string, unknown>[]> {
  const answer = await kept('RecurringExpenses', { vehicleId })
  const vehicle = answer?.vehicle as { recurring?: unknown } | undefined
  return Array.isArray(vehicle?.recurring) ? (vehicle.recurring as Record<string, unknown>[]) : []
}

/**
 * Which rows set aside the server lacks, or holds at an older version than this device did: those stay listed; the others go. A row of a
 * vehicle whose download is not complete yet is left as it is (nothing can be said about it yet).
 */
export function compare(rows: readonly FoundRow[], held: Held, complete: (vehicleId: string) => boolean): { keep: FoundRow[]; drop: string[] } {
  const keep: FoundRow[] = []
  const drop: string[] = []
  for (const row of rows) {
    if (!complete(row.vehicleId)) {
      keep.push(row)
      continue
    }
    const server = held(row.entity, row.id)
    if (server !== undefined && server >= row.version) drop.push(row.id)
    else keep.push(server === undefined ? { ...row, serverVersion: undefined } : { ...row, serverVersion: server })
  }
  return { keep, drop }
}

/** Oldest first within a vehicle, the vehicle itself before its rows, so neighbours exist when the server takes them. */
export function inSendOrder(rows: readonly FoundRow[]): FoundRow[] {
  const rank = (row: FoundRow) => (row.entity === 'vehicles' ? 0 : row.entity === 'recurring' ? 1 : 2)
  return [...rows].sort((a, b) => a.vehicleId.localeCompare(b.vehicleId) || rank(a) - rank(b) || (a.date ?? '').localeCompare(b.date ?? '') || a.id.localeCompare(b.id))
}

export const foundRows = {
  /** Before the device starts afresh: everything it holds of the old history, added to what may already be set aside (never cleared: two tabs may do this at once). */
  async setAside(rows: RowStore, kept: Kept, generation: string, now = Date.now()): Promise<number> {
    const list: FoundRow[] = []
    const vehicles = await rows.vehicles()
    for (const vehicle of vehicles) {
      const own = found('vehicles', vehicle.id, vehicle as VehicleRow, generation, now)
      if (own) list.push(own)
      for (const kind of ['refuelings', 'expenses'] as const)
        for (const log of await rows.logs(kind, vehicle.id)) {
          const row = found(kind as LogEntity, vehicle.id, log as LogRow, generation, now)
          if (row) list.push(row)
        }
      for (const schedule of await schedulesOf(kept, vehicle.id)) {
        const row = found('recurring', vehicle.id, schedule, generation, now)
        if (row) list.push(row)
      }
    }
    await rows.putFound(list)
    return list.length
  },

  /** After a download: what the server has again leaves the list. */
  async reconcile(rows: RowStore, kept: Kept): Promise<void> {
    const all = await rows.found()
    if (all.length === 0) return
    const vehicles = new Map((await rows.vehicles()).map((v) => [v.id, v]))
    const cursors = new Map((await rows.cursors()).map((c) => [c.vehicleId, c]))
    const logs = new Map<string, LogRow>()
    const schedules = new Map<string, Record<string, unknown>>()
    for (const vehicleId of new Set(all.map((r) => r.vehicleId))) {
      if (!vehicles.has(vehicleId)) continue
      for (const kind of ['refuelings', 'expenses'] as const) for (const log of await rows.logs(kind, vehicleId)) logs.set(log.id, log)
      for (const schedule of await schedulesOf(kept, vehicleId)) if (typeof schedule.id === 'string') schedules.set(schedule.id, schedule)
    }
    const version = (row: Record<string, unknown> | undefined) => (typeof row?.version === 'number' ? row.version : row ? 0 : undefined)
    const held: Held = (entity, id) => (entity === 'vehicles' ? version(vehicles.get(id)) : entity === 'recurring' ? version(schedules.get(id)) : version(logs.get(id)))
    // A vehicle the server no longer lists is gone with everything below it: its rows are found; a listed one must be downloaded in full.
    const complete = (vehicleId: string) => !vehicles.has(vehicleId) || cursors.get(vehicleId)?.complete === true
    const { keep, drop } = compare(all, held, complete)
    if (drop.length > 0) await rows.deleteFound(drop)
    const changed = keep.filter((row) => row.serverVersion !== all.find((r) => r.id === row.id)?.serverVersion)
    if (changed.length > 0) await rows.putFound(changed)
  },

  async list(): Promise<FoundRow[]> {
    return (await deviceData.rows())?.found() ?? []
  },

  async forget(ids: string[]): Promise<void> {
    await (await deviceData.rows())?.deleteFound(ids)
  },

  /**
   * Queues a row for the server as a change waiting on this device: an add with the row's own id and values (the server creates it under
   * that id, or answers from its ledger when it knows the id), or, when the server holds an older version, an edit against that version.
   * The push sends it like any other change; what the server refuses parks, as always.
   */
  async send(client: ApolloClient, rows: readonly FoundRow[]): Promise<void> {
    for (const row of inSendOrder(rows)) {
      await keepChange(client, row.serverVersion === undefined
        ? { id: row.id, entity: row.entity, action: 'add', vehicleId: row.vehicleId, targetId: row.id, input: row.values }
        : { id: uuidV4(), entity: row.entity, action: 'update', vehicleId: row.vehicleId, targetId: row.id, input: row.values, expectedVersion: row.serverVersion })
    }
    await foundRows.forget(rows.map((r) => r.id))
  },
}

/** The kept answers of the open user's data, for the schedules. */
export const keptAnswers: Kept = async (operationName, variables) => (await deviceData.read(snapshotKey(operationName, variables)))?.data as Record<string, unknown> | undefined
