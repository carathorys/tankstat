import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { SIGNED_OUT_KEY } from '../../../src/frontend/auth/oidc.ts'
import { navigation } from '../../../src/frontend/navigation.ts'
import { fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport, user } from '../support/mocks.tsx'
import { server } from '../support/server.ts'

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
    http.post('/auth/token/logout', () => {
      current = null
      return new HttpResponse(null, { status: 204 })
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
  expect(window.sessionStorage.getItem(SIGNED_OUT_KEY)).toBeNull()
})
