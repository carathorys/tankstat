import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, onTestFinished, vi } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import en from '../../../src/frontend/i18n/locales/en.json'
import { navigation } from '../../../src/frontend/navigation.ts'
import { server } from '../support/server.ts'
import {
  adminSession,
  authWarning,
  fakeVehicle,
  fakeVehicleBackend,
  healthHandler,
  renderWithApollo,
  sessionHandler,
  silenceConsoleError,
  stubViewport,
  user,
} from '../support/mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

const withBackend = (vehicles = [fakeVehicle()]) => fakeVehicleBackend(vehicles).handlers

it('without authentication shows the warning, the top bar, the data and the API status', async () => {
  server.use(sessionHandler('NONE', () => null, [authWarning]), healthHandler, ...withBackend())
  renderWithApollo(<App />, '/vehicles')

  await screen.findByText(en.notices.AUTH_DISABLED)
  expect(screen.getByRole('banner')).toHaveTextContent('Tankstat')
  await screen.findByText('Octavia')
  await screen.findByText('Healthy')
  await screen.findByText(/v1\.2\.3/)
  expect(screen.queryByRole('button', { name: /sign in/i })).not.toBeInTheDocument()
})

it('the start page is the welcome screen with a card per vehicle', async () => {
  server.use(sessionHandler('NONE', () => null), healthHandler, ...withBackend())
  renderWithApollo(<App />, '/')

  await screen.findByRole('heading', { name: 'Your vehicles' })
  expect(await screen.findByRole('link', { name: 'Open Octavia' })).toHaveAttribute('href', '/vehicles/v1')
})

it('shows a message for unknown pages', async () => {
  server.use(sessionHandler('NONE', () => null), healthHandler, ...withBackend())
  renderWithApollo(<App />, '/nope')

  await screen.findByText('This page does not exist.')
})

it('shows an alert when the API is down, and says why in the console', async () => {
  const consoleError = silenceConsoleError()
  server.use(
    sessionHandler('NONE', () => null),
    graphql.query('Health', () => new HttpResponse(null, { status: 500 })),
    ...withBackend(),
  )
  renderWithApollo(<App />, '/vehicles')

  await screen.findByRole('alert')
  expect(consoleError).toHaveBeenCalledWith('GraphQL Health could not be completed', expect.anything())
})

it('in OIDC mode an anonymous visitor is sent to the identity provider and sees no menu and no data meanwhile', async () => {
  const replace = vi.spyOn(navigation, 'replace').mockImplementation(() => undefined)
  onTestFinished(() => replace.mockRestore())
  server.use(sessionHandler('OIDC', () => null), healthHandler)
  renderWithApollo(<App />, '/vehicles')

  expect(await screen.findByRole('link', { name: 'Continue to sign in' })).toHaveAttribute('href', '/auth/oidc/login?returnUrl=%2Fvehicles')
  expect(screen.getByText('Taking you to your identity provider…')).toBeInTheDocument()
  expect(replace).toHaveBeenCalledWith('/auth/oidc/login?returnUrl=%2Fvehicles')
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

  await screen.findByRole('heading', { name: 'Your vehicles' }) // bounced back to the home page
  expect(screen.queryByRole('region', { name: 'Administration' })).not.toBeInTheDocument()
})

const navLinks = () => within(screen.getByRole('navigation', { name: 'Main navigation' })).getAllByRole('link').map((l) => l.textContent)

it('on a desktop the navigation is docked and open by default, and its links navigate', async () => {
  const ui = userEvent.setup()
  stubViewport('desktop')
  server.use(adminSession(), healthHandler, ...withBackend())
  renderWithApollo(<App />, '/vehicles')
  await screen.findByRole('heading', { name: 'Vehicles' })

  expect(screen.getByRole('button', { name: 'Hide menu' })).toHaveAttribute('aria-expanded', 'true')
  await ui.click(within(screen.getByRole('navigation', { name: 'Main navigation' })).getByRole('link', { name: 'Trash' }))

  await screen.findByRole('heading', { name: 'Trash' })
  expect(within(screen.getByRole('navigation', { name: 'Main navigation' })).getByRole('link', { name: 'Trash' })).toHaveAttribute('aria-current', 'page')
})

