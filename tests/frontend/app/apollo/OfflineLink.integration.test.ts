import { graphql, http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import { createApolloClient } from '../../../../src/frontend/apolloClient.ts'
import { HealthDocument } from '../../../../src/frontend/gql/generated.ts'
import { connectivity } from '../../../../src/frontend/offline/connectivity.ts'
import { OfflineError } from '../../../../src/frontend/offline/errors.ts'
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
