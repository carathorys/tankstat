import { http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import { fetchSignedIn, refreshSession, signOutDevice } from '../../../src/frontend/auth/refresh.ts'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

it('a refresh says whether the device is signed in again, and callers at the same moment share one request', async () => {
  const requests: Request[] = []
  server.use(
    http.post('/auth/token/refresh', ({ request }) => {
      requests.push(request)
      return new HttpResponse(null, { status: 204 })
    }),
  )

  expect(await Promise.all([refreshSession(), refreshSession(), refreshSession()])).toEqual([true, true, true])
  expect(requests).toHaveLength(1) // one refresh token is traded in once
  expect(requests[0].headers.get('X-Requested-With')).toBe('fetch')

  expect(await refreshSession()).toBe(true) // a later one is a new request
  expect(requests).toHaveLength(2)
})

it('a refused refresh is "sign in again", a network failure is not', async () => {
  expect(await refreshSession()).toBe(false) // the default handler: no refresh cookie

  server.use(http.post('/auth/token/refresh', () => HttpResponse.error()))
  await expect(refreshSession()).rejects.toBeInstanceOf(TypeError)
})

it('a REST request answered 401 is sent once more after a refresh', async () => {
  let uploads = 0
  server.use(
    http.post('/auth/token/refresh', () => new HttpResponse(null, { status: 204 })),
    http.put('/media/me/avatar', () => (++uploads === 1 ? new HttpResponse(null, { status: 401 }) : HttpResponse.json({ id: 'i1', url: '/media/i1' }))),
  )

  const response = await fetchSignedIn('/media/me/avatar', { method: 'PUT', body: new Blob(['x']) })

  expect((await response.json()) as unknown).toEqual({ id: 'i1', url: '/media/i1' })
  expect(uploads).toBe(2)
})

it('a 401 stays a 401 when the device has to sign in again', async () => {
  server.use(http.put('/media/me/avatar', () => new HttpResponse(null, { status: 401 })))

  expect((await fetchSignedIn('/media/me/avatar', { method: 'PUT' })).status).toBe(401)
})

it('signing out tells the token endpoint, with the header', async () => {
  let header: string | null = null
  server.use(
    http.post('/auth/token/logout', ({ request }) => {
      header = request.headers.get('X-Requested-With')
      return new HttpResponse(null, { status: 204 })
    }),
  )

  await signOutDevice()

  expect(header).toBe('fetch')
})
