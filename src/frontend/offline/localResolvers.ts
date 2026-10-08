import type { ExpenseSortField, RefuelingSortField, SortDirection } from '../gql/generated.ts'
import { keptPhotosOf, type Change } from './changes.ts'
import type { LogKind, LogRow, PullCursor, RowStore, VehicleRow } from './deviceStorage.ts'
import { keptPhotos } from './keptPhotos.ts'
import { outbox } from './outbox.ts'

/**
 * Answers the log queries from the downloaded window while the server is out of reach (`offlineLink.ts`): any page and any order of a
 * vehicle's refuelings and expenses, a log's details and the trash. The rows are the server's own (the offline feed brings every field
 * the documents select, with their `__typename`s), so nothing is worked out here except what the repositories do: the order, the paging,
 * the counts, and who may edit (a log's `canEdit`/`canDelete` follow its vehicle's `logAccess`, which every download refreshes).
 * A query the device cannot answer (a vehicle not downloaded yet, a log outside the window) gets `undefined`, and the link falls back to
 * the last answer seen.
 */
type Variables = Record<string, unknown>
/** The last answer seen of a query (an operation and its variables), or undefined. */
type Kept = (operationName: string, variables: Variables) => Promise<Record<string, unknown> | undefined>
type Resolver = (rows: RowStore, variables: Variables, kept: Kept) => Promise<Record<string, unknown> | undefined>

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

/** The values an add or an update carries onto the row; the price per unit follows them, a person's save leaves nothing to review. */
const VALUES: Record<LogKind, readonly string[]> = {
  refuelings: ['date', 'volume', 'totalCost', 'currency', 'odometer', 'isFullTank', 'missedPreviousFillUp', 'note'],
  expenses: ['date', 'title', 'category', 'amount', 'currency', 'odometer', 'note'],
}

function withValues(kind: LogKind, row: LogRow, input: Record<string, unknown> | undefined): LogRow {
  const values = Object.fromEntries(VALUES[kind].filter((k) => input && k in input).map((k) => [k, input![k]]))
  const next: LogRow = { ...row, ...values, reviewState: 'NONE', filledFromPhoto: [] }
  if (kind === 'refuelings') next.pricePerUnit = num(next.totalCost) !== null && num(next.volume) ? Math.round(((next.totalCost as number) / (next.volume as number)) * 1000) / 1000 : null
  return next
}

/** A log added on this device and not sent yet: what the server would answer, without what only the server works out. */
function added(kind: LogKind, change: Change): LogRow {
  const common = {
    id: change.targetId, vehicleId: change.vehicleId, date: '', deletedAt: null, version: 0, updatedAt: new Date(change.createdAt).toISOString(),
    createdBy: null, photos: [], pulledAt: 0, note: null, odometer: null, currency: null,
  }
  const row: LogRow =
    kind === 'refuelings'
      ? { __typename: 'Refueling', ...common, volume: null, totalCost: null, consumption: null, isFullTank: true, missedPreviousFillUp: false }
      : { __typename: 'Expense', ...common, title: '', category: null, amount: null, schedules: [] }
  return withValues(kind, row, change.input)
}

/**
 * The rows with the changes waiting on this device laid over them, never written into them (the next download may replace a row): an add
 * is a row of its own, an update its values, a restore takes the row out of the trash; a log waiting to be trashed stays where it is,
 * marked (`usePendingMark`).
 */
