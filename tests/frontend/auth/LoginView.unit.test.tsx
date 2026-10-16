import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, expect, it, vi } from 'vitest'
import { SIGNED_OUT_KEY } from '../../../src/frontend/auth/oidc.ts'
import { LoginView } from '../../../src/frontend/LoginView.tsx'
import { navigation } from '../../../src/frontend/navigation.ts'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { deviceData } from '../../../src/frontend/offline/deviceData.ts'

afterEach(() => vi.restoreAllMocks())

// StrictMode runs effects twice, as the app does: the redirect must still happen once.
const oidc = (route = '/') => render(<MemoryRouter initialEntries={[route]}><LoginView mode="OIDC" /></MemoryRouter>, { reactStrictMode: true })
const spyOnRedirect = () => vi.spyOn(navigation, 'replace').mockImplementation(() => undefined)
/** The screen read whether this device signed out here (async): a redirect it would start has started by now. */
const settled = () => act(async () => void (await deviceData.signedOutHere()))

it('in OIDC mode a fresh visitor is sent to the identity provider at once, with a link in case the redirect did not happen', async () => {
  const replace = spyOnRedirect()
  oidc('/vehicles/abc?tab=refuelings')

  expect(screen.getByText('Taking you to your identity provider…')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Continue to sign in' })).toHaveAttribute('href', '/auth/oidc/login?returnUrl=%2Fvehicles%2Fabc%3Ftab%3Drefuelings')
  await waitFor(() => expect(replace).toHaveBeenCalledTimes(1)) // once the device said it did not sign out here
  expect(replace).toHaveBeenCalledWith('/auth/oidc/login?returnUrl=%2Fvehicles%2Fabc%3Ftab%3Drefuelings')
})

it('after signing out it waits for a click instead of going back to the provider', async () => {
  const replace = spyOnRedirect()
  window.sessionStorage.setItem(SIGNED_OUT_KEY, '1')
  oidc()

  expect(screen.getByRole('heading', { name: 'You have signed out.' })).toBeInTheDocument()
  await settled()
  expect(replace).not.toHaveBeenCalled()

  await userEvent.setup().click(screen.getByRole('button', { name: 'Sign in' }))
  await waitFor(() => expect(replace).toHaveBeenCalledWith('/auth/oidc/login?returnUrl=%2F'))
  expect(replace).toHaveBeenCalledTimes(1)
  expect(window.sessionStorage.getItem(SIGNED_OUT_KEY)).toBeNull()
})

it('signed out while the server was out of reach, it waits for a click in any tab, and says why it cannot go while still offline', async () => {
  const replace = spyOnRedirect()
  await deviceData.setSignedOutHere({ mode: 'OIDC', at: 1 }) // no sessionStorage flag: another tab, or after a restart
  connectivity.failed()
  oidc()

  expect(await screen.findByRole('heading', { name: 'You have signed out.' })).toBeInTheDocument()
  await userEvent.setup().click(screen.getByRole('button', { name: 'Sign in' }))
  expect(await screen.findByText('The server cannot be reached right now. This works again once you are back online.')).toBeInTheDocument()
  expect(replace).not.toHaveBeenCalled()

  connectivity.succeeded()
  // Said as things are now: the server is back, so the note goes before anything is clicked.
  await waitFor(() => expect(screen.queryByText(/The server cannot be reached right now/)).not.toBeInTheDocument())
  const logout = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 204 }))
  await userEvent.setup().click(screen.getByRole('button', { name: 'Sign in' }))
  await waitFor(() => expect(replace).toHaveBeenCalledWith('/auth/oidc/login?returnUrl=%2F'))
  expect(logout).toHaveBeenCalledTimes(1) // the session it signed out of ended first: the provider's return must not be signed out again
  expect(await deviceData.signedOutHere()).toBeNull()
})

it("signed out here, Sign in waits while the server does not end the old session, so the provider's return is not signed out again", async () => {
  const replace = spyOnRedirect()
  await deviceData.setSignedOutHere({ mode: 'OIDC', at: 1 })
  oidc()
  expect(await screen.findByRole('heading', { name: 'You have signed out.' })).toBeInTheDocument()
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 500 }))

  await userEvent.setup().click(screen.getByRole('button', { name: 'Sign in' }))

  expect(await screen.findByText(/has not yet ended the session this device signed out of/)).toBeInTheDocument()
  expect(replace).not.toHaveBeenCalled()
  expect(await deviceData.signedOutHere()).not.toBeNull()
})

it('a failed sign-in says why and tries again without the failure marker', async () => {
  const replace = spyOnRedirect()
  oidc('/vehicles/abc?signIn=failed&reason=account_disabled')

  expect(screen.getByRole('alert')).toHaveTextContent('This account has been disabled.')
  await settled() // the device said whether it signed out here: only then could a redirect start
  expect(replace).not.toHaveBeenCalled()

  await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }))
  await waitFor(() => expect(replace).toHaveBeenCalledWith('/auth/oidc/login?returnUrl=%2Fvehicles%2Fabc'))
  expect(replace).toHaveBeenCalledTimes(1) // the click's, never one of its own back to the provider that just refused
})

it('a cancelled or refused sign-in says so; an unknown reason gets the general explanation', () => {
  spyOnRedirect()
  const cancelled = oidc('/?signIn=failed&reason=access_denied')
  expect(screen.getByRole('alert')).toHaveTextContent('The sign-in was cancelled or refused by your identity provider.')
  cancelled.unmount()

  oidc('/?signIn=failed&reason=whatever')

  expect(screen.getByRole('alert')).toHaveTextContent(/did not work/)
})

it('explains the proxy in proxy mode, without any login form', () => {
  render(<LoginView mode="PROXY_HEADER" />)
  expect(screen.getByText(/reverse proxy/i)).toBeInTheDocument()
  expect(screen.queryByRole('button')).not.toBeInTheDocument()
})
