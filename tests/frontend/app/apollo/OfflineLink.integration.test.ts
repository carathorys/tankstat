import { ApolloClient, ApolloLink, HttpLink, InMemoryCache } from '@apollo/client'
import { parse } from 'graphql'
import { graphql, http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import { createApolloClient } from '../../../../src/frontend/apolloClient.ts'
import { AdminDocument, HealthDocument, LoginDocument, RefuelingDetailsDocument, SessionDocument, VehicleDefaultsDocument } from '../../../../src/frontend/gql/generated.ts'
import { appliedMutations } from '../../../../src/frontend/offline/appliedMutations.ts'
import { connectivity } from '../../../../src/frontend/offline/connectivity.ts'
import { deviceData } from '../../../../src/frontend/offline/deviceData.ts'
import { OfflineError } from '../../../../src/frontend/offline/errors.ts'
import { createOfflineLink } from '../../../../src/frontend/offline/offlineLink.ts'
import { outbox } from '../../../../src/frontend/offline/outbox.ts'
import { trackedFetch } from '../../../../src/frontend/offline/trackedFetch.ts'
import { silenceConsoleError } from '../../support/mocks.tsx'
import { server } from '../../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const health = { health: { status: 'ok', version: '1.2.3', databaseReachable: true } }

it('a request that gets no answer marks the server unreachable; then nothing is sent until the probe gets through', async () => {
  const consoleError = silenceConsoleError()
  let sent = 0
  server.use(graphql.query('Health', () => (sent++, HttpResponse.error())))
  const client = createApolloClient('http://localhost/graphql')

  await expect(client.query({ query: HealthDocument, fetchPolicy: 'network-only' })).rejects.toBeInstanceOf(TypeError)
  expect(connectivity.reachable).toBe(false)

  await expect(client.query({ query: HealthDocument, fetchPolicy: 'network-only' })).rejects.toBeInstanceOf(OfflineError)
  expect(sent).toBe(1) // the second one was never sent
  expect(consoleError).toHaveBeenCalledTimes(1) // and not reported: the screen says offline

  server.use(graphql.query('Health', () => HttpResponse.json({ data: health })))
  await client.query({ query: HealthDocument, fetchPolicy: 'network-only', context: { offline: 'bypass' } }) // the probe
  expect(connectivity.reachable).toBe(true)
})

it('an answer, even an error, means the server is reachable; a gateway answering for it means it is not', async () => {
  silenceConsoleError()
  const client = createApolloClient('http://localhost/graphql')
  connectivity.failed()

  server.use(graphql.query('Health', () => HttpResponse.json({ errors: [{ message: 'Unexpected Execution Error' }] })))
  await expect(client.query({ query: HealthDocument, fetchPolicy: 'network-only', context: { offline: 'bypass' } })).rejects.toBeDefined()
  expect(connectivity.reachable).toBe(true)

  // A reverse proxy whose app is down answers with its own page, not GraphQL.
  server.use(http.post('http://localhost/graphql', () => new HttpResponse('<html>Bad gateway</html>', { status: 502 })))
  await expect(client.query({ query: HealthDocument, fetchPolicy: 'network-only' })).rejects.toBeDefined()
  expect(connectivity.reachable).toBe(false)
})

it('the REST requests report their outcome too', async () => {
  server.use(http.put('/media/me/avatar', () => HttpResponse.error()))
  await expect(trackedFetch('/media/me/avatar', { method: 'PUT' })).rejects.toBeInstanceOf(TypeError)
  expect(connectivity.reachable).toBe(false)
  await expect(trackedFetch('/media/me/avatar', { method: 'PUT' })).rejects.toBeInstanceOf(OfflineError)
})

const defaults = { vehicleDefaults: { __typename: 'VehicleDefaults', distanceUnit: 'KILOMETRES', volumeUnit: 'LITRES', currency: 'EUR', recurringWarnDays: 14, recurringWarnDistance: 500 } }
const session = { session: { __typename: 'Session', mode: 'NONE', user: null }, notices: [] }

async function signedIn(client: ReturnType<typeof createApolloClient>) {
  server.use(graphql.query('Session', () => HttpResponse.json({ data: session })))
  await client.query({ query: SessionDocument, fetchPolicy: 'network-only' })
  await deviceData.settled()
}

it('keeps the answer of a query and gives it again while the server is out of reach, without sending anything', async () => {
  const client = createApolloClient('http://localhost/graphql')
  await signedIn(client)
  let sent = 0
  server.use(graphql.query('VehicleDefaults', () => (sent++, HttpResponse.json({ data: defaults }))))
  await client.query({ query: VehicleDefaultsDocument, fetchPolicy: 'network-only' })
  await deviceData.settled()

  connectivity.failed()
  const offline = createApolloClient('http://localhost/graphql') // a fresh cache: the answer comes from the device
  const { data } = await offline.query({ query: VehicleDefaultsDocument, fetchPolicy: 'network-only' })

  expect(data?.vehicleDefaults.currency).toBe('EUR')
  expect(sent).toBe(1)
})

it('a query that fails because the server just went out of reach is answered from the device', async () => {
  const client = createApolloClient('http://localhost/graphql')
  await signedIn(client)
  server.use(graphql.query('VehicleDefaults', () => HttpResponse.json({ data: defaults })))
  await client.query({ query: VehicleDefaultsDocument, fetchPolicy: 'network-only' })
  await deviceData.settled()

  server.use(graphql.query('VehicleDefaults', () => HttpResponse.error()))
  const { data } = await createApolloClient('http://localhost/graphql').query({ query: VehicleDefaultsDocument, fetchPolicy: 'network-only' })

  expect(data?.vehicleDefaults.currency).toBe('EUR')
  expect(connectivity.reachable).toBe(false)
})

it('an answer with errors is never kept, and nothing kept says it is not on this device yet', async () => {
  const client = createApolloClient('http://localhost/graphql')
  await signedIn(client)
  silenceConsoleError()
  server.use(graphql.query('VehicleDefaults', () => HttpResponse.json({ data: null, errors: [{ message: 'Unexpected Execution Error' }] })))
  await expect(client.query({ query: VehicleDefaultsDocument, fetchPolicy: 'network-only' })).rejects.toBeDefined()
  await deviceData.settled()

  connectivity.failed()
  const error = await client.query({ query: VehicleDefaultsDocument, fetchPolicy: 'network-only' }).catch((e: unknown) => e)
  expect(error).toBeInstanceOf(OfflineError)
  expect((error as OfflineError).reason).toBe('notLoaded')
})

it('a screen that needs the server says so, and a change is not sent', async () => {
  const client = createApolloClient('http://localhost/graphql')
  connectivity.failed()

  const admin = await client.query({ query: AdminDocument, fetchPolicy: 'network-only' }).catch((e: unknown) => e)
  expect((admin as OfflineError).reason).toBe('onlineOnly')
  const login = await client.mutate({ mutation: LoginDocument, variables: { input: { email: 'a@b.c', password: 'x' } } }).catch((e: unknown) => e)
  expect(login).toBeInstanceOf(OfflineError)
  expect((login as OfflineError).reason).toBeUndefined()
})

it('signing in stops keeping until the server says who is signed in', async () => {
  const client = createApolloClient('http://localhost/graphql')
  await signedIn(client)
  server.use(
    graphql.mutation('Login', () => HttpResponse.json({ data: { login: { __typename: 'UserInfo', id: 'u2' } } })),
    graphql.query('VehicleDefaults', () => HttpResponse.json({ data: defaults })),
  )
  await client.mutate({ mutation: LoginDocument, variables: { input: { email: 'a@b.c', password: 'x' } } })
  await client.query({ query: VehicleDefaultsDocument, fetchPolicy: 'network-only' })
  await deviceData.settled()

  connectivity.failed()
  const error = await client.query({ query: VehicleDefaultsDocument, fetchPolicy: 'network-only' }).catch((e: unknown) => e)
  expect((error as OfflineError).reason).toBe('notLoaded')
})

it('an operation without a name goes to the server as it is: nothing is kept of it, and no screen hears it applied', async () => {
  server.use(
    http.post('http://localhost/graphql', async ({ request }) => {
      const { query } = (await request.json()) as { query: string }
      return HttpResponse.json({ data: query.trimStart().startsWith('mutation') ? { logout: true } : health })
    }),
  )
  const applied: string[] = []
  const stop = appliedMutations.subscribe((name) => applied.push(name))
  const client = createApolloClient('http://localhost/graphql')
  await signedIn(client)

  const { data } = await client.query({ query: parse('{ health { status version databaseReachable } }'), fetchPolicy: 'network-only' })
  await client.mutate({ mutation: parse('mutation { logout }') })
  await deviceData.settled()
  stop()

  expect(data).toEqual(health)
  expect(applied).toEqual([])
  connectivity.failed()
  await expect(client.query({ query: parse('{ health { status version databaseReachable } }'), fetchPolicy: 'network-only' })).rejects.toBeInstanceOf(OfflineError)
})

it('while a change of a log waits, its details are answered by the device even though the server can be reached', async () => {
  let asked = 0
  server.use(graphql.query('RefuelingDetails', () => (asked++, HttpResponse.json({ data: { refueling: null } }))))
  await deviceData.signedIn('u1')
  await outbox.reload()
  const rows = (await deviceData.rows())!
  await rows.putLogs('refuelings', [{
    __typename: 'Refueling', id: 'r1', vehicleId: 'v1', date: '2026-10-01', volume: 40, totalCost: 100, currency: 'EUR', odometer: 1000, isFullTank: true,
    missedPreviousFillUp: false, note: null, reviewState: 'NONE', filledFromPhoto: [], version: 2, deletedAt: null, photos: [], pulledAt: 1,
  } as never])
  await outbox.enqueue({ id: 't1', entity: 'refuelings', action: 'trash', vehicleId: 'v1', targetId: 'other' })
  await outbox.enqueue({ id: 'd1', entity: 'recurring', action: 'markDone', vehicleId: 'v1', targetId: 'e9', targetIds: ['s1'], input: { expenseId: 'e9' } })
  await outbox.enqueue({ id: 'u1', entity: 'refuelings', action: 'update', vehicleId: 'v1', targetId: 'r1', expectedVersion: 2, input: { id: 'r1', volume: 31 } })
  const client = createApolloClient('http://localhost/graphql')

  const { data } = await client.query({ query: RefuelingDetailsDocument, variables: { id: 'r1' }, fetchPolicy: 'network-only' })
  const other = await client.query({ query: RefuelingDetailsDocument, variables: { id: 'r2' }, fetchPolicy: 'network-only' })

  expect(data?.refueling).toMatchObject({ id: 'r1', volume: 31 })
  expect(other.data?.refueling).toBeNull() // nothing waits for that one: the server answered
  expect(asked).toBe(1)
})

it('an answer the device cannot keep (its storage is full) still reaches the screen', async () => {
  const faulty = Object.assign(Object.create(deviceData) as typeof deviceData, { keep: () => Promise.reject(new Error('The storage is full.')) })
  server.use(
    graphql.query('Session', () => HttpResponse.json({ data: session })),
    graphql.query('VehicleDefaults', () => HttpResponse.json({ data: defaults })),
  )
  const client = new ApolloClient({ link: ApolloLink.from([createOfflineLink(faulty), new HttpLink({ uri: 'http://localhost/graphql' })]), cache: new InMemoryCache() })

  const signed = await client.query({ query: SessionDocument, fetchPolicy: 'network-only' })
  const { data } = await client.query({ query: VehicleDefaultsDocument, fetchPolicy: 'network-only' })
  await deviceData.settled()

  expect(signed.data?.session.mode).toBe('NONE')
  expect(data?.vehicleDefaults.currency).toBe('EUR')
  connectivity.failed()
  const error = await client.query({ query: VehicleDefaultsDocument, fetchPolicy: 'network-only' }).catch((e: unknown) => e)
  expect((error as OfflineError).reason).toBe('notLoaded') // nothing was kept
})
