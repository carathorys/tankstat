import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { act } from '@testing-library/react'
import { afterAll, afterEach, beforeAll, beforeEach, expect, it } from 'vitest'
import { axe } from 'vitest-axe'
import App from '../../../src/frontend/App.tsx'
import { createApolloClient } from '../../../src/frontend/apolloClient.ts'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { deviceData } from '../../../src/frontend/offline/deviceData.ts'
import { memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import { outbox } from '../../../src/frontend/offline/outbox.ts'
import { createPullEngine } from '../../../src/frontend/offline/pull.ts'
import { fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport } from '../support/mocks.tsx'
import { fakeFeed, now } from '../support/offlineFeed.ts'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

let sent: string[]
let feed: ReturnType<typeof fakeFeed>

/** Octavia (v1) with a refuelling and an "Insurance" schedule, downloaded for offline use; then the server goes out of reach. */
beforeEach(async () => {
  deviceData.reset(memoryStorage())
  stubViewport('desktop')
  feed = fakeFeed()
  feed.add('a', '2026-09-01')
  sent = []
  const changes = ['AddVehicle', 'UpdateVehicle', 'DeleteVehicle', 'AddRecurringExpense', 'UpdateRecurringExpense', 'DeleteRecurringExpense', 'MarkRecurringExpensesDone', 'LogRefueling']
  server.use(
    sessionHandler('NONE', () => null),
    healthHandler,
    ...fakeVehicleBackend([fakeVehicle()]).handlers,
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

it('a vehicle added offline gets its card, its page and logs of its own, all marked and counted', async () => {
  renderWithApollo(<App />, '/')
  const ui = userEvent.setup()
  await screen.findByText('Octavia')

  await ui.click(screen.getByRole('button', { name: 'Add vehicle' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add vehicle' })
  expect(within(dialog).getByText(/^You are offline/)).toBeInTheDocument()
  await ui.type(within(dialog).getByLabelText('Name'), 'Golf')
  await ui.click(within(dialog).getByRole('button', { name: 'Add vehicle' }))

  const card = await screen.findByRole('link', { name: 'Open Golf' })
  expect(await screen.findByText('Saved on this device · syncs when online.')).toBeInTheDocument()
  expect(screen.getByText('New · not synced')).toBeInTheDocument()

  await ui.click(card)
  expect(await screen.findByRole('heading', { name: 'Golf', level: 1 })).toBeInTheDocument()
  await ui.click(screen.getByRole('tab', { name: 'Refuelings' }))
  await ui.click(await screen.findByRole('button', { name: 'Add refuelling' }))
  const refuel = await screen.findByRole('dialog', { name: 'Add refuelling' })
  await ui.type(within(refuel).getByLabelText(/^Volume/), '30')
  await ui.type(within(refuel).getByLabelText('Total cost'), '90')
  await ui.type(within(refuel).getByLabelText(/^Odometer/), '100')
  await ui.type(within(refuel).getByLabelText('Currency'), 'EUR') // a vehicle's first log has no currency to start from, online too
  await ui.click(within(refuel).getByRole('button', { name: 'Add refuelling' }))

  await waitFor(() => expect(screen.getAllByRole('row')).toHaveLength(2)) // a header and the new refuelling of the new vehicle
  expect(screen.getByText('2 changes waiting to sync')).toBeInTheDocument()
  const [vehicle, log] = outbox.changes
  expect(vehicle).toMatchObject({ entity: 'vehicles', action: 'add', input: { name: 'Golf' } })
  expect(log).toMatchObject({ entity: 'refuelings', action: 'add', vehicleId: vehicle.targetId })
  expect(sent).toEqual([])
})

it('schedules added, done and deleted offline are marked, and one done here cannot be done again meanwhile', async () => {
  renderWithApollo(<App />, '/vehicles/v1?tab=recurring')
  const ui = userEvent.setup()
  await screen.findByText('Insurance')

  await ui.click(screen.getByRole('button', { name: 'Add recurring expense' }))
  const add = await screen.findByRole('dialog', { name: 'Add recurring expense' })
  await ui.type(within(add).getByLabelText('Title'), 'Oil')
  await ui.type(within(add).getByLabelText(/^Every \(kilometers\)/), '15000')
  await ui.type(within(add).getByLabelText(/^Odometer on that day/), '1000')
  await ui.click(within(add).getByRole('button', { name: 'Add recurring expense' }))
  const oil = (await screen.findByRole('rowheader', { name: /^Oil/ })).closest('tr')!
  expect(within(oil).getByRole('button', { name: /New · not synced/ })).toBeInTheDocument()

  await ui.click(screen.getByRole('button', { name: 'Mark Insurance as done' }))
  const done = await screen.findByRole('dialog')
  await ui.click(within(done).getByRole('button', { name: 'Mark as done' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

  const insurance = screen.getByRole('rowheader', { name: /^Insurance/ }).closest('tr')!
  expect(within(insurance).getByRole('button', { name: /Done · not synced/ })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Mark Insurance as done' })).not.toBeInTheDocument()
  expect(within(insurance).getByRole('checkbox')).toBeDisabled()
  expect(outbox.changes.map((c) => `${c.entity}:${c.action}`)).toEqual(['recurring:add', 'recurring:markDone'])
  expect(outbox.changes[1].targetIds).toEqual(['s-v1'])

  await ui.click(within(oil).getByRole('button', { name: 'Delete the recurring expense Oil' }))
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Delete' }))
  await waitFor(() => expect(screen.queryByRole('rowheader', { name: /^Oil/ })).not.toBeInTheDocument()) // never sent: simply gone
  expect(outbox.changes.map((c) => `${c.entity}:${c.action}`)).toEqual(['recurring:markDone'])
  expect(sent).toEqual([])
})

it('a vehicle renamed offline shows its new name, marked changed', async () => {
  renderWithApollo(<App />, '/vehicles/v1?tab=details')
  const ui = userEvent.setup()
  await screen.findByRole('heading', { name: 'Car v1', level: 1 })

  await ui.click(await screen.findByRole('button', { name: /^Edit/ }))
  const dialog = await screen.findByRole('dialog', { name: 'Edit vehicle' })
  await waitFor(() => expect(within(dialog).getByLabelText('Name')).toHaveValue('Car v1'))
  await ui.clear(within(dialog).getByLabelText('Name'))
  await ui.type(within(dialog).getByLabelText('Name'), 'Octavia RS')
  await ui.click(within(dialog).getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByRole('heading', { name: 'Octavia RS', level: 1 })).toBeInTheDocument()
  expect(screen.getByText('Changed · not synced')).toBeInTheDocument()
  expect(outbox.changes).toMatchObject([{ entity: 'vehicles', action: 'update', targetId: 'v1', expectedVersion: 3, input: { name: 'Octavia RS' } }])
  expect(sent).toEqual([])
})

it('the next download, with the server back, asks the server and not the device: the vehicle added here is listed once and not asked for', async () => {
  const view = renderWithApollo(<App />, '/')
  const ui = userEvent.setup()
  await screen.findByText('Octavia')
  await ui.click(screen.getByRole('button', { name: 'Add vehicle' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add vehicle' })
  await ui.type(within(dialog).getByLabelText('Name'), 'Golf')
  await ui.click(within(dialog).getByRole('button', { name: 'Add vehicle' }))
  await screen.findByRole('link', { name: 'Open Golf' })
  const results = await axe(document.body, { rules: { 'color-contrast': { enabled: false } } })
  expect(results.violations.map((v) => v.id)).toEqual([]) // the card's mark is text, not colour
  view.unmount()

  await act(() => connectivity.reset())
  feed.asked.length = 0
  await createPullEngine({ client: createApolloClient('http://localhost/graphql'), now }).run()
  connectivity.failed()
  renderWithApollo(<App />, '/')

  expect(await screen.findAllByRole('link', { name: 'Open Golf' })).toHaveLength(1)
  expect(feed.asked.map((input) => input.vehicleId)).toEqual(['v1'])
})

it('a vehicle of the server moved to the trash offline stays on the home page, marked, until Undo takes the change back', async () => {
  renderWithApollo(<App />, '/vehicles/v1?tab=details')
  const ui = userEvent.setup()
  await screen.findByRole('heading', { name: 'Car v1', level: 1 })

  await ui.click(await screen.findByRole('button', { name: /^Move to trash/ }))
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: /^Move to trash/ }))

  // Back on the home page (the vehicle page, marked too, leaves first): the card is marked.
  const card = (await screen.findByRole('link', { name: 'Open Octavia' })).closest('li')!
  expect(await within(card).findByText('To be removed · not synced')).toBeInTheDocument()
  expect(outbox.changes).toMatchObject([{ entity: 'vehicles', action: 'trash', targetId: 'v1', expectedVersion: 3 }])
  await ui.click(await screen.findByRole('button', { name: 'Undo' }))
  await waitFor(() => expect(outbox.changes).toEqual([]))
  expect(screen.queryByText('To be removed · not synced')).not.toBeInTheDocument()
  expect(sent).toEqual([])
})

it('a schedule of the server deleted offline stays listed, marked, until the server has it', async () => {
  renderWithApollo(<App />, '/vehicles/v1?tab=recurring')
  const ui = userEvent.setup()
  await screen.findByText('Insurance')

  await ui.click(screen.getByRole('button', { name: 'Delete the recurring expense Insurance' }))
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Delete' }))

  const row = (await screen.findByRole('button', { name: /To be removed · not synced/ })).closest('tr')!
  expect(within(row).getByRole('rowheader', { name: /^Insurance/ })).toBeInTheDocument()
  expect(outbox.changes).toMatchObject([{ entity: 'recurring', action: 'trash', targetId: 's-v1', expectedVersion: 1 }])
  expect(sent).toEqual([])
})
