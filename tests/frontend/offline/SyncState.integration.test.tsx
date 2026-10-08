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
import { createPullEngine } from '../../../src/frontend/offline/pull.ts'
import { fakeLogBackend, fakeRefueling, fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport } from '../support/mocks.tsx'
import { fakeFeed, now } from '../support/offlineFeed.ts'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const noContrast = { rules: { 'color-contrast': { enabled: false } } }

/** Octavia (v1) with three refuellings, downloaded for offline use; then the server goes out of reach. */
async function downloadedThenOffline() {
  deviceData.reset(memoryStorage())
  stubViewport('phone')
  const feed = fakeFeed()
  for (const [id, date] of [['a', '2026-09-01'], ['b', '2026-09-15'], ['c', '2026-10-01']]) feed.add(id, date)
  server.use(sessionHandler('NONE', () => null), healthHandler, ...fakeVehicleBackend([fakeVehicle()]).handlers, ...feed.handlers)
  const online = renderWithApollo(<App />, '/')
  await screen.findByText('Octavia')
  await deviceData.settled()
  await createPullEngine({ client: createApolloClient('http://localhost/graphql'), now }).run()
  online.unmount()
  connectivity.failed()
}

it('a grid has no sync column while nothing on its page waits or was refused', async () => {
  await downloadedThenOffline()
  renderWithApollo(<App />, '/vehicles/v1?tab=refuelings')
  await waitFor(() => expect(screen.getAllByRole('row')).toHaveLength(4))

  expect(screen.queryByRole('columnheader', { name: 'Sync state' })).not.toBeInTheDocument()
})

it('a refuelling moved to the trash offline gets the state icon; its bubble says why it waits and what comes next, and leads to Waiting to sync', async () => {
  await downloadedThenOffline()
  renderWithApollo(<App />, '/vehicles/v1?tab=refuelings')
  const ui = userEvent.setup()
  await waitFor(() => expect(screen.getAllByRole('row')).toHaveLength(4))

  await ui.click(screen.getByRole('button', { name: 'Delete the refuelling of Sep 1, 2026' }))
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Move to trash' }))

  expect(await screen.findByRole('columnheader', { name: 'Sync state' })).toBeInTheDocument()
  const state = screen.getByRole('button', { name: 'Sep 1, 2026: To be removed · not synced' }) // the whole state, never the colour alone
  expect(screen.getAllByRole('button', { name: /not synced/ })).toHaveLength(1) // only the row that waits

  await ui.click(state)
  const bubble = await screen.findByRole('dialog', { name: 'To be removed · not synced' })
  expect(bubble).toHaveTextContent('It moves to the trash on the server when this device next syncs')
  expect(bubble).toHaveTextContent('The server cannot be reached right now.')
  expect(within(bubble).queryByRole('button')).not.toBeInTheDocument() // it only explains: the decisions stay where they are
  expect((await axe(bubble, noContrast)).violations).toEqual([])

  await ui.click(within(bubble).getByRole('link', { name: 'Open Waiting to sync' }))
  expect(await screen.findByRole('heading', { name: 'Waiting to sync', level: 1 })).toBeInTheDocument()
})

it('a change the server could not apply is marked on its row, with the reason the server gave', async () => {
  deviceData.reset(memoryStorage())
  stubViewport('desktop')
  const vehicle = fakeVehicle()
  const parked = {
    __typename: 'SyncChangeInfo', id: 'p1', kind: 'UPDATE_REFUELING', status: 'PARKED', vehicleId: 'v1', targetId: 'r1',
    change: JSON.stringify({ id: 'p1', expectedVersion: 1, updateRefueling: { id: 'r1', volume: 41 } }), receivedAt: '2026-10-06T09:00:00Z',
    reason: { __typename: 'SyncReasonInfo', key: 'sync.versionMismatch', args: [] },
    vehicle: { __typename: 'SyncVehicleRef', id: 'v1', name: 'Octavia', distanceUnit: 'KILOMETERS', volumeUnit: 'LITERS' },
    submittedBy: { __typename: 'UserRef', id: 'u2', displayName: 'Bob' }, canResolve: true,
  }
  server.use(
    sessionHandler('NONE', () => null),
    healthHandler,
    ...fakeLogBackend(vehicle, [fakeRefueling({ id: 'r1' }), fakeRefueling({ id: 'r2', date: '2026-08-01' })]).handlers,
    ...fakeVehicleBackend([vehicle]).handlers,
    graphql.query('ParkedChanges', () => HttpResponse.json({ data: { parkedChanges: [parked] } })),
  )
  renderWithApollo(<App />, '/vehicles/v1?tab=refuelings')
  const ui = userEvent.setup()

  await ui.click(await screen.findByRole('button', { name: 'Sep 1, 2026: Not applied by the server' }))

  const bubble = await screen.findByRole('dialog', { name: 'Not applied by the server' })
  expect(bubble).toHaveTextContent(/It was changed meanwhile by someone else/) // the server's reason, as Waiting to sync words it
  expect(within(bubble).getByRole('link', { name: 'Open Waiting to sync' })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /^Aug 1, 2026:/ })).not.toBeInTheDocument()
})
