import { ApolloProvider } from '@apollo/client/react'
import { Theme } from '@radix-ui/themes'
import { render } from '@testing-library/react'
import { graphql, http, HttpResponse } from 'msw'
import { MotionConfig } from 'motion/react'
import type { ReactElement } from 'react'
import { MemoryRouter } from 'react-router'
import { vi } from 'vitest'
import { createApolloClient } from '../../src/frontend/apolloClient.ts'
import type { AuthMode, SessionQuery } from '../../src/frontend/gql/generated.ts'

/** Renders with a fresh Apollo client (and cache) talking to the msw-mocked GraphQL endpoint over HTTP; animations are instant. */
export const renderWithApollo = (ui: ReactElement, route = '/') =>
  render(
    <ApolloProvider client={createApolloClient('http://localhost/graphql')}>
      <Theme>
        <MotionConfig transition={{ duration: 0 }}>
          <MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>
        </MotionConfig>
      </Theme>
    </ApolloProvider>,
  )

type SessionUser = NonNullable<SessionQuery['session']['user']>
type Notice = SessionQuery['notices'][number]

export const user = (over: Partial<SessionUser> = {}): SessionUser => ({
  id: 'u1',
  displayName: 'Alice',
  email: 'alice@example.com',
  isAdmin: false,
  avatarUrl: null,
  ...over,
})

export const authWarning: Notice = {
  code: 'AUTH_DISABLED',
  severity: 'WARNING',
  message: 'Authentication is disabled: anyone can see and change all data.',
}

export const sessionHandler = (mode: AuthMode, current: () => SessionUser | null, notices: Notice[] = []) =>
  graphql.query('Session', () => HttpResponse.json({ data: { session: { mode, user: current() }, notices } }))

/** A signed-in administrator: the full vehicle list and the administration are for administrators only. */
export const adminSession = (notices: Notice[] = []) => sessionHandler('STANDALONE', () => user({ isAdmin: true }), notices)

export const healthHandler = graphql.query('Health', () =>
  HttpResponse.json({ data: { health: { status: 'ok', version: '1.2.3', databaseReachable: true } } }),
)

/** A GraphQL error as the API sends it: coarse `code`, specific translation `key`, `args`, English fallback `message`. */
export const gqlError = (message: string, code: string, key?: string, args?: Record<string, unknown>) => ({
  errors: [{ message, extensions: { code, ...(key ? { key, args: args ?? {} } : {}) } }],
})

type Person = { id: string; displayName: string; avatarUrl: string | null }

export interface FakeSummary {
  lastFillUpDate: string | null
  latestOdometer: number | null
  averageConsumption: number | null
  currency: string | null
  thisMonthSpend: number
  lastMonthSpend: number
  fillUpCount: number
  expenseCount: number
  spendTrend: { month: string; amount: number }[]
}

export const fakeSummary = (over: Partial<FakeSummary> = {}): FakeSummary => ({
  lastFillUpDate: '2026-09-17',
  latestOdometer: 12000,
  averageConsumption: 6.5,
  currency: 'HUF',
  thisMonthSpend: 50000,
  lastMonthSpend: 40000,
  fillUpCount: 5,
  expenseCount: 2,
  spendTrend: ['2026-05', '2026-06', '2026-07', '2026-08', '2026-09', '2026-10'].map((month, i) => ({ month, amount: 10000 * i })),
  ...over,
})

export interface FakeVehicle {
  id: string
  name: string
  licensePlate: string | null
  fuelType: 'PETROL' | 'DIESEL' | 'LPG'
  owner: Person | null
  canEdit: boolean
  logAccess: 'NONE' | 'VIEW' | 'EDIT' | 'DELETE'
  pictureUrl: string | null
  summary: FakeSummary
  units: { distance: 'KILOMETERS' | 'MILES'; volume: 'LITERS' | 'US_GALLONS' | 'IMPERIAL_GALLONS' }
  refuelingCount: number
  recurring: FakeRecurring[]
}

export const person = (displayName: string, over: Partial<Person> = {}): Person => ({ id: `p-${displayName}`, displayName, avatarUrl: null, ...over })