export function withChanges(kind: LogKind, rows: readonly LogRow[], vehicleId?: string): LogRow[] {
  const waiting = outbox.changes.filter((c) => c.entity === kind && (vehicleId === undefined || c.vehicleId === vehicleId))
  const visits = kind === 'expenses' && outbox.changes.some((c) => c.action === 'markDone')
  if (waiting.length === 0 && !visits) return [...rows]
  const byId = new Map(rows.map((r) => [r.id, r]))
  if (kind === 'expenses') {
    // A visit with an amount logs an expense: it is a row of its own until the server has it (its id is the visit's expense id).
    for (const visit of outbox.changes.filter((c) => c.action === 'markDone' && c.input?.amount != null && (vehicleId === undefined || c.vehicleId === vehicleId)))
      byId.set(visit.targetId, added('expenses', { ...visit, input: { ...visit.input, category: visit.input?.category || null, note: null } }))
  }
  for (const change of waiting) {
    const row = byId.get(change.targetId)
    if (change.action === 'add') byId.set(change.targetId, added(kind, change))
    else if (row && change.action === 'update') byId.set(row.id, withValues(kind, row, change.input))
    else if (row && change.action === 'restore') byId.set(row.id, { ...row, deletedAt: null })
  }
  return [...byId.values()]
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

/** A vehicle added on this device and not sent yet. */
const addedVehicle = (id: unknown) => outbox.changes.find((c) => c.entity === 'vehicles' && c.action === 'add' && c.targetId === id)

/** The vehicle's logs out of the trash (with the changes waiting), when the device holds them (all of them, for a vehicle it added). */
async function downloaded(rows: RowStore, kind: LogKind, vehicleId: unknown) {
  if (typeof vehicleId !== 'string' || !(addedVehicle(vehicleId) || holdsLogs(await rows.cursor(vehicleId)))) return undefined
  return withChanges(kind, await rows.logs(kind, vehicleId), vehicleId).filter((r) => !r.deletedAt)
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
    if (typeof variables.id !== 'string') return undefined
    const stored = await rows.log(kind, variables.id)
    const row = withChanges(kind, stored ? [stored] : []).find((r) => r.id === variables.id)
    if (!row || row.deletedAt) return undefined
    return { [field]: { ...asAnswer(row, (await vehicleMap(rows)).get(row.vehicleId)), photos: await photosWithChanges(kind, row) } }
  }
}

/**
 * A log's photos with the photo changes waiting laid over them: one to be removed is gone, one kept on this device (picked for the add, or
 * added to the saved log) is there, at an address on the device (`keptPhotos.url`).
 */
async function photosWithChanges(kind: LogKind, row: LogRow): Promise<{ __typename: 'LogPhotoInfo'; id: string; url: string }[]> {
  const mine = outbox.changes.filter((c) => c.entity === kind && c.targetId === row.id)
  const removed = new Set(mine.filter((c) => c.action === 'removePhoto').map((c) => c.input?.imageId))
  const server = ((row.photos as { id: string; url: string }[] | undefined) ?? []).filter((p) => !removed.has(p.id))
  const keys = mine.flatMap(keptPhotosOf)
  const kept = await Promise.all(keys.map(async (id) => ({ id, url: await keptPhotos.url(id) })))
  return [...server, ...kept.flatMap((p) => (p.url ? [{ id: p.id, url: p.url }] : []))].map((p) => ({ __typename: 'LogPhotoInfo' as const, ...p }))
}

