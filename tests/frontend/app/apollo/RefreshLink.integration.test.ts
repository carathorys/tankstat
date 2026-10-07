import { graphql, http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import { createApolloClient } from '../../../../src/frontend/apolloClient.ts'
import { HealthDocument, SessionDocument } from '../../../../src/frontend/gql/generated.ts'
import { gqlError } from '../../support/mocks.tsx'
import { server } from '../../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const health = { health: { status: 'ok', version: '1.2.3', databaseReachable: true } }
const signInFirst = () => gqlError('Authentication is required.', 'UNAUTHENTICATED', 'auth.unauthenticated')

it('an operation answered "sign in first" is sent again once the refresh cookie bought a new access cookie', async () => {
  let signedIn = false
  let refreshes = 0
  server.use(
    graphql.query('Health', () => HttpResponse.json(signedIn ? { data: health } : signInFirst())),
    http.post('/auth/token/refresh', () => {
      refreshes++
      signedIn = true
      return new HttpResponse(null, { status: 204 })
    }),
  )
  const client = createApolloClient('http://localhost/graphql')

  const result = await client.query({ query: HealthDocument, fetchPolicy: 'network-only' })

  expect(result.data?.health.status).toBe('ok')
  expect(refreshes).toBe(1)
})

it('when the device has to sign in again the answer stands, and the session is asked again so the sign-in screen appears', async () => {
  let sessions = 0
  server.use(
    graphql.query('Health', () => HttpResponse.json(signInFirst())),
    graphql.query('Session', () => {
      sessions++
      return HttpResponse.json({ data: { session: { mode: 'STANDALONE', user: null }, notices: [] } })
    }),
  )
  const client = createApolloClient('http://localhost/graphql')
  const watch = client.watchQuery({ query: SessionDocument })
  const subscription = watch.subscribe(() => undefined)
  await expect.poll(() => sessions).toBe(1)

  await expect(client.query({ query: HealthDocument, fetchPolicy: 'network-only' })).rejects.toBeDefined()

  await expect.poll(() => sessions).toBe(2)
  subscription.unsubscribe()
})

it('the sign-in operations themselves are never refreshed for', async () => {
  let refreshes = 0
  server.use(
    graphql.query('Session', () => HttpResponse.json(signInFirst())),
    http.post('/auth/token/refresh', () => {
      refreshes++
      return new HttpResponse(null, { status: 204 })
    }),
  )
  const client = createApolloClient('http://localhost/graphql')

  await expect(client.query({ query: SessionDocument, fetchPolicy: 'network-only' })).rejects.toBeDefined()

  expect(refreshes).toBe(0)
})
