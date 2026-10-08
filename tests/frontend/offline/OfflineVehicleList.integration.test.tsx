import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, beforeEach, expect, it } from 'vitest'
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

let sent: string[]
let backend: ReturnType<typeof fakeVehicleBackend>

/**
 * An administrator's device with two vehicles downloaded for offline use (their pages are named as the download saw them: "Car v1",
 * "Car v2"); then the server goes out of reach. Anything that would change something on the server is counted: nothing should get there.
 */
beforeEach(async () => {
  deviceData.reset(memoryStorage())
  stubViewport('desktop')
  const feed = fakeFeed()
  feed.add('a', '2026-09-01')
  sent = []
  backend = fakeVehicleBackend([fakeVehicle(), fakeVehicle({ id: 'v2', name: 'Astra' })])
  const changes = ['AddVehicle', 'UpdateVehicle', 'DeleteVehicle', 'RestoreVehicle', 'LogRefueling']
  server.use(
    sessionHandler('STANDALONE', () => user({ isAdmin: true })),
    healthHandler,
    ...backend.handlers,
    ...feed.handlers,
    ...changes.map((name) => graphql.mutation(name, () => (sent.push(name), HttpResponse.error()))),
  )
  const online = renderWithApollo(<App />, '/')
  await screen.findByText('Octavia')
  await deviceData.settled()
  await createPullEngine({ client: createApolloClient('http://localhost/graphql'), now }).run()
  online.unmount()
  connectivity.failed()
})

const names = () => screen.getAllByRole('rowheader').map((cell) => within(cell).getByRole('link').textContent)

it('the vehicle list shows the vehicles kept on this device, says so, and sorts them on the device', async () => {
  renderWithApollo(<App />, '/vehicles')
  const ui = userEvent.setup()

  expect(await screen.findByText(/only the vehicles kept on this device are listed/)).toBeInTheDocument()
  await waitFor(() => expect(names()).toEqual(['Car v1', 'Car v2']))

  await ui.click(screen.getByRole('columnheader', { name: /^Name/ }))
  await waitFor(() => expect(names()).toEqual(['Car v2', 'Car v1'])) // another order, though the server was never asked for it
  expect(sent).toEqual([])
})

it('a vehicle moved to the trash from the list offline stays listed, marked, until Undo takes the change back', async () => {
  renderWithApollo(<App />, '/vehicles')
  const ui = userEvent.setup()
  await waitFor(() => expect(names()).toEqual(['Car v1', 'Car v2']))

  await ui.click(screen.getByRole('button', { name: 'Delete Car v1' }))
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Move to trash' }))

  expect(await screen.findByRole('button', { name: 'Car v1: To be removed · not synced' })).toBeInTheDocument()
  expect(names()).toEqual(['Car v1', 'Car v2'])
  expect(outbox.changes).toMatchObject([{ entity: 'vehicles', action: 'trash', targetId: 'v1' }])

  await ui.click(await screen.findByRole('button', { name: 'Undo' }))
  await waitFor(() => expect(screen.queryByRole('button', { name: /To be removed · not synced/ })).not.toBeInTheDocument())
  expect(outbox.changes).toEqual([])
  expect(sent).toEqual([])
})

it('a vehicle added or renamed from the list offline is listed with its values, marked', async () => {
  renderWithApollo(<App />, '/vehicles')
  const ui = userEvent.setup()
  await waitFor(() => expect(names()).toEqual(['Car v1', 'Car v2']))

  await ui.click(screen.getByRole('button', { name: 'Add vehicle' }))
  const adding = await screen.findByRole('dialog', { name: 'Add vehicle' })
  await ui.type(within(adding).getByLabelText('Name'), 'Golf')
  await ui.click(within(adding).getByRole('button', { name: 'Add vehicle' }))
  await waitFor(() => expect(names()).toEqual(['Car v1', 'Car v2', 'Golf']))
  expect(screen.getByRole('button', { name: 'Golf: New · not synced' })).toBeInTheDocument()

  await ui.click(screen.getByRole('button', { name: 'Edit Car v2' }))
  const editing = await screen.findByRole('dialog', { name: /^Edit/ })
  await waitFor(() => expect(within(editing).getByLabelText('Name')).toHaveValue('Car v2'))
  await ui.clear(within(editing).getByLabelText('Name'))
  await ui.type(within(editing).getByLabelText('Name'), 'Astra Sport')
  await ui.click(within(editing).getByRole('button', { name: /^Save/ }))

  await waitFor(() => expect(names()).toEqual(['Astra Sport', 'Car v1', 'Golf']))
  expect(screen.getByRole('button', { name: 'Astra Sport: Changed · not synced' })).toBeInTheDocument()
  expect(outbox.changes.map((c) => [c.action, c.targetId])).toEqual([
    ['add', expect.any(String)],
    ['update', 'v2'],
  ])
  expect(sent).toEqual([])
})

it('a vehicle known on this device but not downloaded yet (added online since) can be logged offline, from nothing to start with', async () => {
  backend.state.vehicles.push({ ...fakeVehicle({ id: 'v3', name: 'Golf' }), deletedAt: '' } as never)
  connectivity.reset()
  const online = renderWithApollo(<App />, '/vehicles/v3?tab=refuelings')
  await screen.findByRole('heading', { name: 'Golf', level: 1 }) // its page was seen online; its logs were never downloaded
  await screen.findByRole('button', { name: 'Add refuelling' })
  await deviceData.settled()
  online.unmount()
  connectivity.failed()

  renderWithApollo(<App />, '/vehicles/v3?tab=refuelings')
  const ui = userEvent.setup()
  await screen.findByRole('heading', { name: 'Golf', level: 1 })
  await ui.click(await screen.findByRole('button', { name: 'Add refuelling' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add refuelling' })
  await ui.type(within(dialog).getByLabelText(/^Volume/), '30')
  await ui.type(within(dialog).getByLabelText('Total cost'), '90')
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '100')
  await ui.type(within(dialog).getByLabelText('Currency'), 'EUR')
  await ui.click(within(dialog).getByRole('button', { name: 'Add refuelling' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(outbox.changes).toMatchObject([{ entity: 'refuelings', action: 'add', vehicleId: 'v3' }])
  expect(sent).toEqual([])
})
