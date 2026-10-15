import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import type { SessionQuery } from '../../../src/frontend/gql/generated.ts'
import { server } from '../support/server.ts'
import { fakeVehicleBackend, gqlError, healthHandler, renderWithApollo, sessionHandler, user } from '../support/mocks.tsx'

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
    // Signing out goes to the token endpoint, which ends the device's session even after its access cookie ran out.
    http.post('/auth/token/logout', () => {
      state.current = null
      return new HttpResponse(null, { status: 204 })
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

it('a password shorter than the minimum now: only the change is shown, and the pages come back once it is changed', async () => {
  const ui = userEvent.setup()
  const state = standaloneServer()
  state.current = user({ passwordChangeRequired: true })
  server.use(
    graphql.mutation('ChangePassword', ({ variables }) => {
      ;(state.calls.ChangePassword ??= []).push(variables)
      state.current = user() // the server cleared it with the new password
      return HttpResponse.json({ data: { changePassword: true } })
    }),
  )
  renderWithApollo(<App />, '/vehicles')

  expect(await screen.findByRole('heading', { name: 'Choose a new password' })).toBeInTheDocument()
  expect(screen.getByText(/Passwords must be longer now than when you chose yours/)).toBeInTheDocument()
  expect(screen.queryByRole('navigation', { name: 'Main navigation' })).not.toBeInTheDocument() // nothing else to go to
  expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()

  await ui.type(screen.getByLabelText('Current password'), 'old-password')
  await ui.type(screen.getByLabelText('New password'), 'brand-new-password')
  await ui.type(screen.getByLabelText('Repeat the new password'), 'brand-new-password')
  await ui.click(screen.getByRole('button', { name: 'Update password' }))

  expect(await screen.findByText('Password changed. Your other sessions were signed out.')).toBeInTheDocument()
  await waitFor(() => expect(screen.queryByRole('heading', { name: 'Choose a new password' })).not.toBeInTheDocument())
  expect(await screen.findByRole('navigation', { name: 'Main navigation' })).toBeInTheDocument()
  expect(state.calls.ChangePassword).toEqual([
    { input: { currentPassword: 'old-password', newPassword: 'brand-new-password', newPasswordConfirmation: 'brand-new-password' } },
  ])
})

it('a password shorter than the minimum now: Sign out leaves without changing it', async () => {
  const ui = userEvent.setup()
  const state = standaloneServer()
  state.current = user({ passwordChangeRequired: true })
  renderWithApollo(<App />, '/')

  await ui.click(await screen.findByRole('button', { name: 'Sign out' }))

  expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  expect(state.calls.ChangePassword).toBeUndefined()
})

it('reset link: sets the new password with the token from the URL', async () => {
  const ui = userEvent.setup()
  const state = standaloneServer()
  renderWithApollo(<App />, '/?resetToken=tok.en')

  await ui.type(await screen.findByLabelText('New password'), 'brand-new-password')
  await ui.type(screen.getByLabelText('Repeat the new password'), 'brand-new-password')
  await ui.click(screen.getByRole('button', { name: 'Set password' }))

  expect(await screen.findByText('Your password has been set.')).toBeInTheDocument()
  expect(state.calls.ResetPassword).toEqual([{ input: { token: 'tok.en', newPassword: 'brand-new-password', newPasswordConfirmation: 'brand-new-password' } }])
})

it('reset link: a repetition that differs stops the form at it, and nothing is sent', async () => {
  const ui = userEvent.setup()
  const state = standaloneServer()
  renderWithApollo(<App />, '/?resetToken=tok.en')

  await ui.type(await screen.findByLabelText('New password'), 'brand-new-password')
  await ui.type(screen.getByLabelText('Repeat the new password'), 'brand-new-passwort')
  await ui.click(screen.getByRole('button', { name: 'Set password' }))

  expect(await screen.findByText('The two entries are not the same.')).toBeInTheDocument()
  expect(screen.getByLabelText('Repeat the new password')).toHaveFocus()
  expect(screen.getByLabelText('Repeat the new password')).toHaveAccessibleDescription('The two entries are not the same.')
  expect(state.calls.ResetPassword).toBeUndefined()
})

it('reset link: says the minimum the administrators set before typing, and a shorter password stops the form at it', async () => {
  const ui = userEvent.setup()
  const state = standaloneServer()
  server.use(graphql.query('PasswordRequirements', () => HttpResponse.json({ data: { passwordRequirements: { minLength: 14, maxLength: 128 } } })))
  renderWithApollo(<App />, '/?resetToken=tok.en')

  const password = await screen.findByLabelText('New password')
  await waitFor(() => expect(password).toHaveAccessibleDescription('At least 14 characters.'))
  await ui.type(password, 'thirteen-char')
  await ui.type(screen.getByLabelText('Repeat the new password'), 'thirteen-char')
  await ui.click(screen.getByRole('button', { name: 'Set password' }))

  expect(password).toHaveFocus()
  expect(password).toHaveAccessibleDescription(/The password must be at least 14 characters long\./)
  expect(state.calls.ResetPassword).toBeUndefined()
})

it('reset link: a password longer than the server takes stops the form at it too', async () => {
  const ui = userEvent.setup()
  const state = standaloneServer()
  server.use(graphql.query('PasswordRequirements', () => HttpResponse.json({ data: { passwordRequirements: { minLength: 10, maxLength: 20 } } })))
  renderWithApollo(<App />, '/?resetToken=tok.en')

  const password = await screen.findByLabelText('New password')
  await waitFor(() => expect(password).toHaveAccessibleDescription('At least 10 characters.'))
  await ui.type(password, 'twenty-one-characters')
  await ui.type(screen.getByLabelText('Repeat the new password'), 'twenty-one-characters')
  await ui.click(screen.getByRole('button', { name: 'Set password' }))

  expect(password).toHaveFocus()
  expect(password).toHaveAccessibleDescription(/The password must be at most 20 characters long\./)
  expect(state.calls.ResetPassword).toBeUndefined()
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
  await ui.type(screen.getByLabelText('Repeat the new password'), 'brand-new-password')
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
  await ui.type(screen.getByLabelText('Repeat the new password'), 'new-password-123')
  await ui.click(screen.getByRole('button', { name: 'Update password' }))

  await waitFor(() =>
    expect(state.calls.ChangePassword).toEqual([
      { input: { currentPassword: 'old-password-1', newPassword: 'new-password-123', newPasswordConfirmation: 'new-password-123' } },
    ]),
  )
  expect(await screen.findByText(/Password changed/)).toBeInTheDocument()
})
