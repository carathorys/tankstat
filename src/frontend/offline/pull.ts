import type { ApolloClient } from '@apollo/client'
import { OfflineChangesDocument, OfflineSettingsDocument, WelcomeDocument, type OfflineChangesQuery } from '../gql/generated.ts'
import { HOME_PAGE_SIZE } from '../homePaging.ts'
import { connectivity } from './connectivity.ts'
import { deviceData } from './deviceData.ts'
import type { LogKind, LogRow, PullCursor, RowStore, VehicleRow } from './deviceStorage.ts'
import { isConnectionFailure } from './errors.ts'
import { DEFAULT_RULE, fromDate, narrower } from './offlineWindow.ts'
import { snapshotKey } from './snapshotPolicy.ts'

/** Rows per table and page (the server's maximum). */
export const PAGE_SIZE = 200
/** The home list the download covers, like the Arrange dialog: the user's vehicles up to the server's page limit. */
export const MAX_VEHICLES = 200

type Page = OfflineChangesQuery['offlineChanges']

export interface PullState {
  status: 'idle' | 'pulling'
  vehiclesDone: number
  vehiclesTotal: number
  /** When the last download finished (Date.now()), in this page's life. */
  lastPullAt: number | null
  /** The last one stopped because the server went out of reach. */
  interrupted: boolean
}

export interface PullDeps {
  client: ApolloClient
  device?: typeof deviceData
  now?: () => Date
  pageSize?: number
  /** How many vehicles download at the same time. */
  concurrency?: number
}

/**
 * Downloads the user's offline window and keeps it current (`offlineChanges` on the server, `offline-download` in the docs):
 * 1. the home list (one request): the vehicles are kept, the ones no longer listed are dropped with everything below them (access lost,
 *    trashed or purged by someone else), and its pages are kept for the home page;
 * 2. the window rules (`offlineSettings`), turned into a start date per vehicle on this device's clock;
 * 3. per vehicle, two at a time: a **full** download the first time, when the window grew or the server says the last one is too old
 *    (`resync`); otherwise only **what changed** since the last one's watermark, whatever its date, plus what was removed for good. A
 *    window that shrank drops the older logs here and downloads nothing again. Each page is stored before the next is asked, and a full
 *    download goes on from where it stopped; at its end the logs it did not bring again are removed. The vehicle's details and schedules
 *    come with the first page and are kept as the answers of `VehicleDetails` and `RecurringExpenses`.
 * One run at a time; a run asked for meanwhile follows it. A lost connection ends the run (the next one goes on from the cursors).
 */
