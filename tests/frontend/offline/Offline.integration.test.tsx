import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { deviceData } from '../../../src/frontend/offline/deviceData.ts'
import { memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import { server } from '../support/server.ts'
import { fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, silenceConsoleError, stubViewport, user } from '../support/mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function setup() {
  stubViewport('desktop')
  const backend = fakeVehicleBackend([fakeVehicle()])
  server.use(sessionHandler('NONE', () => null), healthHandler, ...backend.handlers)
  const view = renderWithApollo(<App />, '/')
  return { ...backend, ui: userEvent.setup(), unmount: view.unmount }
}

const status = () => screen.getAllByRole('status').find((s) => s.textContent?.includes('offline') || s.textContent?.includes('online'))

it('a request that gets no answer marks the app offline: the top bar and the footer say so, and so does the live region once', async () => {
  const { ui } = setup()
  await screen.findByText('Octavia')
  silenceConsoleError()
  server.use(graphql.query('Welcome', () => HttpResponse.error()))

  await ui.click(screen.getByRole('link', { name: 'Home' })) // asks again
  await act(() => connectivity.failed()) // what the failed request did; the explicit call keeps the test independent of the page's timing

  expect(await screen.findByText('Offline')).toBeInTheDocument()
  expect(within(screen.getByRole('contentinfo')).getByText('The server cannot be reached')).toBeInTheDocument()
  expect(status()?.textContent).toBe('You are offline. What was loaded stays on screen; saving needs the server.')
  expect(screen.getByText('Octavia')).toBeInTheDocument() // what was loaded stays
})

it('while the server is out of reach nothing is sent, and a page that needs it says so calmly instead of with an error', async () => {
  const { ui } = setup()
  await screen.findByText('Octavia')
  let sent = 0
  server.use(graphql.query('Trash', () => (sent++, HttpResponse.json({ data: null }))))
  await act(() => connectivity.failed())

  await ui.click(screen.getByRole('link', { name: 'Trash' }))

  expect((await screen.findAllByText('This needs a connection to the server. It works again once you are back online.')).length).toBeGreaterThan(0)
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  expect(sent).toBe(0)
})

it('what was opened online opens again without the server, after a restart too, and nothing is sent', async () => {
  const storage = memoryStorage()
  deviceData.reset(storage)
  const view = setup()
  await screen.findByText('Octavia')
  await deviceData.settled()
  view.unmount()

  // The next start: the server is out of reach from the beginning.
  await deviceData.boot(storage)
  connectivity.failed()
  let sent = 0
  server.use(graphql.query('Welcome', () => (sent++, HttpResponse.error())), graphql.query('Session', () => (sent++, HttpResponse.error())))
  renderWithApollo(<App />, '/')

  expect(await screen.findByText('Octavia')).toBeInTheDocument()
  expect(screen.getByText('Offline')).toBeInTheDocument()
  expect(sent).toBe(0)
})

it('a page that was never opened online says it is not on this device yet', async () => {
  const { ui } = setup()
  await screen.findByText('Octavia')
  await act(() => connectivity.failed())

  await ui.click(screen.getByRole('link', { name: /Octavia/ }))

  expect((await screen.findAllByText(/This is not on this device yet/)).length).toBeGreaterThan(0)
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
})

it('what the device kept for one account never shows for another, and signing out leaves nothing to open', async () => {
  const storage = memoryStorage()
  deviceData.reset(storage)
  stubViewport('desktop')
  let current: ReturnType<typeof user> | null = user({ id: 'alice', displayName: 'Alice' })
  const alice = fakeVehicleBackend([fakeVehicle({ name: 'Alice car' })])
  server.use(sessionHandler('STANDALONE', () => current), healthHandler, ...alice.handlers)
  const first = renderWithApollo(<App />, '/')
  await screen.findByText('Alice car')
  await deviceData.settled()
  first.unmount()

  // Bob signs in on the same device.
  await deviceData.boot(storage)
  current = user({ id: 'bob', displayName: 'Bob' })
  server.use(...fakeVehicleBackend([fakeVehicle({ id: 'v2', name: 'Bob car' })]).handlers)
  const second = renderWithApollo(<App />, '/')
  await screen.findByText('Bob car')
  await deviceData.settled()
  second.unmount()

  await deviceData.boot(storage)
  connectivity.failed()
  const third = renderWithApollo(<App />, '/')
  expect(await screen.findByText('Bob car')).toBeInTheDocument()
  expect(screen.queryByText('Alice car')).not.toBeInTheDocument()
  third.unmount()

  // Bob signs out: the next start without the server opens nobody's data.
  connectivity.reset()
  await deviceData.boot(storage)
  current = null
  const fourth = renderWithApollo(<App />, '/')
  await screen.findByRole('button', { name: /sign in/i })
  await deviceData.settled()
  fourth.unmount()

  await deviceData.boot(storage)
  connectivity.failed()
  renderWithApollo(<App />, '/')
  expect(await screen.findByText(/The server cannot be reached right now/)).toBeInTheDocument()
  expect(screen.queryByText('Bob car')).not.toBeInTheDocument()
})

it('Try again in the footer asks the server, and when it answers the app is back online', async () => {
  const { ui } = setup()
  await screen.findByText('Octavia')
  await act(() => connectivity.failed())

  await ui.click(within(screen.getByRole('contentinfo')).getByRole('button', { name: 'Try again' }))

  await waitFor(() => expect(screen.queryByText('Offline')).not.toBeInTheDocument())
  expect(status()?.textContent).toBe('Back online.')
  expect(within(screen.getByRole('contentinfo')).getByText(/API:/)).toBeInTheDocument()
})
