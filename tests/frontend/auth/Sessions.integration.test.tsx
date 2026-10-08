import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { server } from '../support/server.ts'
import { healthHandler, renderWithApollo, sessionHandler, stubViewport, user } from '../support/mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

type Device = { id: string; client: string | null; createdAt: string; lastUsedAt: string; expiresAt: string; current: boolean }
const device = (id: string, client: string | null, current = false): Device => ({
  id, client, current, createdAt: '2026-09-01T08:00:00Z', lastUsedAt: '2026-10-01T08:00:00Z', expiresAt: '2026-12-30T08:00:00Z',
})

/** A server that keeps the user's devices and signs them out; signing this device out ends the session. */
function fakeSessions(devices: Device[], mode: 'STANDALONE' | 'OIDC' | 'PROXY_HEADER' = 'STANDALONE') {
  const state = { devices: [...devices], signedIn: true as boolean, calls: {} as Record<string, unknown[]> }
  const record = (name: string, vars: unknown) => (state.calls[name] ??= []).push(vars)
  server.use(
    sessionHandler(mode, () => (state.signedIn ? user() : null)),
    healthHandler,
    graphql.query('MySessions', () => HttpResponse.json({ data: { mySessions: state.devices } })),
    graphql.mutation('RevokeSession', ({ variables }) => {
      record('RevokeSession', variables)
      state.devices = state.devices.filter((d) => d.id !== variables.id)
      return HttpResponse.json({ data: { revokeSession: true } })
    }),
    graphql.mutation('RevokeOtherSessions', () => {
      record('RevokeOtherSessions', {})
      const others = state.devices.filter((d) => !d.current).length
      state.devices = state.devices.filter((d) => d.current)
      return HttpResponse.json({ data: { revokeOtherSessions: others } })
    }),
    http.post('/auth/token/logout', () => {
      state.signedIn = false
      return new HttpResponse(null, { status: 204 })
    }),
  )
  return state
}

const section = async () => screen.findByRole('region', { name: 'Signed-in devices' })

it('lists the devices the user is signed in on, this one marked, and when each was used', async () => {
  stubViewport('desktop')
  fakeSessions([device('s1', 'Firefox on Linux', true), device('s2', 'Safari on iOS'), device('s3', null)])
  renderWithApollo(<App />, '/account')

  const list = await section()

  const items = within(list).getAllByRole('listitem')
  expect(items.map((i) => within(i).getByRole('heading').textContent)).toEqual(['Firefox on Linux', 'Safari on iOS', 'Unknown device'])
  expect(within(items[0]).getByText('This device')).toBeInTheDocument()
  expect(within(items[1]).queryByText('This device')).not.toBeInTheDocument()
  expect(within(items[1]).getByText(/Signed in Sep 1, 2026.*last used Oct 1, 2026/)).toBeInTheDocument()
})

it('signs another device out and says so', async () => {
  stubViewport('desktop')
  const ui = userEvent.setup()
  const state = fakeSessions([device('s1', 'Firefox on Linux', true), device('s2', 'Safari on iOS')])
  renderWithApollo(<App />, '/account')
  const list = await section()

  await ui.click(within(list).getByRole('button', { name: 'Sign out of Safari on iOS' }))

  expect(await within(list).findByText('Safari on iOS is signed out.')).toBeInTheDocument()
  expect(state.calls.RevokeSession).toEqual([{ id: 's2' }])
  await waitFor(() => expect(within(list).queryByRole('heading', { name: 'Safari on iOS' })).not.toBeInTheDocument())
  expect(state.signedIn).toBe(true)
})

it('a device signed out meanwhile (another tab) is not announced as signed out by this click', async () => {
  stubViewport('desktop')
  const ui = userEvent.setup()
  const state = fakeSessions([device('s1', 'Firefox on Linux', true), device('s2', 'Safari on iOS')])
  server.use(
    graphql.mutation('RevokeSession', () => {
      state.devices = state.devices.filter((d) => d.id !== 's2')
      return HttpResponse.json({ data: { revokeSession: false } })
    }),
  )
  renderWithApollo(<App />, '/account')
  const list = await section()

  await ui.click(within(list).getByRole('button', { name: 'Sign out of Safari on iOS' }))

  await waitFor(() => expect(within(list).queryByRole('heading', { name: 'Safari on iOS' })).not.toBeInTheDocument())
  expect(within(list).queryByText('Safari on iOS is signed out.')).not.toBeInTheDocument()
})

it('signing this device out is the normal sign-out', async () => {
  stubViewport('desktop')
  const ui = userEvent.setup()
  const state = fakeSessions([device('s1', 'Firefox on Linux', true), device('s2', 'Safari on iOS')])
  renderWithApollo(<App />, '/account')
  const list = await section()

  await ui.click(within(list).getByRole('button', { name: 'Sign out of Firefox on Linux' }))

  await screen.findByRole('heading', { name: 'Sign in' })
  expect(state.signedIn).toBe(false)
  expect(state.calls.RevokeSession).toBeUndefined()
})

it('signs out everywhere else after a confirmation', async () => {
  stubViewport('desktop')
  const ui = userEvent.setup()
  const state = fakeSessions([device('s1', 'Firefox on Linux', true), device('s2', 'Safari on iOS'), device('s3', 'Chrome on Android')])
  renderWithApollo(<App />, '/account')
  const list = await section()

  await ui.click(within(list).getByRole('button', { name: 'Sign out everywhere else' }))
  const dialog = await screen.findByRole('alertdialog', { name: 'Sign out everywhere else?' })
  expect(state.calls.RevokeOtherSessions).toBeUndefined() // nothing before the confirmation
  await ui.click(within(dialog).getByRole('button', { name: 'Sign out everywhere else' }))

  expect(await within(list).findByText('2 other devices are signed out.')).toBeInTheDocument()
  await waitFor(() => expect(within(list).getAllByRole('listitem')).toHaveLength(1))
  expect(within(list).queryByRole('button', { name: 'Sign out everywhere else' })).not.toBeInTheDocument() // nothing else to sign out
})

it('behind a proxy there are no sessions to list', async () => {
  stubViewport('desktop')
  fakeSessions([], 'PROXY_HEADER')
  renderWithApollo(<App />, '/account')

  await screen.findByRole('heading', { name: 'Account' })
  expect(screen.queryByRole('region', { name: 'Signed-in devices' })).not.toBeInTheDocument()
})
