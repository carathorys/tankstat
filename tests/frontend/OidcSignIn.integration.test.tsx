import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../src/frontend/App.tsx'
import { navigation } from '../../src/frontend/navigation.ts'
import { fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport, user } from './mocks.tsx'
import { server } from './server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})
afterAll(() => server.close())

it('signing out of an OIDC session leads to a screen that waits; Sign in goes back to the provider', async () => {
  const replace = vi.spyOn(navigation, 'replace').mockImplementation(() => undefined)
  stubViewport('desktop')
  let current: ReturnType<typeof user> | null = user()
  server.use(
    sessionHandler('OIDC', () => current),
    graphql.mutation('Logout', () => {
      current = null
      return HttpResponse.json({ data: { logout: true } })
    }),
    healthHandler,
    ...fakeVehicleBackend([fakeVehicle()]).handlers,
  )
  const ui = userEvent.setup()
  const view = renderWithApollo(<App />, '/')
  await screen.findByText('Octavia')

  await ui.click(screen.getByRole('button', { name: 'Sign out' }))

  await screen.findByRole('heading', { name: 'You have signed out.' })
  expect(replace).not.toHaveBeenCalled() // else the provider's own session would sign the user straight back in

  // The choice survives a reload of the tab (sessionStorage).
  view.unmount()
  renderWithApollo(<App />, '/')
  await screen.findByRole('heading', { name: 'You have signed out.' })
  expect(replace).not.toHaveBeenCalled()

  await ui.click(screen.getByRole('button', { name: 'Sign in' }))
  expect(replace).toHaveBeenCalledTimes(1)
  expect(replace).toHaveBeenCalledWith('/auth/oidc/login?returnUrl=%2F')
  expect(window.sessionStorage.getItem('tankstat.auth.signedOut')).toBeNull()
})