/** `ownerName` is a shortcut for a person without a picture. */
export const fakeVehicle = (over: Partial<FakeVehicle> & { ownerName?: string | null } = {}): FakeVehicle => {
  const { ownerName, ...rest } = over
  return {
    id: 'v1',
    name: 'Octavia',
    licensePlate: 'ABC-123',
    fuelType: 'DIESEL',
    owner: ownerName === null ? null : person(ownerName ?? 'Alice'),
    canEdit: true,
    logAccess: 'DELETE',
    pictureUrl: null,
    summary: fakeSummary(),
    units: { distance: 'KILOMETERS', volume: 'LITERS' },
    refuelingCount: 2,
    recurring: [],
    ...rest,
  }
}

type Trashed = FakeVehicle & { deletedAt: string }

const sorters: Record<string, (v: Trashed) => string | number> = {
  NAME: (v) => v.name.toLowerCase(),
  LICENSE_PLATE: (v) => (v.licensePlate ?? '').toLowerCase(),
  FUEL_TYPE: (v) => v.fuelType,
  OWNER: (v) => (v.owner?.displayName ?? '').toLowerCase(),
  REFUELING_COUNT: (v) => v.refuelingCount,
  DELETED_AT: (v) => v.deletedAt,
}

/** Emulates what the real server does with the grid arguments: sorting and paging happen here, not in the UI. */
type GridVars = { orderBy: string; direction: string; skip: number; take: number }

function page<T extends Trashed>(rows: T[], vars: GridVars) {
  const key = sorters[vars.orderBy]
  const sorted = [...rows].sort((a, b) => (key(a) < key(b) ? -1 : key(a) > key(b) ? 1 : 0))
  if (vars.direction === 'DESC') sorted.reverse()
  return sorted.slice(vars.skip, vars.skip + vars.take)
}

/** A small in-memory backend for vehicles and trash, so mutations show up in the refetched lists. */
export function fakeVehicleBackend(initial: FakeVehicle[] = [], trashed: FakeVehicle[] = []) {
  const state = {
    vehicles: initial.map((v) => ({ ...v, deletedAt: '' })) as Trashed[],
    trash: trashed.map((v) => ({ ...v, deletedAt: '2026-10-01T08:00:00Z' })) as Trashed[],
    calls: {} as Record<string, unknown[]>,
    /** Variables of every Vehicles / Trash query the UI sent. */
    requests: { Vehicles: [] as Record<string, unknown>[], Trash: [] as Record<string, unknown>[], Welcome: [] as Record<string, unknown>[] },
    nextId: 100,
    /** How many trashed vehicles this user may delete for good (defaults to all of them). */
    trashDeletable: undefined as number | undefined,
    failWith: undefined as { message: string; key?: string; args?: Record<string, unknown> } | undefined,
  }
  const record = (name: string, vars: unknown) => (state.calls[name] ??= []).push(vars)

  const handlers = [
    graphql.query('Vehicles', ({ variables }) => {
      state.requests.Vehicles.push(variables)
      return HttpResponse.json({
        data: { vehicles: page(state.vehicles, variables as unknown as GridVars), vehicleCount: state.vehicles.length },
      })
    }),
    graphql.query('Welcome', ({ variables }) => {
      state.requests.Welcome.push(variables)
      const term = String(variables.search ?? '').toLowerCase()
      const found = state.vehicles
        .filter((v) => !term || v.name.toLowerCase().includes(term) || (v.licensePlate ?? '').toLowerCase().includes(term))
        .sort((a, b) => a.name.localeCompare(b.name))
      const skip = Number(variables.skip ?? 0)
      return HttpResponse.json({ data: { myVehicles: found.slice(skip, skip + Number(variables.take ?? 50)), myVehicleCount: found.length } })
    }),
    graphql.query('ImportTargets', () => HttpResponse.json({ data: { myVehicles: state.vehicles } })),
    graphql.query('Trash', ({ variables }) => {
      state.requests.Trash.push(variables)
      return HttpResponse.json({
        data: { trash: page(state.trash, variables as unknown as GridVars), trashCount: state.trash.length, trashDeletableCount: state.trashDeletable ?? state.trash.length },
      })
    }),
    graphql.query('VehicleDetails', ({ variables }) => {
      const v = state.vehicles.find((x) => x.id === variables.id)
      return HttpResponse.json({
        data: { vehicle: v ?? null },
      })
    }),
    graphql.query('VehicleDefaults', () =>
      HttpResponse.json({ data: { vehicleDefaults: { distanceUnit: 'KILOMETERS', volumeUnit: 'LITERS', currency: 'HUF', recurringWarnDays: 30, recurringWarnDistance: 500 } } }),
    ),
    graphql.mutation('AddVehicle', ({ variables }) => {
      record('AddVehicle', variables)
      if (state.failWith) {
        return HttpResponse.json(gqlError(state.failWith.message, 'VALIDATION_FAILED', state.failWith.key, state.failWith.args))
      }
      const id = `v${state.nextId++}`
      state.vehicles.push({ ...fakeVehicle({ id, refuelingCount: 0, ...variables.input }), deletedAt: '' })
      return HttpResponse.json({ data: { addVehicle: { id } } })
    }),
    graphql.mutation('UpdateVehicle', ({ variables }) => {
      record('UpdateVehicle', variables)
      if (state.failWith) {
        return HttpResponse.json(gqlError(state.failWith.message, 'VALIDATION_FAILED', state.failWith.key, state.failWith.args))
      }
      const i = state.vehicles.findIndex((v) => v.id === variables.input.id)
      state.vehicles[i] = { ...state.vehicles[i], ...variables.input }
      return HttpResponse.json({ data: { updateVehicle: { id: variables.input.id } } })
    }),
    graphql.mutation('DeleteVehicle', ({ variables }) => {
      record('DeleteVehicle', variables)
      const v = state.vehicles.find((x) => x.id === variables.id)!
      state.vehicles = state.vehicles.filter((x) => x.id !== variables.id)
      state.trash.push({ ...v, deletedAt: '2026-10-02T09:30:00Z' })
      return HttpResponse.json({ data: { deleteVehicle: { id: variables.id } } })
    }),
    graphql.mutation('RestoreVehicle', ({ variables }) => {
      record('RestoreVehicle', variables)
      const t = state.trash.find((x) => x.id === variables.id)!
      state.trash = state.trash.filter((x) => x.id !== variables.id)
      state.vehicles.push({ ...t, deletedAt: '' })
      return HttpResponse.json({ data: { restoreVehicle: { id: variables.id } } })
    }),
    graphql.mutation('EmptyTrash', () => {
      record('EmptyTrash', {})
      const removed = state.trash.length
      state.trash = []
      return HttpResponse.json({ data: { emptyTrash: removed } })
    }),
  ]
  return { state, handlers }
}

