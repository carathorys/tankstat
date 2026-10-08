import type { ApolloClient } from '@apollo/client'
import { SyncChangesDocument, type ChangeInput, type SyncChangesMutation } from '../gql/generated.ts'
import type { Change } from './changes.ts'
import { connectivity } from './connectivity.ts'
import { deviceData } from './deviceData.ts'
import { isConnectionFailure } from './errors.ts'
import { outbox } from './outbox.ts'

/** Changes per request (the server takes at most 200). */
export const CHUNK_SIZE = 20

type Result = SyncChangesMutation['syncChanges']['results'][number]

/** A change the server could not apply: it keeps it (parked) with the reason; the device shows it until the next sync. */
export interface ParkedChange {
  change: Change
  key: string
  args: Record<string, string>
}

export interface PushState {
  status: 'idle' | 'syncing'
  /** What the last sync did (in this page's life). */
  last: { at: number; applied: number; parked: ParkedChange[]; interrupted: boolean } | null
}

export interface PushDeps {
  client: ApolloClient
  device?: typeof deviceData
  /** The download that follows a sync, so what was applied comes back from the server (ids, versions, what it worked out). */
  pull?: () => Promise<void>
  chunkSize?: number
}

/** What each mutation's input may carry: a form may hold more (a client id twice, values only the screen uses). */
const FIELDS = {
  logRefueling: ['id', 'vehicleId', 'date', 'volume', 'totalCost', 'currency', 'odometer', 'isFullTank', 'missedPreviousFillUp', 'note', 'photoIds'],
  updateRefueling: ['id', 'date', 'volume', 'totalCost', 'currency', 'odometer', 'isFullTank', 'missedPreviousFillUp', 'note'],
  addExpense: ['id', 'vehicleId', 'date', 'title', 'category', 'amount', 'currency', 'odometer', 'note', 'photoIds'],
  updateExpense: ['id', 'date', 'title', 'category', 'amount', 'currency', 'odometer', 'note'],
  addVehicle: ['id', 'name', 'licensePlate', 'fuelType', 'units'],
  updateVehicle: ['id', 'name', 'licensePlate', 'fuelType', 'units'],
  addRecurringExpense: ['id', 'vehicleId', 'title', 'category', 'note', 'kind', 'intervalMonths', 'intervalDistance', 'lastDoneDate', 'lastDoneOdometer', 'warnDays', 'warnDistance'],
  updateRecurringExpense: ['id', 'title', 'category', 'note', 'kind', 'intervalMonths', 'intervalDistance', 'lastDoneDate', 'lastDoneOdometer', 'warnDays', 'warnDistance'],
  markRecurringExpensesDone: ['ids', 'date', 'odometer', 'amount', 'currency', 'title', 'category', 'photoIds', 'expenseId'],
} as const

const pick = (input: Record<string, unknown> | undefined, keys: readonly string[], extra: Record<string, unknown> = {}) =>
  Object.fromEntries(Object.entries({ ...input, ...extra }).filter(([k, v]) => keys.includes(k) && v !== undefined))

/** A waiting change as the server's `ChangeInput`: its id, the version it was made from, and its one operation. */
export function toChangeInput(c: Change): ChangeInput {
  const base = { id: c.id, expectedVersion: c.expectedVersion ?? null }
  const op = (name: keyof typeof FIELDS, extra: Record<string, unknown> = {}) => ({ ...base, [name]: pick(c.input, FIELDS[name], extra) }) as ChangeInput
  switch (`${c.entity}:${c.action}`) {
    case 'refuelings:add': return op('logRefueling', { id: c.targetId, vehicleId: c.vehicleId })
    case 'refuelings:update': return op('updateRefueling', { id: c.targetId })
    case 'refuelings:trash': return { ...base, deleteRefueling: c.targetId }
    case 'refuelings:restore': return { ...base, restoreRefueling: c.targetId }
    case 'expenses:add': return op('addExpense', { id: c.targetId, vehicleId: c.vehicleId })
    case 'expenses:update': return op('updateExpense', { id: c.targetId })
    case 'expenses:trash': return { ...base, deleteExpense: c.targetId }
    case 'expenses:restore': return { ...base, restoreExpense: c.targetId }
    case 'vehicles:add': return op('addVehicle', { id: c.targetId })
    case 'vehicles:update': return op('updateVehicle', { id: c.targetId })
    case 'vehicles:trash': return { ...base, deleteVehicle: c.targetId }
    case 'vehicles:restore': return { ...base, restoreVehicle: c.targetId }
    case 'recurring:add': return op('addRecurringExpense', { id: c.targetId, vehicleId: c.vehicleId })
    case 'recurring:update': return op('updateRecurringExpense', { id: c.targetId })
    case 'recurring:trash': return { ...base, deleteRecurringExpense: c.targetId }
    case 'recurring:markDone': return op('markRecurringExpensesDone', { ids: c.targetIds ?? [] })
    default: throw new Error(`A change of ${c.entity} cannot ${c.action}.`)
  }
}

