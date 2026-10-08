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

it('a refresh that does not get through leaves the answer as it is, without asking the session again', async () => {
  let sessions = 0
  server.use(
    graphql.query('Health', () => HttpResponse.json(signInFirst())),
    graphql.query('Session', () => {
      sessions++
      return HttpResponse.json({ data: { session: { mode: 'STANDALONE', user: null }, notices: [] } })
    }),
    http.post('/auth/token/refresh', () => HttpResponse.error()),
  )
  const client = createApolloClient('http://localhost/graphql')

  await expect(client.query({ query: HealthDocument, fetchPolicy: 'network-only' })).rejects.toBeDefined()

  await new Promise((resolve) => setTimeout(resolve, 50))
  expect(sessions).toBe(0)
})

it.each(['STANDALONE', 'OIDC'])('the app opened again after its access cookie ran out (%s) refreshes before it says nobody is signed in', async (mode) => {
  let signedIn = false
  let refreshes = 0
  const user = { __typename: 'UserInfo', id: 'u1', email: 'a@x.co', displayName: 'A', isAdmin: false, avatarUrl: null }
  server.use(
    graphql.query('Session', () => HttpResponse.json({ data: { session: { __typename: 'Session', mode, user: signedIn ? user : null }, notices: [] } })),
    http.post('/auth/token/refresh', () => {
      refreshes++
      signedIn = true
      return new HttpResponse(null, { status: 204 })
    }),
  )
  const client = createApolloClient('http://localhost/graphql')

  const result = await client.query({ query: SessionDocument, fetchPolicy: 'network-only' })

  expect(result.data?.session.user?.id).toBe('u1')
  expect(refreshes).toBe(1)
})

it.each(['NONE', 'PROXY_HEADER'])('without a refresh cookie (%s) nobody signed in is the answer at once', async (mode) => {
  let refreshes = 0
  server.use(
    graphql.query('Session', () => HttpResponse.json({ data: { session: { __typename: 'Session', mode, user: null }, notices: [] } })),
    http.post('/auth/token/refresh', () => {
      refreshes++
      return new HttpResponse(null, { status: 204 })
    }),
  )
  const client = createApolloClient('http://localhost/graphql')

  const result = await client.query({ query: SessionDocument, fetchPolicy: 'network-only' })

  expect(result.data?.session.user).toBeNull()
  expect(refreshes).toBe(0)
})

it('a session nobody can refresh stays signed out after one try', async () => {
  let refreshes = 0
  server.use(
    graphql.query('Session', () => HttpResponse.json({ data: { session: { __typename: 'Session', mode: 'STANDALONE', user: null }, notices: [] } })),
    http.post('/auth/token/refresh', () => {
      refreshes++
      return new HttpResponse(null, { status: 401 })
    }),
  )
  const client = createApolloClient('http://localhost/graphql')

  const result = await client.query({ query: SessionDocument, fetchPolicy: 'network-only' })

  expect(result.data?.session.user).toBeNull()
  expect(refreshes).toBe(1)
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