/** Pretends to be a phone (narrow) or a desktop (wide) browser window for CSS media queries. */
export function stubViewport(kind: 'phone' | 'desktop') {
  vi.stubGlobal('matchMedia', (query: string) => ({
    // A phone is narrow and has no hover (a touch screen); a desktop is wide and has a mouse.
    matches: kind === 'phone' ? query.includes('max-width: 767px') || query.includes('hover: none') : query.includes('min-width: 1024px'),
    media: query,
    onchange: null,
    addEventListener() {},
    removeEventListener() {},
    addListener() {},
    removeListener() {},
    dispatchEvent: () => false,
  }))
}

export interface FakeRefueling {
  id: string
  vehicleId: string
  date: string
  volume: number
  totalCost: number
  currency: string
  odometer: number
  isFullTank: boolean
  note: string | null
  /** Fuel per 100 distance units between this and the previous full fill-up (the server calculates and stores it). */
  consumption: number | null
  canEdit: boolean
  canDelete: boolean
  createdBy: Person
  deletedAt?: string
}

export const fakeRefueling = (over: Partial<FakeRefueling> = {}): FakeRefueling => ({
  id: 'r1',
  vehicleId: 'v1',
  date: '2026-09-01',
  volume: 40.5,
  totalCost: 20250,
  currency: 'HUF',
  odometer: 12000,
  isFullTank: true,
  note: null,
  consumption: null,
  canEdit: true,
  canDelete: false,
  createdBy: person('Alice'),
  ...over,
})

const logSorters: Record<string, (r: FakeRefueling) => string | number> = {
  DATE: (r) => r.date,
  VOLUME: (r) => r.volume,
  TOTAL_COST: (r) => r.totalCost,
  ODOMETER: (r) => r.odometer,
  PRICE_PER_UNIT: (r) => r.totalCost / r.volume,
  CONSUMPTION: (r) => r.consumption ?? 0,
  CREATED_BY: (r) => r.createdBy.displayName.toLowerCase(),
  VEHICLE: (r) => r.vehicleId,
  DELETED_AT: (r) => r.deletedAt ?? '',
}

export interface FakePhoto {
  id: string
  url: string
}

/**
 * In-memory photos of logs behind the upload endpoints (`PUT/DELETE /media/<kind>/<logId>/photos`), as the real API answers them.
 * `failWith` makes uploads fail with a stable error key, `put` records the requests as "<logId>:<bytes>".
 */
