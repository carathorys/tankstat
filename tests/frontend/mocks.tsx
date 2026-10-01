import { ApolloProvider } from '@apollo/client/react'
import { Theme } from '@radix-ui/themes'
import { render } from '@testing-library/react'
import { graphql, HttpResponse } from 'msw'
import { MotionConfig } from 'motion/react'
import type { ReactElement } from 'react'
import { MemoryRouter } from 'react-router'
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
  ...over,
})

export const authWarning: Notice = {
  code: 'AUTH_DISABLED',
  severity: 'WARNING',
  message: 'Authentication is disabled: anyone can see and change all data.',
}

export const sessionHandler = (mode: AuthMode, current: () => SessionUser | null, notices: Notice[] = []) =>
  graphql.query('Session', () => HttpResponse.json({ data: { session: { mode, user: current() }, notices } }))

export const healthHandler = graphql.query('Health', () =>
  HttpResponse.json({ data: { health: { status: 'ok', version: '1.2.3', databaseReachable: true } } }),
)

/** A GraphQL error as the API sends it: coarse `code`, specific translation `key`, `args`, English fallback `message`. */
export const gqlError = (message: string, code: string, key?: string, args?: Record<string, unknown>) => ({
  errors: [{ message, extensions: { code, ...(key ? { key, args: args ?? {} } : {}) } }],
})

export interface FakeVehicle {
  id: string
  name: string
  licensePlate: string | null
  fuelType: 'PETROL' | 'DIESEL' | 'LPG'
  ownerName: string | null
  canEdit: boolean
  refuelingCount: number
}

export const fakeVehicle = (over: Partial<FakeVehicle> = {}): FakeVehicle => ({
  id: 'v1',
  name: 'Octavia',
  licensePlate: 'ABC-123',
  fuelType: 'DIESEL',
  ownerName: 'Alice',
  canEdit: true,
  refuelingCount: 2,
  ...over,
})

type Trashed = FakeVehicle & { deletedAt: string }

const sorters: Record<string, (v: Trashed) => string | number> = {
  NAME: (v) => v.name.toLowerCase(),
  LICENSE_PLATE: (v) => (v.licensePlate ?? '').toLowerCase(),
  FUEL_TYPE: (v) => v.fuelType,
  OWNER: (v) => (v.ownerName ?? '').toLowerCase(),
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
    requests: { Vehicles: [] as Record<string, unknown>[], Trash: [] as Record<string, unknown>[] },
    nextId: 100,
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
    graphql.query('Trash', ({ variables }) => {
      state.requests.Trash.push(variables)
      return HttpResponse.json({ data: { trash: page(state.trash, variables as unknown as GridVars), trashCount: state.trash.length } })
    }),
    graphql.query('VehicleDetails', ({ variables }) => {
      const v = state.vehicles.find((x) => x.id === variables.id)
      return HttpResponse.json({
        data: { vehicle: v ? { id: v.id, name: v.name, licensePlate: v.licensePlate, fuelType: v.fuelType } : null },
      })
    }),
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
