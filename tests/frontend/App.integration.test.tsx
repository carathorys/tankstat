import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
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
  stubViewport,
  user,
} from './mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
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
  expect(screen.queryByRole('button', { name: /menu/i })).not.toBeInTheDocument()
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

const navLinks = () => within(screen.getByRole('navigation', { name: 'Main navigation' })).getAllByRole('link').map((l) => l.textContent)

it('on a desktop the navigation is docked and open by default, and its links navigate', async () => {
  const ui = userEvent.setup()
  stubViewport('desktop')
  server.use(sessionHandler('NONE', () => null), healthHandler, ...withBackend())
  renderWithApollo(<App />)
  await screen.findByRole('heading', { name: 'Vehicles' })

  expect(screen.getByRole('button', { name: 'Hide menu' })).toHaveAttribute('aria-expanded', 'true')
  await ui.click(within(screen.getByRole('navigation', { name: 'Main navigation' })).getByRole('link', { name: 'Trash' }))

  await screen.findByRole('heading', { name: 'Trash' })
  expect(within(screen.getByRole('navigation', { name: 'Main navigation' })).getByRole('link', { name: 'Trash' })).toHaveAttribute('aria-current', 'page')
})

it('hiding the docked menu is remembered in this browser, and it stays hidden', async () => {
  const ui = userEvent.setup()
  stubViewport('desktop')
  server.use(sessionHandler('NONE', () => null), healthHandler, ...withBackend())
  const first = renderWithApollo(<App />)
  await screen.findByRole('heading', { name: 'Vehicles' })

  await ui.click(screen.getByRole('button', { name: 'Hide menu' }))

  expect(screen.queryByRole('navigation', { name: 'Main navigation' })).not.toBeInTheDocument()
  expect(window.localStorage.getItem('tankstat.nav.open')).toBe('false')
  first.unmount()

  renderWithApollo(<App />) // a later visit
  await screen.findByRole('heading', { name: 'Vehicles' })
  expect(screen.queryByRole('navigation', { name: 'Main navigation' })).not.toBeInTheDocument()
  await ui.click(screen.getByRole('button', { name: 'Show menu' }))
  expect(screen.getByRole('navigation', { name: 'Main navigation' })).toBeInTheDocument()
  expect(window.localStorage.getItem('tankstat.nav.open')).toBe('true')
})

it('on a phone the menu is a closed overlay that opens from the button and closes after choosing a page', async () => {
  const ui = userEvent.setup()
  stubViewport('phone')
  server.use(sessionHandler('NONE', () => null), healthHandler, ...withBackend())
  renderWithApollo(<App />)
  await screen.findByRole('heading', { name: 'Vehicles' })
  expect(screen.queryByRole('navigation', { name: 'Main navigation' })).not.toBeInTheDocument()

  await ui.click(screen.getByRole('button', { name: 'Show menu' }))
  await ui.click(await screen.findByRole('link', { name: 'Trash' }))

  await screen.findByRole('heading', { name: 'Trash' })
  await waitFor(() => expect(screen.queryByRole('navigation', { name: 'Main navigation' })).not.toBeInTheDocument())
  expect(window.localStorage.getItem('tankstat.nav.open')).toBeNull() // the phone drawer is never remembered
})

it('has a skip link, the landmarks and a labelled navigation', async () => {
  stubViewport('desktop')
  server.use(sessionHandler('NONE', () => null), healthHandler, ...withBackend())
  renderWithApollo(<App />)
  await screen.findByRole('heading', { name: 'Vehicles' })

  expect(screen.getByRole('link', { name: 'Skip to main content' })).toHaveAttribute('href', '#main')
  expect(screen.getByRole('banner')).toBeInTheDocument()
  expect(screen.getByRole('main')).toHaveAttribute('id', 'main')
  expect(screen.getByRole('contentinfo')).toBeInTheDocument()
})

it('menu entries follow the user: no account/admin without a user, admin entry only for administrators', async () => {
  stubViewport('desktop')
  server.use(sessionHandler('NONE', () => null), healthHandler, ...withBackend())
  const none = renderWithApollo(<App />)
  await screen.findByRole('heading', { name: 'Vehicles' })
  expect(navLinks()).toEqual(['Vehicles', 'Import', 'Trash'])
  none.unmount()

  server.use(sessionHandler('STANDALONE', () => user({ isAdmin: true })))
  renderWithApollo(<App />)
  await screen.findByText('admin')
  expect(navLinks()).toEqual(['Vehicles', 'Import', 'Trash', 'Account', 'Administration'])
  expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
})

it('regular users get no administration entry; behind a proxy there is no sign out', async () => {
  stubViewport('desktop')
  server.use(sessionHandler('PROXY_HEADER', () => user()), healthHandler, ...withBackend())
  renderWithApollo(<App />)
  await screen.findByRole('heading', { name: 'Vehicles' })

  expect(navLinks()).toEqual(['Vehicles', 'Import', 'Trash', 'Account'])
  expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument()
})

it('the account page shows who is signed in', async () => {
  server.use(sessionHandler('OIDC', () => user({ isAdmin: true })), healthHandler, ...withBackend())
  renderWithApollo(<App />, '/account')

  await screen.findByRole('heading', { name: 'Account' })
  expect(screen.getByText(/alice@example\.com/)).toBeInTheDocument()
  expect(screen.getByText(/managed by your identity provider/)).toBeInTheDocument()
})