export function fakePhotoStore(initial: Record<string, FakePhoto[]> = {}) {
  const state = {
    byLog: Object.fromEntries(Object.entries(initial).map(([k, v]) => [k, [...v]])) as Record<string, FakePhoto[]>,
    puts: [] as { kind: string; logId: string; bytes: number }[],
    deletes: [] as { kind: string; logId: string; imageId: string }[],
    failWith: undefined as { key: string; status?: number; args?: Record<string, unknown> } | undefined,
    nextId: 1,
  }
  const handlers = (['expenses', 'refuelings'] as const).flatMap((kind) => [
    http.put(`/media/${kind}/:logId/photos`, async ({ params, request }) => {
      const logId = String(params.logId)
      state.puts.push({ kind, logId, bytes: (await request.arrayBuffer()).byteLength })
      if (state.failWith) return HttpResponse.json({ key: state.failWith.key, args: state.failWith.args ?? {}, message: 'refused' }, { status: state.failWith.status ?? 400 })
      const id = `img${state.nextId++}`
      ;(state.byLog[logId] ??= []).push({ id, url: `/media/${id}` })
      return HttpResponse.json({ id, url: `/media/${id}` })
    }),
    http.delete(`/media/${kind}/:logId/photos/:imageId`, ({ params }) => {
      const logId = String(params.logId)
      state.deletes.push({ kind, logId, imageId: String(params.imageId) })
      state.byLog[logId] = (state.byLog[logId] ?? []).filter((p) => p.id !== params.imageId)
      return new HttpResponse(null, { status: 204 })
    }),
  ])
  return { state, handlers, photosOf: (logId: string) => state.byLog[logId] ?? [] }
}

