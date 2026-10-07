import type { ExpenseSortField, RefuelingSortField, SortDirection } from '../gql/generated.ts'
import type { LogKind, LogRow, PullCursor, RowStore, VehicleRow } from './deviceStorage.ts'

/**
 * Answers the log queries from the downloaded window while the server is out of reach (`offlineLink.ts`): any page and any order of a
 * vehicle's refuelings and expenses, a log's details and the trash. The rows are the server's own (the offline feed brings every field
 * the documents select, with their `__typename`s), so nothing is worked out here except what the repositories do: the order, the paging,
 * the counts, and who may edit (a log's `canEdit`/`canDelete` follow its vehicle's `logAccess`, which every download refreshes).
 * A query the device cannot answer (a vehicle not downloaded yet, a log outside the window) gets `undefined`, and the link falls back to
 * the last answer seen.
 */
type Variables = Record<string, unknown>
type Resolver = (rows: RowStore, variables: Variables) => Promise<Record<string, unknown> | undefined>

const LEVELS = ['NONE', 'VIEW', 'EDIT', 'DELETE']
const atLeast = (access: string | undefined, level: 'EDIT' | 'DELETE') => LEVELS.indexOf(access ?? 'NONE') >= LEVELS.indexOf(level)

/** Comparable values: null sorts first going up (and last going down), as the database does. */
type Key = string | number | null

function compare(a: Key, b: Key): number {
  if (a === b) return 0
  if (a === null) return -1
  if (b === null) return 1
  return a < b ? -1 : 1
}

const num = (value: unknown): number | null => (typeof value === 'number' ? value : null)
const text = (value: unknown): string | null => (typeof value === 'string' ? value : null)
const lower = (value: unknown): string | null => text(value)?.toLowerCase() ?? null
const creator = (row: LogRow) => lower((row.createdBy as { displayName?: string } | null)?.displayName)

/** The repositories' orders (`RefuelingRepository.Page`): the field, then the odometer the same way, then the id. */
export function refuelingKey(field: RefuelingSortField, vehicles: Map<string, VehicleRow>): (row: LogRow) => Key {
  switch (field) {
    case 'VOLUME':
      return (r) => num(r.volume)
    case 'TOTAL_COST':
      return (r) => num(r.totalCost) ?? 0
    case 'ODOMETER':
      return (r) => num(r.odometer) ?? 0
    case 'CONSUMPTION':
      return (r) => num(r.consumption)
    case 'PRICE_PER_UNIT':
      return (r) => (num(r.totalCost) !== null && num(r.volume) ? (r.totalCost as number) / (r.volume as number) : 0)
    case 'CREATED_BY':
      return creator
    case 'VEHICLE':
      return (r) => lower(vehicles.get(r.vehicleId)?.name)
    case 'DELETED_AT':
      return (r) => text(r.deletedAt)
    default:
      return (r) => r.date
  }
}

/** `ExpenseRepository.Page`: the field, then the id. */
export function expenseKey(field: ExpenseSortField, vehicles: Map<string, VehicleRow>): (row: LogRow) => Key {
  switch (field) {
    case 'TITLE':
      return (r) => lower(r.title)
    case 'CATEGORY':
      return (r) => lower(r.category) ?? ''
    case 'AMOUNT':
      return (r) => num(r.amount) ?? 0
    case 'ODOMETER':
      return (r) => num(r.odometer) ?? 0
    case 'CREATED_BY':
      return creator
    case 'VEHICLE':
      return (r) => lower(vehicles.get(r.vehicleId)?.name)
    case 'DELETED_AT':
      return (r) => text(r.deletedAt)
    default:
      return (r) => r.date
  }
}

export function sortLogs(kind: LogKind, rows: readonly LogRow[], field: string, direction: SortDirection, vehicles: Map<string, VehicleRow>): LogRow[] {
  const key = kind === 'refuelings' ? refuelingKey(field as RefuelingSortField, vehicles) : expenseKey(field as ExpenseSortField, vehicles)
  const sign = direction === 'DESC' ? -1 : 1
  const odometer = (r: LogRow) => num(r.odometer) ?? 0
  return [...rows].sort(
    (a, b) =>
      sign * compare(key(a), key(b)) ||
      (kind === 'refuelings' ? sign * compare(odometer(a), odometer(b)) : 0) ||
      compare(a.id.toUpperCase(), b.id.toUpperCase()),
  )
}