/** The trash of the downloaded vehicles whose logs the user may edit; the deletable count, of those they may delete. */
function trash(kind: LogKind, field: string, count: string, deletable: string): Resolver {
  return async (rows, variables) => {
    const vehicles = await vehicleMap(rows)
    const complete = new Set((await rows.cursors()).filter(holdsLogs).map((c) => c.vehicleId))
    if (complete.size === 0) return undefined
    const trashed = withChanges(kind, await rows.allLogs(kind)).filter((r) => r.deletedAt && complete.has(r.vehicleId) && atLeast(vehicles.get(r.vehicleId)?.logAccess, 'EDIT'))
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

/**
 * What a new log starts from (`RefuelingService.DefaultsAsync`): the latest odometer reading of the vehicle's logs (the latest day, its
 * highest value) and the currency of its latest refuelling. From the downloaded window with the changes waiting, so a log kept on the
 * device is where the next one starts.
 */
const logDefaults: Resolver = async (rows, variables) => {
  const refuelings = await downloaded(rows, 'refuelings', variables.vehicleId)
  const expenses = await downloaded(rows, 'expenses', variables.vehicleId)
  if (!refuelings || !expenses) return undefined
  const readings = [...refuelings, ...expenses].filter((r) => num(r.odometer) !== null)
  const latest = readings.sort((a, b) => compare(b.date, a.date) || compare(num(b.odometer), num(a.odometer)))[0]
  const lastRefueling = sortLogs('refuelings', refuelings, 'DATE', 'DESC', new Map())[0]
  return {
    logDefaults: { __typename: 'LogDefaults', lastOdometer: latest ? latest.odometer : null, lastDate: latest ? latest.date : null, currency: lastRefueling ? (lastRefueling.currency ?? null) : null },
  }
}

/** The categories of the vehicle's expenses, for the expense dialog's suggestions (`ExpenseRepository.CategoriesAsync`). */
const expenseCategories: Resolver = async (rows, variables) => {
  const expenses = await downloaded(rows, 'expenses', variables.vehicleId)
  if (!expenses) return undefined
  return { expenseCategories: [...new Set(expenses.map((e) => text(e.category)).filter((c): c is string => !!c))].sort() }
}

/** A change's values over an object the server sent (a vehicle, a schedule): only the fields the change carries. */
const over = <T extends Record<string, unknown>>(base: T, input: Record<string, unknown> | undefined, fields: readonly string[]): T => ({
  ...base,
  ...Object.fromEntries(fields.filter((f) => input && f in input && f !== 'units').map((f) => [f, input![f]])),
  ...(input?.units ? { units: { __typename: 'MeasurementUnits', ...(base.units as object | undefined), ...(input.units as object) } } : {}),
})

const VEHICLE_FIELDS = ['name', 'licensePlate', 'fuelType', 'units']

/** Today on this device's calendar (a schedule added without a last-done day starts today, as on the server). */
const localToday = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
const SCHEDULE_FIELDS = ['title', 'category', 'note', 'kind', 'intervalMonths', 'intervalDistance', 'lastDoneDate', 'lastDoneOdometer', 'warnDays', 'warnDistance']

/** A vehicle as the device knows it: the server's (details, or its card from the home list) with the changes waiting, or one added here. */
async function vehicleOf(rows: RowStore, id: unknown, base: Record<string, unknown> | undefined) {
  const add = addedVehicle(id)
  const fresh = add && {
    __typename: 'Vehicle', id, version: 0, licensePlate: null, pictureUrl: null, canEdit: true, logAccess: 'DELETE', refuelingCount: 0, owner: null,
    summary: null, recurring: [], units: { __typename: 'MeasurementUnits', distance: 'KILOMETERS', volume: 'LITERS' },
  }
  const start = fresh ?? base ?? (await rows.vehicles()).find((v) => v.id === id)
  if (!start) return undefined
  const updates = outbox.changes.filter((c) => c.entity === 'vehicles' && c.targetId === id && (c.action === 'update' || c.action === 'add'))
  return updates.reduce<Record<string, unknown>>((v, c) => over(v, c.input, VEHICLE_FIELDS), start)
}

/** The schedules of a vehicle with the changes waiting: an added one is upcoming (the server works out where it stands). */
function schedulesOf(vehicleId: string, base: readonly Record<string, unknown>[]) {
  const byId = new Map(base.map((s) => [s.id as string, s]))
  for (const change of outbox.changes.filter((c) => c.entity === 'recurring' && c.vehicleId === vehicleId)) {
    if (change.action === 'add')
      byId.set(change.targetId, over({
        __typename: 'RecurringExpenseInfo', id: change.targetId, version: 0, category: null, note: null, intervalMonths: null, intervalDistance: null,
        lastDoneOdometer: null, warnDays: 30, warnDistance: 500, lastDoneDate: localToday(),
        status: { __typename: 'RecurrenceStatusInfo', state: 'UPCOMING', limit: null, dueDate: null, dueOdometer: null, daysLeft: null, distanceLeft: null },
      }, change.input, SCHEDULE_FIELDS))
    else if (change.action === 'update' && byId.has(change.targetId)) byId.set(change.targetId, over(byId.get(change.targetId)!, change.input, SCHEDULE_FIELDS))
  }
  return [...byId.values()]
}

const vehicleDetails: Resolver = async (rows, variables, kept) => {
  if (!outbox.changes.some((c) => c.entity === 'vehicles' && c.targetId === variables.id)) return undefined
  const vehicle = await vehicleOf(rows, variables.id, (await kept('VehicleDetails', variables))?.vehicle as Record<string, unknown> | undefined)
  return vehicle && { vehicle }
}

const vehicleCard: Resolver = async (rows, variables, kept) => {
  if (!outbox.changes.some((c) => c.vehicleId === variables.id)) return undefined
  const base = (await kept('VehicleCard', variables))?.vehicle as Record<string, unknown> | undefined
  const vehicle = await vehicleOf(rows, variables.id, base)
  return vehicle && { vehicle: { ...vehicle, recurring: schedulesOf(String(variables.id), (vehicle.recurring as Record<string, unknown>[] | undefined) ?? []) } }
}

/** The home list: the last answer seen, with the vehicles' changes over it and the vehicles added here at the end of the first page. */
const welcome: Resolver = async (rows, variables, kept) => {
  const base = await kept('Welcome', variables)
  if (!base) return undefined
  const search = typeof variables.search === 'string' ? variables.search.trim().toLowerCase() : ''
  const listed = await Promise.all((base.myVehicles as Record<string, unknown>[]).map(async (v) => {
    const vehicle = (await vehicleOf(rows, v.id, v)) ?? v
    return { ...vehicle, recurring: schedulesOf(String(v.id), (v.recurring as Record<string, unknown>[] | undefined) ?? []) }
  }))
  const adds = Number(variables.skip ?? 0) > 0 ? [] : await Promise.all(
    outbox.changes
      .filter((c) => c.entity === 'vehicles' && c.action === 'add')
      .filter((c) => !search || String(c.input?.name ?? '').toLowerCase().includes(search) || String(c.input?.licensePlate ?? '').toLowerCase().includes(search))
      .map(async (c) => ({ ...(await vehicleOf(rows, c.targetId, undefined)), recurring: schedulesOf(c.targetId, []) })),
  )
  return { ...base, myVehicles: [...listed, ...adds], myVehicleCount: Number(base.myVehicleCount ?? 0) + adds.length, vehicleTotal: Number(base.vehicleTotal ?? 0) + adds.length }
}

const recurringExpenses: Resolver = async (_rows, variables, kept) => {
  const vehicleId = String(variables.vehicleId)
  if (!outbox.vehicleIds().has(vehicleId)) return undefined
  const base = (await kept('RecurringExpenses', variables))?.vehicle as Record<string, unknown> | undefined
  if (!base && !addedVehicle(vehicleId)) return undefined
  return { vehicle: { __typename: 'Vehicle', id: vehicleId, ...base, recurring: schedulesOf(vehicleId, (base?.recurring as Record<string, unknown>[] | undefined) ?? []) } }
}

/** A vehicle added here has no figures or charts until the server has it. */
const vehicleDashboard: Resolver = async (_rows, variables) =>
  addedVehicle(variables.id) ? { vehicle: { __typename: 'Vehicle', id: variables.id, summary: null }, vehicleCharts: [] } : undefined

const chartData: Resolver = async (_rows, variables) =>
  addedVehicle(variables.vehicleId) ? { vehicleChartData: { __typename: 'ChartData', unit: 'COUNT', series: [] } } : undefined

const RESOLVERS: Record<string, Resolver> = {
  Refuelings: list('refuelings', 'refuelings', 'refuelingCount'),
  Expenses: list('expenses', 'expenses', 'expenseCount'),
  RefuelingDetails: details('refuelings', 'refueling'),
  ExpenseDetails: details('expenses', 'expense'),
  RefuelingTrash: trash('refuelings', 'refuelingTrash', 'refuelingTrashCount', 'refuelingTrashDeletableCount'),
  ExpenseTrash: trash('expenses', 'expenseTrash', 'expenseTrashCount', 'expenseTrashDeletableCount'),
  LogDefaults: logDefaults,
  ExpenseCategories: expenseCategories,
  Welcome: welcome,
  VehicleCard: vehicleCard,
  VehicleDetails: vehicleDetails,
  RecurringExpenses: recurringExpenses,
  VehicleDashboard: vehicleDashboard,
  ChartData: chartData,
}

/** The device's own answer to a query, or undefined when it has none (not one of these queries, or not downloaded). */
export async function answerLocally(
  rows: RowStore | null, operationName: string | undefined, variables: Variables, kept: Kept = async () => undefined,
): Promise<Record<string, unknown> | undefined> {
  const resolver = RESOLVERS[operationName ?? '']
  if (!rows || !resolver) return undefined
  return resolver(rows, variables, kept)
}