/** A small in-memory backend for one vehicle's logs (and their trash), including the sharing list. */
export function fakeLogBackend(vehicle: FakeVehicle, logs: FakeRefueling[] = [], photos = fakePhotoStore()) {
  const state = {
    vehicle,
    logs: [...logs],
    trash: [] as FakeRefueling[],
    calls: {} as Record<string, unknown[]>,
    requests: [] as Record<string, unknown>[],
    grants: [] as { user: Person; level: 'EDIT' | 'DELETE' }[],
    candidates: [person('Bob'), person('Carol')],
    failWith: undefined as { message: string; key: string; args?: Record<string, unknown> } | undefined,
    lastOdometer: logs.length ? Math.max(...logs.map((l) => l.odometer)) : null,
    nextId: 200,
  }
  const record = (name: string, vars: unknown) => (state.calls[name] ??= []).push(vars)
  const fail = (): Response | undefined => (state.failWith ? HttpResponse.json(gqlError(state.failWith.message, 'VALIDATION_FAILED', state.failWith.key, state.failWith.args)) : undefined)
  const rows = (list: FakeRefueling[], vars: GridVars) => {
    const key = logSorters[vars.orderBy]
    const sorted = [...list].sort((a, b) => (key(a) < key(b) ? -1 : key(a) > key(b) ? 1 : 0))
    if (vars.direction === 'DESC') sorted.reverse()
    return sorted.slice(vars.skip, vars.skip + vars.take).map((r) => ({
      ...r,
      pricePerUnit: r.totalCost / r.volume,
      vehicle: { id: state.vehicle.id, name: state.vehicle.name, units: state.vehicle.units },
    }))
  }

  const handlers = [
    graphql.query('VehicleDetails', ({ variables }) =>
      HttpResponse.json({ data: { vehicle: variables.id === state.vehicle.id ? { ...state.vehicle, refuelingCount: state.logs.length } : null } }),
    ),
    graphql.query('Refuelings', ({ variables }) => {
      state.requests.push(variables)
      return HttpResponse.json({ data: { refuelings: rows(state.logs, variables as unknown as GridVars), refuelingCount: state.logs.length } })
    }),
    graphql.query('RefuelingTrash', ({ variables }) =>
      HttpResponse.json({
        data: { refuelingTrash: rows(state.trash, variables as unknown as GridVars), refuelingTrashCount: state.trash.length, refuelingTrashDeletableCount: state.trash.length },
      }),
    ),
    graphql.query('RefuelingDetails', ({ variables }) => {
      const r = state.logs.find((x) => x.id === variables.id)
      return HttpResponse.json({ data: { refueling: r ? { ...r, photos: photos.photosOf(r.id) } : null } })
    }),
    graphql.query('LogDefaults', () =>
      HttpResponse.json({ data: { logDefaults: { lastOdometer: state.lastOdometer, lastDate: state.lastOdometer ? '2026-09-01' : null, currency: 'HUF' } } }),
    ),
    graphql.mutation('LogRefueling', ({ variables }) => {
      record('LogRefueling', variables)
      const failure = fail()
      if (failure) return failure
      const id = `r${state.nextId++}`
      state.logs.push(fakeRefueling({ id, vehicleId: variables.input.vehicleId, ...variables.input }))
      state.lastOdometer = Math.max(state.lastOdometer ?? 0, variables.input.odometer)
      return HttpResponse.json({ data: { logRefueling: { id } } })
    }),
    graphql.mutation('UpdateRefueling', ({ variables }) => {
      record('UpdateRefueling', variables)
      const failure = fail()
      if (failure) return failure
      const i = state.logs.findIndex((l) => l.id === variables.input.id)
      state.logs[i] = { ...state.logs[i], ...variables.input }
      return HttpResponse.json({ data: { updateRefueling: { id: variables.input.id } } })
    }),
    graphql.mutation('DeleteRefueling', ({ variables }) => {
      record('DeleteRefueling', variables)
      const log = state.logs.find((l) => l.id === variables.id)!
      state.logs = state.logs.filter((l) => l.id !== variables.id)
      state.trash.push({ ...log, deletedAt: '2026-10-02T09:30:00Z' })
      return HttpResponse.json({ data: { deleteRefueling: { id: variables.id } } })
    }),
    graphql.mutation('RestoreRefueling', ({ variables }) => {
      record('RestoreRefueling', variables)
      const log = state.trash.find((l) => l.id === variables.id)!
      state.trash = state.trash.filter((l) => l.id !== variables.id)
      state.logs.push(log)
      return HttpResponse.json({ data: { restoreRefueling: { id: variables.id } } })
    }),
    graphql.mutation('EmptyRefuelingTrash', () => {
      record('EmptyRefuelingTrash', {})
      const removed = state.trash.length
      state.trash = []
      return HttpResponse.json({ data: { emptyRefuelingTrash: removed } })
    }),
    graphql.query('LogAccess', () =>
      HttpResponse.json({
        data: {
          vehicleLogAccess: state.grants.map((g) => ({ level: g.level, user: g.user })),
          shareCandidates: state.candidates.filter((c) => !state.grants.some((g) => g.user.id === c.id)),
        },
      }),
    ),
    graphql.mutation('SetVehicleLogAccess', ({ variables }) => {
      record('SetVehicleLogAccess', variables)
      const { userId, level } = variables.input
      state.grants = state.grants.filter((g) => g.user.id !== userId)
      if (level !== 'NONE') state.grants.push({ user: state.candidates.find((c) => c.id === userId)!, level })
      return HttpResponse.json({ data: { setVehicleLogAccess: true } })
    }),
  ]
  return { state, handlers: [...handlers, ...photos.handlers], photos }
}

export interface FakeExpense {
  id: string
  vehicleId: string
  date: string
  title: string
  category: string | null
  amount: number
  currency: string
  odometer: number | null
  note: string | null
  canEdit: boolean
  canDelete: boolean
  createdBy: Person
  deletedAt?: string
}

export const fakeExpense = (over: Partial<FakeExpense> = {}): FakeExpense => ({
  id: 'e1',
  vehicleId: 'v1',
  date: '2026-09-01',
  title: 'Oil change',
  category: 'Service',
  amount: 35000,
  currency: 'HUF',
  odometer: 12000,
  note: null,
  canEdit: true,
  canDelete: false,
  createdBy: person('Alice'),
  ...over,
})

const expenseSorters: Record<string, (e: FakeExpense) => string | number> = {
  DATE: (e) => e.date,
  TITLE: (e) => e.title.toLowerCase(),
  CATEGORY: (e) => (e.category ?? '').toLowerCase(),
  AMOUNT: (e) => e.amount,
  ODOMETER: (e) => e.odometer ?? 0,
  CREATED_BY: (e) => e.createdBy.displayName.toLowerCase(),
  VEHICLE: (e) => e.vehicleId,
  DELETED_AT: (e) => e.deletedAt ?? '',
}