/** What a change needs the server to have first: the vehicle it was made on, or the schedule it changes or marks done, when added here. */
const needs = (c: Change): string[] => [c.vehicleId, ...(c.entity === 'recurring' ? [c.targetId, ...(c.targetIds ?? [])] : [])]

/**
 * The order the server gets the changes in: the vehicles added here first (everything else may be made on them), then the rest as it was
 * made; requests of `size`, a change never in the same request as the add it depends on.
 */
export function batches(changes: readonly Change[], size = CHUNK_SIZE): Change[][] {
  const vehicles = changes.filter((c) => c.entity === 'vehicles' && c.action === 'add')
  const rest = changes.filter((c) => !vehicles.includes(c)).sort((a, b) => a.seq - b.seq)
  const out: Change[][] = []
  for (let i = 0; i < vehicles.length; i += size) out.push(vehicles.slice(i, i + size))
  let current: Change[] = []
  const addsIn = new Set<string>()
  for (const change of rest) {
    if (current.length === size || needs(change).some((id) => addsIn.has(id))) {
      out.push(current)
      current = []
      addsIn.clear()
    }
    current.push(change)
    if (change.action === 'add') addsIn.add(change.targetId)
  }
  if (current.length > 0) out.push(current)
  return out
}

/**
 * Sends the changes kept on this device (`outbox.ts`) once the server can be reached, in order (`batches`), with `syncChanges`. The
 * server applies each through the same rules as online, or parks it with the reason; either way it has it, so it leaves the device (the
 * parked ones are listed with their reason until the next sync, and on the server for everyone who may see the vehicle). A change made on
 * an add the server parked is not sent (it could only fail); it waits on the device. A lost connection ends the run (the server answers a
 * change sent again with what it did the first time). Then the download brings the server's rows, and only then do the kept changes
 * leave the screen, so nothing blinks in between. One run at a time.
 */
export function createPushEngine({ client, device = deviceData, pull, chunkSize = CHUNK_SIZE }: PushDeps) {
  let state: PushState = { status: 'idle', last: null }
  const listeners = new Set<() => void>()
  const set = (next: Partial<PushState>) => {
    state = { ...state, ...next }
    listeners.forEach((listener) => listener())
  }
  let running: Promise<void> | null = null
  let again = false

  async function runOnce() {
    if (!connectivity.reachable || device.user === null || outbox.changes.length === 0) return
    set({ status: 'syncing' })
    const answered: string[] = []
    const parked: ParkedChange[] = []
    const blocked = new Set<string>() // adds the server parked: what depends on them waits
    let interrupted = false
    try {
      for (const chunk of batches(outbox.changes, chunkSize)) {
        const sendable = chunk.filter((c) => !needs(c).some((id) => blocked.has(id)))
        if (sendable.length === 0) continue
        const { data } = await client.mutate({ mutation: SyncChangesDocument, variables: { input: { changes: sendable.map(toChangeInput) } } })
        const results = new Map<string, Result>((data?.syncChanges.results ?? []).map((r) => [r.id, r]))
        for (const change of sendable) {
          const result = results.get(change.id)
          if (!result) continue
          answered.push(change.id)
          if (result.status === 'PARKED') {
            parked.push({ change, key: result.reason?.key ?? '', args: Object.fromEntries((result.reason?.args ?? []).map((a) => [a.name, a.value])) })
            if (change.action === 'add') blocked.add(change.targetId)
          }
        }
      }
    } catch (error) {
      if (!isConnectionFailure(error)) console.warn('Sending the changes kept on this device failed; the next sync tries again.', error)
      interrupted = true
    }
    if (answered.length > 0) {
      await pull?.().catch(() => undefined) // the server's rows first...
      await outbox.remove(answered) // ...then the kept changes leave
      void client.refetchQueries({ include: 'active' }).catch(() => undefined)
    }
    set({ status: 'idle', last: { at: Date.now(), applied: answered.length - parked.length, parked, interrupted } })
  }

  return {
    get state(): PushState {
      return state
    },
    subscribe(listener: () => void): () => void {
      listeners.add(listener)
      return () => {
        listeners.delete(listener)
      }
    },
    /** Sends now, or right after the run under way. */
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

export type PushEngine = ReturnType<typeof createPushEngine>
