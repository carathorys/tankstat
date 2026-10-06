import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../src/frontend/App.tsx'
import { platform } from '../../src/frontend/pwa/platform.ts'
import { fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport, user } from './mocks.tsx'
import { server } from './server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})
afterAll(() => server.close())

const offer = () => {
  const event = Object.assign(new Event('beforeinstallprompt', { cancelable: true }), {
    prompt: vi.fn().mockResolvedValue(undefined),
    userChoice: Promise.resolve({ outcome: 'accepted', platform: 'web' }),
  })
  window.dispatchEvent(event)
  return event
}

const signedIn = () => server.use(sessionHandler('STANDALONE', () => user()), healthHandler, ...fakeVehicleBackend([fakeVehicle()]).handlers)

it('offers to install from the phone menu once the browser allows it, and the drawer closes for the browser\'s dialog', async () => {
  stubViewport('phone')
  const event = offer() // Chromium fires it early, before any menu exists
  signedIn()
  const ui = userEvent.setup()
  renderWithApollo(<App />, '/')
  await screen.findByText('Octavia')

  await ui.click(screen.getByRole('button', { name: 'Show menu' }))
  await ui.click(await screen.findByRole('button', { name: 'Install app' }))

  expect(event.prompt).toHaveBeenCalledTimes(1)
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
})

it('the desktop menu shows the entry only once the browser has offered to install', async () => {
  stubViewport('desktop')
  signedIn()
  renderWithApollo(<App />, '/')
  await screen.findByText('Octavia')
  const nav = screen.getByRole('navigation', { name: 'Main navigation' })
  expect(within(nav).queryByRole('button', { name: 'Install app' })).not.toBeInTheDocument()

  act(() => {
    offer()
  })

  expect(await within(nav).findByRole('button', { name: 'Install app' })).toBeVisible()
})

it("on an iPhone the entry explains Safari's Share menu", async () => {
  vi.spyOn(platform, 'isIos').mockReturnValue(true)
  stubViewport('phone')
  signedIn()
  const ui = userEvent.setup()
  renderWithApollo(<App />, '/')
  await screen.findByText('Octavia')

  await ui.click(screen.getByRole('button', { name: 'Show menu' }))
  await ui.click(await screen.findByRole('button', { name: 'Install app' }))

  const dialog = await screen.findByRole('dialog', { name: 'Install Tankstat' })
  expect(within(dialog).getAllByRole('listitem')).toHaveLength(3)
  expect(dialog).toHaveTextContent(/Add to Home Screen/)
  await ui.click(within(dialog).getByRole('button', { name: 'Close' }))
  await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Install Tankstat' })).not.toBeInTheDocument())
})