/** A small in-memory backend for one vehicle's expenses (and their trash). */
export function fakeExpenseBackend(vehicle: FakeVehicle, expenses: FakeExpense[] = [], photos = fakePhotoStore()) {
  const state = {
    vehicle,
    expenses: [...expenses],
    trash: [] as FakeExpense[],
    calls: {} as Record<string, unknown[]>,
    requests: [] as Record<string, unknown>[],
    failWith: undefined as { message: string; key: string; args?: Record<string, unknown> } | undefined,
    nextId: 300,
  }
  const record = (name: string, vars: unknown) => (state.calls[name] ??= []).push(vars)
  const fail = (): Response | undefined => (state.failWith ? HttpResponse.json(gqlError(state.failWith.message, 'VALIDATION_FAILED', state.failWith.key, state.failWith.args)) : undefined)
  const rows = (list: FakeExpense[], vars: GridVars) => {
    const key = expenseSorters[vars.orderBy]
    const sorted = [...list].sort((a, b) => (key(a) < key(b) ? -1 : key(a) > key(b) ? 1 : 0))
    if (vars.direction === 'DESC') sorted.reverse()
    return sorted.slice(vars.skip, vars.skip + vars.take).map((e) => ({ ...e, vehicle: { id: state.vehicle.id, name: state.vehicle.name } }))
  }

  const handlers = [
    graphql.query('VehicleDetails', ({ variables }) =>
      HttpResponse.json({ data: { vehicle: variables.id === state.vehicle.id ? { ...state.vehicle, refuelingCount: 0 } : null } }),
    ),
    graphql.query('Expenses', ({ variables }) => {
      state.requests.push(variables)
      return HttpResponse.json({ data: { expenses: rows(state.expenses, variables as unknown as GridVars), expenseCount: state.expenses.length } })
    }),
    graphql.query('ExpenseTrash', ({ variables }) =>
      HttpResponse.json({ data: { expenseTrash: rows(state.trash, variables as unknown as GridVars), expenseTrashCount: state.trash.length, expenseTrashDeletableCount: state.trash.length } }),
    ),
    graphql.query('ExpenseDetails', ({ variables }) => {
      const e = state.expenses.find((x) => x.id === variables.id)
      return HttpResponse.json({ data: { expense: e ? { ...e, photos: photos.photosOf(e.id) } : null } })
    }),
    graphql.query('ExpenseCategories', () =>
      HttpResponse.json({ data: { expenseCategories: [...new Set(state.expenses.map((e) => e.category).filter((c): c is string => c !== null))].sort() } }),
    ),
    graphql.query('LogDefaults', () => HttpResponse.json({ data: { logDefaults: { lastOdometer: null, lastDate: null, currency: 'HUF' } } })),
    graphql.mutation('AddExpense', ({ variables }) => {
      record('AddExpense', variables)
      const failure = fail()
      if (failure) return failure
      const id = `e${state.nextId++}`
      state.expenses.push(fakeExpense({ id, vehicleId: variables.input.vehicleId, ...variables.input }))
      return HttpResponse.json({ data: { addExpense: { id } } })
    }),
    graphql.mutation('UpdateExpense', ({ variables }) => {
      record('UpdateExpense', variables)
      const i = state.expenses.findIndex((e) => e.id === variables.input.id)
      state.expenses[i] = { ...state.expenses[i], ...variables.input }
      return HttpResponse.json({ data: { updateExpense: { id: variables.input.id } } })
    }),
    graphql.mutation('DeleteExpense', ({ variables }) => {
      record('DeleteExpense', variables)
      const e = state.expenses.find((x) => x.id === variables.id)!
      state.expenses = state.expenses.filter((x) => x.id !== variables.id)
      state.trash.push({ ...e, deletedAt: '2026-10-02T09:30:00Z' })
      return HttpResponse.json({ data: { deleteExpense: { id: variables.id } } })
    }),
    graphql.mutation('RestoreExpense', ({ variables }) => {
      record('RestoreExpense', variables)
      const e = state.trash.find((x) => x.id === variables.id)!
      state.trash = state.trash.filter((x) => x.id !== variables.id)
      state.expenses.push(e)
      return HttpResponse.json({ data: { restoreExpense: { id: variables.id } } })
    }),
    graphql.mutation('EmptyExpenseTrash', () => {
      record('EmptyExpenseTrash', {})
      const removed = state.trash.length
      state.trash = []
      return HttpResponse.json({ data: { emptyExpenseTrash: removed } })
    }),
  ]
  return { state, handlers: [...handlers, ...photos.handlers], photos }
}

