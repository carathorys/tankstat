import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import App from '../../src/frontend/App.tsx'
import en from '../../src/frontend/i18n/locales/en.json'
import { server } from './server.ts'
import {
  authWarning,
  fakeVehicle,
  fakeVehicleBackend,
  healthHandler,
  renderWithApollo,
  sessionHandler,
  user,
} from './mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const withBackend = (vehicles = [fakeVehicle()]) => fakeVehicleBackend(vehicles).handlers

it('without authentication shows the warning, the top bar, the data and the API status', async () => {
  server.use(sessionHandler('NONE', () => null, [authWarning]), healthHandler, ...withBackend())
  renderWithApollo(<App />)

  await screen.findByText(en.notices.AUTH_DISABLED)
  expect(screen.getByRole('banner')).toHaveTextContent('Tankstat')
  await screen.findByText('Octavia')
  await screen.findByText('Healthy')
  await screen.findByText(/v1\.2\.3/)
  expect(screen.queryByRole('button', { name: /sign in/i })).not.toBeInTheDocument()
})

it('redirects the start page to the vehicles', async () => {
  server.use(sessionHandler('NONE', () => null), healthHandler, ...withBackend())
  renderWithApollo(<App />, '/')

  await screen.findByRole('heading', { name: 'Vehicles' })
})

it('shows a message for unknown pages', async () => {
  server.use(sessionHandler('NONE', () => null), healthHandler, ...withBackend())
  renderWithApollo(<App />, '/nope')

  await screen.findByText('This page does not exist.')
})

it('shows an alert when the API is down', async () => {
  server.use(
    sessionHandler('NONE', () => null),
    graphql.query('Health', () => new HttpResponse(null, { status: 500 })),
    ...withBackend(),
  )
  renderWithApollo(<App />)

  await screen.findByRole('alert')
})

it('in OIDC mode an anonymous visitor only sees the provider link, no menu and no data', async () => {
  server.use(sessionHandler('OIDC', () => null), healthHandler)
  renderWithApollo(<App />)

  await screen.findByRole('link', { name: /identity provider/i })
  expect(screen.queryByRole('button', { name: 'Open menu' })).not.toBeInTheDocument()
  expect(screen.queryByText('Vehicles')).not.toBeInTheDocument()
})

it('a reset link in the URL opens the set-password screen (standalone, signed out)', async () => {
  server.use(sessionHandler('STANDALONE', () => null), healthHandler)
  renderWithApollo(<App />, '/?resetToken=abc.def')

  await screen.findByRole('heading', { name: 'Set your password' })
})

it('only administrators can open the administration page', async () => {
  server.use(sessionHandler('STANDALONE', () => user()), healthHandler, ...withBackend())
  renderWithApollo(<App />, '/admin')

  await screen.findByRole('heading', { name: 'Vehicles' }) // bounced back
  expect(screen.queryByRole('region', { name: 'Administration' })).not.toBeInTheDocument()
})

it('the hamburger menu navigates between pages', async () => {
  const ui = userEvent.setup()
  server.use(sessionHandler('NONE', () => null), healthHandler, ...withBackend())
  renderWithApollo(<App />)
  await screen.findByRole('heading', { name: 'Vehicles' })

  await ui.click(screen.getByRole('button', { name: 'Open menu' }))
  await ui.click(await screen.findByRole('menuitem', { name: 'Trash' }))

  await screen.findByRole('heading', { name: 'Trash' })
})

it('menu entries follow the user: no account/admin without a user, admin entry only for administrators', async () => {
  const ui = userEvent.setup()
  server.use(sessionHandler('NONE', () => null), healthHandler, ...withBackend())
  const none = renderWithApollo(<App />)
  await screen.findByRole('heading', { name: 'Vehicles' })
  await ui.click(screen.getByRole('button', { name: 'Open menu' }))
  expect(await screen.findAllByRole('menuitem')).toHaveLength(2)
  none.unmount()
  await ui.keyboard('{Escape}')

  server.use(sessionHandler('STANDALONE', () => user({ isAdmin: true })))
  renderWithApollo(<App />)
  await screen.findByText('admin')
  await ui.click(screen.getByRole('button', { name: 'Open menu' }))
  const names = (await screen.findAllByRole('menuitem')).map((i) => i.textContent)
  expect(names).toEqual(['Vehicles', 'Trash', 'Account', 'Administration', 'Sign out'])
})

it('regular users get no administration entry; behind a proxy there is no sign out', async () => {
  const ui = userEvent.setup()
  server.use(sessionHandler('PROXY_HEADER', () => user()), healthHandler, ...withBackend())
  renderWithApollo(<App />)
  await screen.findByRole('heading', { name: 'Vehicles' })

  await ui.click(screen.getByRole('button', { name: 'Open menu' }))

  expect((await screen.findAllByRole('menuitem')).map((i) => i.textContent)).toEqual(['Vehicles', 'Trash', 'Account'])
})

it('the account page shows who is signed in', async () => {
  server.use(sessionHandler('OIDC', () => user({ isAdmin: true })), healthHandler, ...withBackend())
  renderWithApollo(<App />, '/account')

  await screen.findByRole('heading', { name: 'Account' })
  expect(screen.getByText(/alice@example\.com/)).toBeInTheDocument()
  expect(screen.getByText(/managed by your identity provider/)).toBeInTheDocument()
})