it('hiding the docked menu is remembered in this browser, and it stays hidden', async () => {
  const ui = userEvent.setup()
  stubViewport('desktop')
  server.use(adminSession(), healthHandler, ...withBackend())
  const first = renderWithApollo(<App />, '/vehicles')
  await screen.findByRole('heading', { name: 'Vehicles' })

  await ui.click(screen.getByRole('button', { name: 'Hide menu' }))

  expect(screen.queryByRole('navigation', { name: 'Main navigation' })).not.toBeInTheDocument()
  expect(window.localStorage.getItem('tankstat.nav.open')).toBe('false')
  first.unmount()

  renderWithApollo(<App />, '/vehicles') // a later visit
  await screen.findByRole('heading', { name: 'Vehicles' })
  expect(screen.queryByRole('navigation', { name: 'Main navigation' })).not.toBeInTheDocument()
  await ui.click(screen.getByRole('button', { name: 'Show menu' }))
  expect(screen.getByRole('navigation', { name: 'Main navigation' })).toBeInTheDocument()
  expect(window.localStorage.getItem('tankstat.nav.open')).toBe('true')
})

it('on a phone the menu is a closed overlay that opens from the button and closes after choosing a page', async () => {
  const ui = userEvent.setup()
  stubViewport('phone')
  server.use(adminSession(), healthHandler, ...withBackend())
  renderWithApollo(<App />, '/vehicles')
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
  server.use(adminSession(), healthHandler, ...withBackend())
  renderWithApollo(<App />, '/vehicles')
  await screen.findByRole('heading', { name: 'Vehicles' })

  expect(screen.getByRole('link', { name: 'Skip to main content' })).toHaveAttribute('href', '#main')
  expect(screen.getByRole('banner')).toBeInTheDocument()
  expect(screen.getByRole('main')).toHaveAttribute('id', 'main')
  expect(screen.getByRole('contentinfo')).toBeInTheDocument()
})

it('menu entries follow the user: no account/admin without a user, admin entry only for administrators', async () => {
  stubViewport('desktop')
  server.use(sessionHandler('NONE', () => null), healthHandler, ...withBackend())
  const none = renderWithApollo(<App />, '/')
  await screen.findByRole('heading', { name: 'Your vehicles' })
  expect(navLinks()).toEqual(['Home', 'Import', 'Notifications', 'Trash']) // the full vehicle list is an administrator feature
  none.unmount()

  server.use(sessionHandler('STANDALONE', () => user({ isAdmin: true })))
  renderWithApollo(<App />, '/vehicles')
  await screen.findByText('admin')
  expect(navLinks()).toEqual(['Home', 'Vehicles', 'Import', 'Notifications', 'Trash', 'Account', 'Administration'])
  expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
})

it('regular users get no administration entry; behind a proxy there is no sign out', async () => {
  stubViewport('desktop')
  server.use(sessionHandler('PROXY_HEADER', () => user()), healthHandler, ...withBackend())
  renderWithApollo(<App />, '/vehicles') // not allowed for them: back to the home page
  await screen.findByRole('heading', { name: 'Your vehicles' })

  expect(navLinks()).toEqual(['Home', 'Import', 'Notifications', 'Trash', 'Account'])
  expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument()
})

it('the account page shows who is signed in', async () => {
  server.use(sessionHandler('OIDC', () => user({ isAdmin: true })), healthHandler, ...withBackend())
  renderWithApollo(<App />, '/account')

  await screen.findByRole('heading', { name: 'Account' })
  expect(screen.getByText(/alice@example\.com/)).toBeInTheDocument()
  expect(screen.getByText(/managed by your identity provider/)).toBeInTheDocument()
})