export interface FakeChart {
  id: string
  title: string
  metric: string
  grouping: string
  kind: string
  range: string
  rangeFrom: string | null
  rangeTo: string | null
  stacked: boolean
  isShared: boolean
  canEdit: boolean
  createdBy: Person
}

export const fakeChart = (over: Partial<FakeChart> = {}): FakeChart => ({
  id: 'c1',
  title: 'My chart',
  metric: 'FUEL_COST',
  grouping: 'MONTH',
  kind: 'BAR',
  range: 'LAST6_MONTHS',
  rangeFrom: null,
  rangeTo: null,
  stacked: false,
  isShared: false,
  canEdit: true,
  createdBy: person('Alice'),
  ...over,
})

/** A small in-memory backend for a vehicle's dashboard: key figures, saved charts and chart data (a fixed answer per unit). */
export function fakeDashboardBackend(vehicle: FakeVehicle, charts: FakeChart[] = []) {
  const state = {
    vehicle,
    charts: [...charts],
    calls: {} as Record<string, unknown[]>,
    chartRequests: [] as { vehicleId: string; config: Record<string, unknown> }[],
    failWith: undefined as { message: string; key: string; args?: Record<string, unknown> } | undefined,
    nextId: 400,
  }
  const record = (name: string, vars: unknown) => (state.calls[name] ??= []).push(vars)
  const months = ['2026-08', '2026-09', '2026-10']
  const answer = (config: Record<string, unknown>) => {
    const metric = String(config.metric)
    const unit = metric === 'FUEL_VOLUME' ? 'VOLUME' : metric === 'DISTANCE' ? 'DISTANCE' : metric === 'AVERAGE_CONSUMPTION' ? 'CONSUMPTION' : metric === 'FILL_UPS' ? 'COUNT' : 'CURRENCY'
    const cost = unit === 'CURRENCY'
    const keys = config.grouping === 'CATEGORY' ? ['Service', 'Parking', ''] : months
    const kinds = config.stacked ? ['fuel', 'expenses'] : [config.metric === 'EXPENSE_COST' ? 'expenses' : cost ? 'total' : 'fuel']
    return {
      unit,
      series: kinds.map((kind, k) => ({ kind, currency: cost ? 'HUF' : null, points: keys.map((key, i) => ({ key, value: (i + 1) * 1000 * (k + 1) })) })),
    }
  }

  const handlers = [
    graphql.query('VehicleDashboard', ({ variables }) =>
      HttpResponse.json({ data: { vehicle: variables.id === state.vehicle.id ? { id: state.vehicle.id, summary: state.vehicle.summary } : null, vehicleCharts: state.charts } }),
    ),
    graphql.query('ChartData', ({ variables }) => {
      state.chartRequests.push(variables as never)
      return HttpResponse.json({ data: { vehicleChartData: answer(variables.config as Record<string, unknown>) } })
    }),
    graphql.mutation('SaveChart', ({ variables }) => {
      record('SaveChart', variables)
      if (state.failWith) return HttpResponse.json(gqlError(state.failWith.message, 'VALIDATION_FAILED', state.failWith.key, state.failWith.args))
      const { id, title, shared, config } = variables.input as { id: string | null; title: string; shared: boolean; config: Record<string, unknown> }
      const entry = fakeChart({ id: id ?? `c${state.nextId++}`, title, isShared: shared, metric: String(config.metric), grouping: String(config.grouping), kind: String(config.kind), range: String(config.range), stacked: Boolean(config.stacked), rangeFrom: (config.from as string) ?? null, rangeTo: (config.to as string) ?? null })
      state.charts = id ? state.charts.map((c) => (c.id === id ? entry : c)) : [...state.charts, entry]
      return HttpResponse.json({ data: { saveVehicleChart: { id: entry.id } } })
    }),
    graphql.mutation('DeleteChart', ({ variables }) => {
      record('DeleteChart', variables)
      state.charts = state.charts.filter((c) => c.id !== variables.id)
      return HttpResponse.json({ data: { deleteVehicleChart: true } })
    }),
  ]
  return { state, handlers }
}

export interface FakeRecurring {
  /** The fragment on RecurringExpenseInfo only matches when the type is named. */
  __typename: 'RecurringExpenseInfo'
  id: string
  title: string
  category: string | null
  note: string | null
  kind: 'TIME' | 'ODOMETER' | 'COMBINED'
  intervalMonths: number | null
  intervalDistance: number | null
  lastDoneDate: string
  lastDoneOdometer: number | null
  warnDays: number
  warnDistance: number
  status: {
    state: 'UPCOMING' | 'DUE_SOON' | 'OVERDUE'
    limit: 'TIME' | 'ODOMETER' | null
    dueDate: string | null
    dueOdometer: number | null
    daysLeft: number | null
    distanceLeft: number | null
  }
}