/** A log as a query answers it: who may edit it follows its vehicle today. */
function asAnswer(row: LogRow, vehicle: VehicleRow | undefined): Record<string, unknown> {
  const { pulledAt: _pulledAt, ...fields } = row
  return { ...fields, canEdit: atLeast(vehicle?.logAccess, 'EDIT'), canDelete: atLeast(vehicle?.logAccess, 'DELETE') }
}

const page = <T>(rows: readonly T[], variables: Variables) => {
  const skip = Math.max(0, Number(variables.skip ?? 0))
  return rows.slice(skip, skip + Math.max(0, Number(variables.take ?? rows.length)))
}

async function vehicleMap(rows: RowStore) {
  return new Map((await rows.vehicles()).map((v) => [v.id, v]))
}

/** A vehicle whose logs this device holds: its download finished at least once, and its window is not "none". */
const holdsLogs = (cursor: PullCursor | undefined) => cursor?.complete === true && cursor.from !== false

/** The vehicle's logs, when the device holds them. */
async function downloaded(rows: RowStore, kind: LogKind, vehicleId: unknown) {
  if (typeof vehicleId !== 'string' || !holdsLogs(await rows.cursor(vehicleId))) return undefined
  return (await rows.logs(kind, vehicleId)).filter((r) => !r.deletedAt)
}

function list(kind: LogKind, field: 'refuelings' | 'expenses', count: string): Resolver {
  return async (rows, variables) => {
    const live = await downloaded(rows, kind, variables.vehicleId)
    if (!live) return undefined
    const vehicles = await vehicleMap(rows)
    const sorted = sortLogs(kind, live, String(variables.orderBy ?? 'DATE'), (variables.direction as SortDirection) ?? 'DESC', vehicles)
    return { [field]: page(sorted, variables).map((r) => asAnswer(r, vehicles.get(r.vehicleId))), [count]: live.length }
  }
}

function details(kind: LogKind, field: 'refueling' | 'expense'): Resolver {
  return async (rows, variables) => {
    const row = typeof variables.id === 'string' ? await rows.log(kind, variables.id) : undefined
    if (!row || row.deletedAt) return undefined
    return { [field]: asAnswer(row, (await vehicleMap(rows)).get(row.vehicleId)) }
  }
}

/** The trash of the downloaded vehicles whose logs the user may edit; the deletable count, of those they may delete. */
function trash(kind: LogKind, field: string, count: string, deletable: string): Resolver {
  return async (rows, variables) => {
    const vehicles = await vehicleMap(rows)
    const complete = new Set((await rows.cursors()).filter(holdsLogs).map((c) => c.vehicleId))
    if (complete.size === 0) return undefined
    const trashed = (await rows.allLogs(kind)).filter((r) => r.deletedAt && complete.has(r.vehicleId) && atLeast(vehicles.get(r.vehicleId)?.logAccess, 'EDIT'))
    const sorted = sortLogs(kind, trashed, String(variables.orderBy ?? 'DELETED_AT'), (variables.direction as SortDirection) ?? 'DESC', vehicles)
    return {
      [field]: page(sorted, variables).map((r) => {
        const vehicle = vehicles.get(r.vehicleId)
        return { ...asAnswer(r, vehicle), vehicle: vehicle && { __typename: 'Vehicle', id: vehicle.id, name: vehicle.name, units: vehicle.units } }
      }),
      [count]: trashed.length,
      [deletable]: trashed.filter((r) => atLeast(vehicles.get(r.vehicleId)?.logAccess, 'DELETE')).length,
    }
  }
}

const RESOLVERS: Record<string, Resolver> = {
  Refuelings: list('refuelings', 'refuelings', 'refuelingCount'),
  Expenses: list('expenses', 'expenses', 'expenseCount'),
  RefuelingDetails: details('refuelings', 'refueling'),
  ExpenseDetails: details('expenses', 'expense'),
  RefuelingTrash: trash('refuelings', 'refuelingTrash', 'refuelingTrashCount', 'refuelingTrashDeletableCount'),
  ExpenseTrash: trash('expenses', 'expenseTrash', 'expenseTrashCount', 'expenseTrashDeletableCount'),
}

/** The device's own answer to a query, or undefined when it has none (not one of these queries, or not downloaded). */
export async function answerLocally(rows: RowStore | null, operationName: string | undefined, variables: Variables): Promise<Record<string, unknown> | undefined> {
  const resolver = RESOLVERS[operationName ?? '']
  if (!rows || !resolver) return undefined
  return resolver(rows, variables)
}
