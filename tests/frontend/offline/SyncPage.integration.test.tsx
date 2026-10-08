import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import { axe } from 'vitest-axe'
import App from '../../../src/frontend/App.tsx'
import { createApolloClient } from '../../../src/frontend/apolloClient.ts'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { deviceData } from '../../../src/frontend/offline/deviceData.ts'
import { memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import { outbox } from '../../../src/frontend/offline/outbox.ts'
import { createPullEngine } from '../../../src/frontend/offline/pull.ts'
import { fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport, user } from '../support/mocks.tsx'
import { fakeFeed, now } from '../support/offlineFeed.ts'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

/** Octavia (v1) with two refuellings downloaded for offline use, for `who` (sign-in off by default); then the server goes out of reach. */
async function downloaded(session = sessionHandler('NONE', () => null)) {
  deviceData.reset(memoryStorage())
  stubViewport('desktop')
  const feed = fakeFeed()
  feed.add('a', '2026-09-01')
  feed.add('b', '2026-09-20')
  server.use(session, healthHandler, ...fakeVehicleBackend([fakeVehicle()]).handlers, ...feed.handlers)
  const online = renderWithApollo(<App />, '/')
  await screen.findByText('Octavia')
  await deviceData.settled()
  await createPullEngine({ client: createApolloClient('http://localhost/graphql'), now }).run()
  online.unmount()
  connectivity.failed()
}

/** Changes as the dialogs make them: a refuelling added and one trashed on Octavia, and a new vehicle with a refuelling of its own. */
async function someChanges() {
  await outbox.enqueue({ id: 'n1', entity: 'refuelings', action: 'add', vehicleId: 'v1', targetId: 'n1', input: { id: 'n1', vehicleId: 'v1', date: '2026-10-05', volume: 30, totalCost: 90, currency: 'EUR', odometer: 2000 } })
  await outbox.enqueue({ id: 't1', entity: 'refuelings', action: 'trash', vehicleId: 'v1', targetId: 'a', expectedVersion: 1 })
  await outbox.enqueue({ id: 'golf', entity: 'vehicles', action: 'add', vehicleId: 'golf', targetId: 'golf', input: { id: 'golf', name: 'Golf', fuelType: 'PETROL' } })
  await outbox.enqueue({ id: 'g1', entity: 'refuelings', action: 'add', vehicleId: 'golf', targetId: 'g1', input: { id: 'g1', vehicleId: 'golf', date: '2026-10-06', volume: 20, totalCost: 60, currency: 'EUR', odometer: 10 } })
}

it('lists the changes waiting per vehicle, says what each does, and taking back a new vehicle takes its entries along', async () => {
  await downloaded()
  await someChanges()
  renderWithApollo(<App />, '/')
  const ui = userEvent.setup()
  await screen.findByText('Octavia')

  await ui.click(screen.getByRole('link', { name: '4 changes waiting to sync' }))

  const octavia = within(await screen.findByRole('region', { name: /^Octavia/ }))
  expect(octavia.getByRole('heading', { name: 'New refuelling', level: 3 })).toBeInTheDocument()
  expect(octavia.getByText(/30 L · €90\.00$/)).toBeInTheDocument()
  expect(octavia.getByRole('heading', { name: 'Refuelling to the trash' })).toBeInTheDocument()
  const golf = within(screen.getByRole('region', { name: /^Golf/ }))
  expect(golf.getByRole('heading', { name: 'New vehicle' })).toBeInTheDocument()
  expect(golf.getByText('2 changes')).toBeInTheDocument()
  expect((await axe(document.body, { rules: { 'color-contrast': { enabled: false } } })).violations.map((v) => v.id)).toEqual([])

  await ui.click(golf.getByRole('button', { name: /^Remove the change: New vehicle/ }))
  const confirm = await screen.findByRole('alertdialog', { name: 'Remove this change?' })
  expect(within(confirm).getByText(/its entries go with it/)).toBeInTheDocument()
  await ui.click(within(confirm).getByRole('button', { name: 'Remove the change' }))

  expect(await screen.findByText('The change is removed; it will not be sent.')).toBeInTheDocument()
  await waitFor(() => expect(screen.queryByRole('region', { name: /^Golf/ })).not.toBeInTheDocument())
  expect(outbox.changes.map((c) => c.id)).toEqual(['n1', 't1'])
  expect(screen.getByRole('heading', { name: 'Waiting to sync', level: 1 })).toHaveFocus()
})

it('says when nothing is waiting, and the navigation only offers the page while something is', async () => {
  await downloaded()
  renderWithApollo(<App />, '/sync')

  expect(await screen.findByText('Nothing is waiting: everything made on this device is on the server.')).toBeInTheDocument()
  expect(screen.queryByRole('link', { name: /Waiting to sync/ })).not.toBeInTheDocument()

  await outbox.enqueue({ id: 't1', entity: 'refuelings', action: 'trash', vehicleId: 'v1', targetId: 'a', expectedVersion: 1 })
  expect(await screen.findByRole('link', { name: /Waiting to sync/ })).toBeInTheDocument()
})

it('Keep, on an entry waiting to be moved to the trash, takes that back', async () => {
  await downloaded()
  await outbox.enqueue({ id: 't1', entity: 'refuelings', action: 'trash', vehicleId: 'v1', targetId: 'a', expectedVersion: 1 })
  renderWithApollo(<App />, '/vehicles/v1?tab=refuelings')
  const ui = userEvent.setup()

  // The vehicle page and its data grid may load for the first time here, which can take longer than the usual wait.
  await ui.click(await screen.findByRole('button', { name: /^Keep .*: take back moving it to the trash$/ }, { timeout: 10_000 }))

  expect(await screen.findByText('Kept: it will not be moved to the trash.')).toBeInTheDocument()
  await waitFor(() => expect(screen.queryByText('To be removed · not synced')).not.toBeInTheDocument())
  expect(outbox.changes).toEqual([])
})

it('signing out with changes waiting asks first, and they stay on the device for the account', async () => {
  let signedIn = true
  await downloaded(sessionHandler('STANDALONE', () => (signedIn ? user() : null)))
  await outbox.enqueue({ id: 't1', entity: 'refuelings', action: 'trash', vehicleId: 'v1', targetId: 'a', expectedVersion: 1 })
  connectivity.reset()
  server.use(graphql.query('Session', () => HttpResponse.json({ data: { session: { mode: 'STANDALONE', user: signedIn ? user() : null }, notices: [] } })))
  renderWithApollo(<App />, '/')
  const ui = userEvent.setup()
  await screen.findByText('Octavia')

  await ui.click(screen.getByRole('button', { name: 'Sign out' }))
  const confirm = await screen.findByRole('alertdialog', { name: 'Sign out with changes waiting?' })
  expect(within(confirm).getByText(/1 change made on this device has not reached the server yet/)).toBeInTheDocument()
  await ui.click(within(confirm).getByRole('button', { name: 'Cancel' }))
  expect(screen.getByText('Octavia')).toBeInTheDocument()

  await ui.click(screen.getByRole('button', { name: 'Sign out' }))
  signedIn = false
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Sign out' }))

  await waitFor(() => expect(screen.queryByText('Octavia')).not.toBeInTheDocument())
  expect(outbox.changes).toEqual([]) // nobody's data is open now
  await deviceData.signedIn(user().id) // the same account signs in here again
  await outbox.reload()
  expect(outbox.changes).toHaveLength(1) // it waited for them
})