/** A combined (12 months or 15,000 km) schedule that is upcoming, unless the test says otherwise. */
export const fakeRecurring = (over: Partial<FakeRecurring> = {}): FakeRecurring => ({
  __typename: 'RecurringExpenseInfo',
  id: 'rc1',
  title: 'Oil change',
  category: 'Service',
  note: null,
  kind: 'COMBINED',
  intervalMonths: 12,
  intervalDistance: 15000,
  lastDoneDate: '2026-01-15',
  lastDoneOdometer: 50000,
  warnDays: 30,
  warnDistance: 500,
  status: { state: 'UPCOMING', limit: 'TIME', dueDate: '2027-01-15', dueOdometer: 65000, daysLeft: 106, distanceLeft: 9000 },
  ...over,
})

/** In-memory recurring expenses of one vehicle behind the Recurring* queries and mutations; a finished item is upcoming again. */
export function fakeRecurringBackend(items: FakeRecurring[] = []) {
  const state = {
    items: [...items],
    calls: {} as Record<string, unknown[]>,
    failWith: undefined as { message: string; key: string; args?: Record<string, unknown> } | undefined,
    nextId: 500,
    /** The instance's default warnings a new schedule starts with. */
    warnDefaults: { recurringWarnDays: 30, recurringWarnDistance: 500 },
  }
  const record = (name: string, vars: unknown) => (state.calls[name] ??= []).push(vars)
  const fail = (): Response | undefined => (state.failWith ? HttpResponse.json(gqlError(state.failWith.message, 'VALIDATION_FAILED', state.failWith.key, state.failWith.args)) : undefined)
  const upcoming = (): FakeRecurring['status'] => ({ state: 'UPCOMING', limit: 'TIME', dueDate: '2027-12-01', dueOdometer: null, daysLeft: 400, distanceLeft: null })
  const handlers = [
    graphql.query('RecurringExpenses', ({ variables }) => HttpResponse.json({ data: { vehicle: { __typename: 'Vehicle', id: variables.vehicleId, recurring: state.items } } })),
    graphql.query('VehicleDefaults', () =>
      HttpResponse.json({ data: { vehicleDefaults: { distanceUnit: 'KILOMETERS', volumeUnit: 'LITERS', currency: 'HUF', ...state.warnDefaults } } }),
    ),
    graphql.mutation('AddRecurringExpense', ({ variables }) => {
      record('AddRecurringExpense', variables)
      const failed = fail()
      if (failed) return failed
      const input = variables.input as Partial<FakeRecurring>
      const item = fakeRecurring({ ...input, id: `rc${state.nextId++}`, status: upcoming() })
      state.items.push(item)
      return HttpResponse.json({ data: { addRecurringExpense: item } })
    }),
    graphql.mutation('UpdateRecurringExpense', ({ variables }) => {
      record('UpdateRecurringExpense', variables)
      const failed = fail()
      if (failed) return failed
      const input = variables.input as Partial<FakeRecurring> & { id: string }
      const i = state.items.findIndex((x) => x.id === input.id)
      state.items[i] = { ...state.items[i], ...input }
      return HttpResponse.json({ data: { updateRecurringExpense: state.items[i] } })
    }),
    graphql.mutation('DeleteRecurringExpense', ({ variables }) => {
      record('DeleteRecurringExpense', variables)
      state.items = state.items.filter((x) => x.id !== variables.id)
      return HttpResponse.json({ data: { deleteRecurringExpense: true } })
    }),
    graphql.mutation('MarkRecurringExpenseDone', ({ variables }) => {
      record('MarkRecurringExpenseDone', variables)
      const failed = fail()
      if (failed) return failed
      const input = variables.input as { id: string; date: string; odometer: number | null }
      const i = state.items.findIndex((x) => x.id === input.id)
      state.items[i] = { ...state.items[i], lastDoneDate: input.date, lastDoneOdometer: input.odometer ?? state.items[i].lastDoneOdometer, status: upcoming() }
      return HttpResponse.json({ data: { markRecurringExpenseDone: state.items[i] } })
    }),
  ]
  return { state, handlers }
}
