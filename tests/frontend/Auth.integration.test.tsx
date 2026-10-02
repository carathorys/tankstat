import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import App from '../../src/frontend/App.tsx'
import type { SessionQuery } from '../../src/frontend/gql/generated.ts'
import { server } from './server.ts'
import { fakeVehicleBackend, gqlError, healthHandler, renderWithApollo, sessionHandler, user } from './mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

/** A fake server session: login/logout flip it, and the Session query reports it. */
function standaloneServer(login: (vars: { email: string; password: string }) => boolean = () => true) {
  const state: { current: SessionQuery['session']['user']; calls: Record<string, unknown[]> } = { current: null, calls: {} }
  const record = (name: string, vars: unknown) => (state.calls[name] ??= []).push(vars)

  server.use(
    sessionHandler('STANDALONE', () => state.current),
    healthHandler,
    ...fakeVehicleBackend().handlers,
    graphql.mutation('Login', ({ variables }) => {
      record('Login', variables)
      if (!login(variables.input)) return HttpResponse.json(gqlError('Invalid e-mail or password.', 'INVALID_CREDENTIALS'))
      state.current = user()
      return HttpResponse.json({ data: { login: { id: 'u1' } } })
    }),
    graphql.mutation('Logout', () => {
      state.current = null
      return HttpResponse.json({ data: { logout: true } })
    }),
    graphql.mutation('RequestPasswordReset', ({ variables }) => {
      record('RequestPasswordReset', variables)
      return HttpResponse.json({ data: { requestPasswordReset: true } })
    }),
    graphql.mutation('ResetPassword', ({ variables }) => {
      record('ResetPassword', variables)
      return HttpResponse.json({ data: { resetPassword: true } })
    }),
    graphql.mutation('ChangePassword', ({ variables }) => {
      record('ChangePassword', variables)
      return HttpResponse.json({ data: { changePassword: true } })
    }),
  )
  return state
}

it('anonymous visitors get the login form instead of data', async () => {
  standaloneServer()
  renderWithApollo(<App />, '/vehicles')

  await screen.findByRole('heading', { name: 'Sign in' })
  expect(screen.queryByText('Vehicles')).not.toBeInTheDocument()
})

it('signs in, shows the app, and signs out again', async () => {
  const ui = userEvent.setup()
  standaloneServer()
  renderWithApollo(<App />, '/')

  await ui.type(await screen.findByLabelText('E-mail'), 'alice@example.com')
  await ui.type(screen.getByLabelText('Password'), 'alice-password-1')
  await ui.click(screen.getByRole('button', { name: 'Sign in' }))

  await screen.findByText('Alice')
  await screen.findByText(/You have no vehicles yet/)

  await ui.click(screen.getByRole('button', { name: 'Sign out' }))
  await screen.findByRole('heading', { name: 'Sign in' })
})

it('sends the entered credentials and shows the server message on failure', async () => {
  const ui = userEvent.setup()
  const state = standaloneServer(() => false)
  renderWithApollo(<App />, '/vehicles')

  await ui.type(await screen.findByLabelText('E-mail'), 'alice@example.com')
  await ui.type(screen.getByLabelText('Password'), 'wrong')
  await ui.click(screen.getByRole('button', { name: 'Sign in' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('Invalid e-mail or password.')
  expect(state.calls.Login).toEqual([{ input: { email: 'alice@example.com', password: 'wrong' } }])
  expect(screen.queryByText('Vehicles')).not.toBeInTheDocument()
})

it('does not call the server when the form is incomplete', async () => {
  const ui = userEvent.setup()
  const state = standaloneServer()
  renderWithApollo(<App />, '/vehicles')

  await ui.type(await screen.findByLabelText('E-mail'), 'not-an-email')
  await ui.click(screen.getByRole('button', { name: 'Sign in' }))

  expect(await screen.findByText('Enter a valid e-mail address')).toBeInTheDocument()
  expect(await screen.findByText('Password is required')).toBeInTheDocument()
  expect(state.calls.Login).toBeUndefined()
})

it('forgot password: requests a reset and gives neutral feedback', async () => {
  const ui = userEvent.setup()
  const state = standaloneServer()
  renderWithApollo(<App />, '/vehicles')

  await ui.click(await screen.findByRole('button', { name: 'Forgot password?' }))
  await ui.type(screen.getByLabelText('E-mail'), 'alice@example.com')
  await ui.click(screen.getByRole('button', { name: 'Send reset link' }))

  expect(await screen.findByText(/if that address is registered/i)).toBeInTheDocument()
  expect(state.calls.RequestPasswordReset).toEqual([{ email: 'alice@example.com' }])
})

it('reset link: sets the new password with the token from the URL', async () => {
  const ui = userEvent.setup()
  const state = standaloneServer()
  renderWithApollo(<App />, '/?resetToken=tok.en')

  await ui.type(await screen.findByLabelText('New password'), 'brand-new-password')
  await ui.click(screen.getByRole('button', { name: 'Set password' }))

  expect(await screen.findByText('Your password has been set.')).toBeInTheDocument()
  expect(state.calls.ResetPassword).toEqual([{ input: { token: 'tok.en', newPassword: 'brand-new-password' } }])
})

it('reset link: shows an invalid-link error from the server', async () => {
  const ui = userEvent.setup()
  standaloneServer()
  server.use(
    graphql.mutation('ResetPassword', () =>
      HttpResponse.json(gqlError('This password reset link is invalid or has expired.', 'VALIDATION_FAILED')),
    ),
  )
  renderWithApollo(<App />, '/?resetToken=bad')

  await ui.type(await screen.findByLabelText('New password'), 'brand-new-password')
  await ui.click(screen.getByRole('button', { name: 'Set password' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('invalid or has expired')
})

it('signed-in users can change their password', async () => {
  const ui = userEvent.setup()
  const state = standaloneServer()
  state.current = user()
  renderWithApollo(<App />, '/account')

  await screen.findByRole('heading', { name: 'Change password' })
  await ui.type(screen.getByLabelText('Current password'), 'old-password-1')
  await ui.type(screen.getByLabelText('New password'), 'new-password-123')
  await ui.click(screen.getByRole('button', { name: 'Update password' }))

  await waitFor(() => expect(state.calls.ChangePassword).toEqual([{ input: { currentPassword: 'old-password-1', newPassword: 'new-password-123' } }]))
  expect(await screen.findByText(/Password changed/)).toBeInTheDocument()
})
