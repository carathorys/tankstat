import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, expect, it, vi } from 'vitest'
import { SIGNED_OUT_KEY } from '../../src/frontend/auth/oidc.ts'
import { LoginView } from '../../src/frontend/LoginView.tsx'
import { navigation } from '../../src/frontend/navigation.ts'

afterEach(() => vi.restoreAllMocks())

// StrictMode runs effects twice, as the app does: the redirect must still happen once.
const oidc = (route = '/') => render(<MemoryRouter initialEntries={[route]}><LoginView mode="OIDC" /></MemoryRouter>, { reactStrictMode: true })
const spyOnRedirect = () => vi.spyOn(navigation, 'replace').mockImplementation(() => undefined)

it('in OIDC mode a fresh visitor is sent to the identity provider at once, with a link in case the redirect did not happen', () => {
  const replace = spyOnRedirect()
  oidc('/vehicles/abc?tab=refuelings')

  expect(screen.getByText('Taking you to your identity provider…')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Continue to sign in' })).toHaveAttribute('href', '/auth/oidc/login?returnUrl=%2Fvehicles%2Fabc%3Ftab%3Drefuelings')
  expect(replace).toHaveBeenCalledTimes(1)
  expect(replace).toHaveBeenCalledWith('/auth/oidc/login?returnUrl=%2Fvehicles%2Fabc%3Ftab%3Drefuelings')
})

it('after signing out it waits for a click instead of going back to the provider', async () => {
  const replace = spyOnRedirect()
  window.sessionStorage.setItem(SIGNED_OUT_KEY, '1')
  oidc()

  expect(screen.getByRole('heading', { name: 'You have signed out.' })).toBeInTheDocument()
  expect(replace).not.toHaveBeenCalled()

  await userEvent.setup().click(screen.getByRole('button', { name: 'Sign in' }))
  expect(window.sessionStorage.getItem(SIGNED_OUT_KEY)).toBeNull()
  expect(replace).toHaveBeenCalledWith('/auth/oidc/login?returnUrl=%2F')
})

it('a failed sign-in says why and tries again without the failure marker', async () => {
  const replace = spyOnRedirect()
  oidc('/vehicles/abc?signIn=failed&reason=account_disabled')

  expect(screen.getByRole('alert')).toHaveTextContent('This account has been disabled.')
  expect(replace).not.toHaveBeenCalled()

  await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }))
  expect(replace).toHaveBeenCalledWith('/auth/oidc/login?returnUrl=%2Fvehicles%2Fabc')
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
