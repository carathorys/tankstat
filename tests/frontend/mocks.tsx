import { ApolloProvider } from '@apollo/client/react'
import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { graphql, HttpResponse } from 'msw'
import type { ReactElement } from 'react'
import { createApolloClient } from '../../src/frontend/apolloClient.ts'
import type { AuthMode, Notice, SessionUser } from '../../src/frontend/session.ts'

/** Renders with a fresh Apollo client (and cache) talking to the msw-mocked GraphQL endpoint over HTTP. */
export const renderWithApollo = (ui: ReactElement, route = '/') =>
  render(
    <ApolloProvider client={createApolloClient('http://localhost/graphql')}>
      <MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>
    </ApolloProvider>,
  )

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

export const vehiclesHandler = (vehicles: unknown[] = []) =>
  graphql.query('Vehicles', () => HttpResponse.json({ data: { vehicles } }))

export const trashHandler = (trash: unknown[] = []) => graphql.query('Trash', () => HttpResponse.json({ data: { trash } }))

export interface FakeVehicle {
  id: string
  name: string
  licensePlate: string | null
  fuelType: 'PETROL' | 'DIESEL' | 'LPG'
  ownerName: string | null
  canEdit: boolean
}

export const fakeVehicle = (over: Partial<FakeVehicle> = {}): FakeVehicle => ({
  id: 'v1',
  name: 'Octavia',
  licensePlate: 'ABC-123',
  fuelType: 'DIESEL',
  ownerName: 'Alice',
  canEdit: true,
  ...over,
})

/** A small in-memory backend for vehicles and trash, so mutations show up in the refetched lists. */
export function fakeVehicleBackend(initial: FakeVehicle[] = [], trashed: FakeVehicle[] = []) {
  const state = {
    vehicles: [...initial],
    trash: trashed.map((v) => ({ ...v, deletedAt: '2026-10-01T08:00:00Z' })),
    calls: {} as Record<string, unknown[]>,
    nextId: 100,
    failWith: undefined as string | undefined,
  }
  const record = (name: string, vars: unknown) => (state.calls[name] ??= []).push(vars)

  const handlers = [
    graphql.query('Vehicles', () =>
      HttpResponse.json({ data: { vehicles: state.vehicles.map((v) => ({ ...v, refuelings: [] })) } }),
    ),
    graphql.query('Trash', () => HttpResponse.json({ data: { trash: state.trash } })),
    graphql.mutation('AddVehicle', ({ variables }) => {
      record('AddVehicle', variables)
      if (state.failWith) return HttpResponse.json(gqlError(state.failWith, 'VALIDATION_FAILED'))
      const id = `v${state.nextId++}`
      state.vehicles.push(fakeVehicle({ id, ...variables.input }))
      return HttpResponse.json({ data: { addVehicle: { id } } })
    }),
    graphql.mutation('UpdateVehicle', ({ variables }) => {
      record('UpdateVehicle', variables)
      if (state.failWith) return HttpResponse.json(gqlError(state.failWith, 'VALIDATION_FAILED'))
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
      state.vehicles.push(fakeVehicle(t))
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

export const gqlError = (message: string, code: string) => ({ errors: [{ message, extensions: { code } }] })
