import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { LoginView } from '../../src/frontend/LoginView.tsx'

it('offers the identity provider link in OIDC mode', () => {
  render(<LoginView mode="OIDC" />)
  expect(screen.getByRole('link', { name: /identity provider/i })).toHaveAttribute('href', '/auth/oidc/login')
})

it('explains the proxy in proxy mode, without any login form', () => {
  render(<LoginView mode="PROXY_HEADER" />)
  expect(screen.getByText(/reverse proxy/i)).toBeInTheDocument()
  expect(screen.queryByRole('button')).not.toBeInTheDocument()
})