export function createPullEngine({ client, device = deviceData, now = () => new Date(), pageSize = PAGE_SIZE, concurrency = 2 }: PullDeps) {
  let state: PullState = { status: 'idle', vehiclesDone: 0, vehiclesTotal: 0, lastPullAt: null, interrupted: false }
  const listeners = new Set<() => void>()
  const set = (next: Partial<PullState>) => {
    state = { ...state, ...next }
    listeners.forEach((listener) => listener())
  }
  let running: Promise<void> | null = null
  let again = false

  const query = async <T>(run: () => Promise<{ data?: T }>): Promise<T> => {
    const { data } = await run()
    if (!data) throw new Error('The server sent no data.')
    return data
  }

  async function keepHomePages(vehicles: readonly VehicleRow[], total: number) {
    for (let skip = 0; skip === 0 || skip < vehicles.length; skip += HOME_PAGE_SIZE) {
      const key = snapshotKey('Welcome', { search: null, skip, take: HOME_PAGE_SIZE })
      await device.keep(key, { myVehicles: vehicles.slice(skip, skip + HOME_PAGE_SIZE), myVehicleCount: total, vehicleTotal: total })
    }
  }

  async function keepVehicleAnswers(vehicleId: string, page: Page) {
    await device.keep(snapshotKey('VehicleDetails', { id: vehicleId }), { vehicle: page.vehicle })
    await device.keep(snapshotKey('RecurringExpenses', { vehicleId }), { vehicle: { __typename: 'Vehicle', id: vehicleId, recurring: page.recurring } })
  }

  /** Stores a page's logs; one that moved out of the window (dated before its start) is removed instead. */
  async function ingest(rows: RowStore, kind: LogKind, list: readonly Record<string, unknown>[], from: string | null, stamp: number) {
    const inside: LogRow[] = []
    const outside: string[] = []
    for (const row of list as readonly LogRow[]) {
      if (from !== null && row.date < from) outside.push(row.id)
      else inside.push({ ...row, pulledAt: stamp })
    }
    await rows.putLogs(kind, inside)
    await rows.deleteLogs(kind, outside)
  }

  async function removeBefore(rows: RowStore, vehicleId: string, test: (row: LogRow) => boolean) {
    for (const kind of ['refuelings', 'expenses'] as const) await rows.deleteLogs(kind, (await rows.logs(kind, vehicleId)).filter(test).map((r) => r.id))
  }

  const ask = (input: Record<string, unknown>) =>
    query(() => client.query({ query: OfflineChangesDocument, variables: { input: input as never }, fetchPolicy: 'no-cache' })).then((d) => d.offlineChanges)

  async function full(rows: RowStore, cursor: PullCursor): Promise<void> {
    const from = cursor.from === false ? null : cursor.from
    const stamp = cursor.fullStartedAt ?? Date.now()
    let next = cursor.next
    for (;;) {
      const page = await ask(next ? { vehicleId: cursor.vehicleId, after: next, take: pageSize } : { vehicleId: cursor.vehicleId, from, take: pageSize })
      await ingest(rows, 'refuelings', page.refuelings, from, stamp)
      await ingest(rows, 'expenses', page.expenses, from, stamp)
      if (!next) await keepVehicleAnswers(cursor.vehicleId, page)
      next = page.next ?? null
      if (!next) {
        await removeBefore(rows, cursor.vehicleId, (r) => r.pulledAt < stamp) // what this download did not bring again is gone
        await rows.putCursor({ ...cursor, watermark: page.watermark, next: null, complete: true, fullStartedAt: null })
        return
      }
      await rows.putCursor({ ...cursor, next, fullStartedAt: stamp })
    }
  }

  async function later(rows: RowStore, cursor: PullCursor): Promise<void> {
    const from = cursor.from === false ? null : cursor.from
    const stamp = Date.now()
    let next: string | null = null
    let watermark: string | null = null
    do {
      const page: Page = await ask(next ? { vehicleId: cursor.vehicleId, after: next, take: pageSize } : { vehicleId: cursor.vehicleId, since: cursor.watermark, take: pageSize })
      if (page.resync) return full(rows, await restart(rows, cursor)) // the last download is older than what the server remembers
      if (!next) {
        watermark = page.watermark
        await keepVehicleAnswers(cursor.vehicleId, page)
        for (const kind of ['refuelings', 'expenses'] as const) {
          const type = kind === 'refuelings' ? 'REFUELING' : 'EXPENSE'
          await rows.deleteLogs(kind, page.removed.filter((r) => r.type === type).map((r) => r.id))
        }
      }
      await ingest(rows, 'refuelings', page.refuelings, from, stamp)
      await ingest(rows, 'expenses', page.expenses, from, stamp)
      next = page.next ?? null
    } while (next)
    await rows.putCursor({ ...cursor, watermark })
  }

  async function restart(rows: RowStore, cursor: PullCursor | undefined, from: string | null = cursor?.from === false ? null : (cursor?.from ?? null)) {
    const fresh: PullCursor = { vehicleId: cursor!.vehicleId, from, watermark: null, next: null, complete: cursor?.complete ?? false, fullStartedAt: Date.now() }
    await rows.putCursor(fresh)
    return fresh
  }

  async function pullVehicle(rows: RowStore, vehicleId: string, rule: string) {
    const from = fromDate(rule, now())
    const cursor = await rows.cursor(vehicleId)
    if (from === false) {
      // Nothing of its logs on this device: the vehicle's card and page come from the home list and the answers seen.
      if (cursor?.from !== false) await removeBefore(rows, vehicleId, () => true)
      await rows.putCursor({ vehicleId, from: false, watermark: null, next: null, complete: true, fullStartedAt: null })
      return
    }
    const started = cursor?.fullStartedAt != null
    const firstOrWider = !cursor || cursor.from === false || (!started && cursor.watermark === null) || narrower(cursor.from, from)
    if (firstOrWider || (started && cursor.from !== from)) return full(rows, await restart(rows, { ...(cursor ?? { vehicleId, complete: false }), vehicleId } as PullCursor, from))
    if (started) return full(rows, cursor)
    if (narrower(from, cursor.from)) {
      await removeBefore(rows, vehicleId, (r) => r.date < from!) // the window shrank: nothing to download again
      await rows.putCursor({ ...cursor, from })
      return later(rows, { ...cursor, from })
    }
    return later(rows, cursor)
  }

  async function runOnce() {
    const rows = await device.writableRows()
    if (!rows || !connectivity.reachable) return
    set({ status: 'pulling', vehiclesDone: 0, vehiclesTotal: 0, interrupted: false })
    try {
      const home = await query(() => client.query({ query: WelcomeDocument, variables: { search: null, skip: 0, take: MAX_VEHICLES }, fetchPolicy: 'network-only' }))
      const vehicles = home.myVehicles as unknown as VehicleRow[]
      await rows.putVehicles(vehicles)
      await keepHomePages(vehicles, home.vehicleTotal)
      if (vehicles.length === home.vehicleTotal) {
        const listed = new Set(vehicles.map((v) => v.id))
        for (const gone of (await rows.vehicles()).filter((v) => !listed.has(v.id))) await rows.dropVehicle(gone.id)
      }
      const settings = (await query(() => client.query({ query: OfflineSettingsDocument, fetchPolicy: 'network-only' }))).offlineSettings
      const rules = new Map(settings.vehicles.map((v) => [v.vehicleId, v.window]))
      set({ vehiclesTotal: vehicles.length })
      const queue = [...vehicles]
      const worker = async () => {
        for (let vehicle = queue.shift(); vehicle; vehicle = queue.shift()) {
          try {
            await pullVehicle(rows, vehicle.id, rules.get(vehicle.id) ?? settings.defaultWindow ?? DEFAULT_RULE)
          } catch (error) {
            if (isConnectionFailure(error)) throw error
            if (keyOf(error) === 'vehicle.notFound') await rows.dropVehicle(vehicle.id) // access lost meanwhile
            else console.warn('The offline download of a vehicle failed; the next one tries again.', error)
          }
          set({ vehiclesDone: state.vehiclesDone + 1 })
        }
      }
      await Promise.all(Array.from({ length: Math.min(concurrency, queue.length) }, worker))
      set({ status: 'idle', lastPullAt: Date.now() })
    } catch (error) {
      set({ status: 'idle', interrupted: isConnectionFailure(error) })
      if (!isConnectionFailure(error)) console.warn('The offline download failed; the next one tries again.', error)
    }
  }

  return {
    get state(): PullState {
      return state
    },
    subscribe(listener: () => void): () => void {
      listeners.add(listener)
      return () => {
        listeners.delete(listener)
      }
    },
    /** Downloads now, or right after the run under way. */
    run(): Promise<void> {
      if (running) {
        again = true
        return running
      }
      running = (async () => {
        do {
          again = false
          await runOnce()
        } while (again)
      })().finally(() => {
        running = null
      })
      return running
    },
  }
}

export type PullEngine = ReturnType<typeof createPullEngine>

function keyOf(error: unknown): string | undefined {
  const errors = (error as { errors?: readonly { extensions?: { key?: unknown } }[] } | null)?.errors
  const key = errors?.[0]?.extensions?.key
  return typeof key === 'string' ? key : undefined
}
