import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, beforeEach, expect, it, vi } from 'vitest'
import { axe } from 'vitest-axe'
import App from '../../../src/frontend/App.tsx'
import { createApolloClient } from '../../../src/frontend/apolloClient.ts'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { deviceData } from '../../../src/frontend/offline/deviceData.ts'
import { memoryStorage, type DeviceStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import { outbox } from '../../../src/frontend/offline/outbox.ts'
import { createPullEngine } from '../../../src/frontend/offline/pull.ts'
import { fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport } from '../support/mocks.tsx'
import { fakeFeed, now } from '../support/offlineFeed.ts'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

let storage: DeviceStorage
let sent: string[]

/** Octavia (v1) with three refuellings, downloaded for offline use; then the server goes out of reach. */
beforeEach(async () => {
  storage = memoryStorage()
  deviceData.reset(storage)
  stubViewport('desktop')
  const feed = fakeFeed()
  for (const [id, date] of [['a', '2026-09-01'], ['b', '2026-09-15'], ['c', '2026-10-01']]) feed.add(id, date)
  sent = []
  server.use(
    sessionHandler('NONE', () => null),
    healthHandler,
    ...fakeVehicleBackend([fakeVehicle()]).handlers,
    ...feed.handlers,
    // Anything that would change a log on the server is counted: nothing should get there.
    ...['LogRefueling', 'UpdateRefueling', 'DeleteRefueling', 'RestoreRefueling'].map((name) => graphql.mutation(name, () => (sent.push(name), HttpResponse.error()))),
    graphql.query('Refuelings', () => (sent.push('Refuelings'), HttpResponse.error())),
  )
  const online = renderWithApollo(<App />, '/')
  await screen.findByText('Octavia')
  await deviceData.settled()
  await createPullEngine({ client: createApolloClient('http://localhost/graphql'), now }).run()
  online.unmount()
  connectivity.failed()
})

async function openRefuelings() {
  const view = renderWithApollo(<App />, '/vehicles/v1?tab=refuelings')
  await waitFor(() => expect(screen.getAllByRole('row')).toHaveLength(4))
  return { view, ui: userEvent.setup() }
}

async function addOffline(ui: ReturnType<typeof userEvent.setup>) {
  await ui.click(screen.getByRole('button', { name: 'Add refuelling' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add refuelling' })
  expect(within(dialog).getByText(/^You are offline\. What you save is kept on this device/)).toBeInTheDocument()
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('EUR')) // the device's own starting values
  await ui.type(within(dialog).getByLabelText(/^Volume/), '30')
  await ui.type(within(dialog).getByLabelText('Total cost'), '90')
  await ui.clear(within(dialog).getByLabelText(/^Odometer/))
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '2000')
  await ui.click(within(dialog).getByRole('button', { name: 'Add refuelling' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
}

it('a refuelling added offline is kept on the device, listed in its place, marked new, and counted in the top bar', async () => {
  const { ui } = await openRefuelings()

  await addOffline(ui)

  expect(await screen.findByText('Saved on this device · syncs when online.')).toBeInTheDocument()
  await waitFor(() => expect(screen.getAllByRole('row')).toHaveLength(5))
  expect(screen.getByRole('button', { name: /New · not synced/ })).toBeInTheDocument()
  expect(screen.getByText('1 change waiting to sync')).toBeInTheDocument()
  expect(outbox.changes).toMatchObject([{ entity: 'refuelings', action: 'add', vehicleId: 'v1', input: { volume: 30, totalCost: 90, odometer: 2000, currency: 'EUR' } }])
  expect(sent).toEqual([])
  const results = await axe(document.body, { rules: { 'color-contrast': { enabled: false } } })
  expect(results.violations.map((v) => v.id)).toEqual([]) // the marks and the count are text, not colour
})

it('a refuelling moved to the trash offline stays listed, marked, until Undo takes the change back', async () => {
  const { ui } = await openRefuelings()
  const [row] = screen.getAllByRole('button', { name: /^Delete the refuelling of/ })

  await ui.click(row)
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Move to trash' }))

  expect(await screen.findByRole('button', { name: /To be removed · not synced/ })).toBeInTheDocument()
  expect(screen.getAllByRole('row')).toHaveLength(4)
  expect(outbox.changes).toMatchObject([{ action: 'trash', expectedVersion: 1 }])

  await ui.click(await screen.findByRole('button', { name: 'Undo' }))

  await waitFor(() => expect(screen.queryByRole('button', { name: /To be removed · not synced/ })).not.toBeInTheDocument())
  expect(outbox.changes).toEqual([])
  expect(screen.queryByText(/waiting to sync/)).not.toBeInTheDocument()
  expect(sent).toEqual([])
})

it('an edit and a trash kept on the device take along the values the refuelling had, so a conflict can be merged later', async () => {
  const { ui } = await openRefuelings()
  const [edit] = screen.getAllByRole('button', { name: /^Edit the refuelling of/ })

  await ui.click(edit)
  const dialog = await screen.findByRole('dialog', { name: /Edit refuelling/ })
  await waitFor(() => expect(within(dialog).getByLabelText(/^Volume/)).toHaveValue('40'))
  await ui.clear(within(dialog).getByLabelText(/^Volume/))
  await ui.type(within(dialog).getByLabelText(/^Volume/), '41')
  await ui.click(within(dialog).getByRole('button', { name: /^Save/ }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

  const base = { date: expect.any(String), volume: 40, totalCost: 100, currency: 'EUR', odometer: 1000, isFullTank: true, missedPreviousFillUp: false, note: null }
  expect(outbox.changes).toMatchObject([{ action: 'update', expectedVersion: 1, input: { volume: 41 }, base }])

  const [trash] = screen.getAllByRole('button', { name: /^Delete the refuelling of/ }).slice(-1)
  await ui.click(trash)
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Move to trash' }))
  await waitFor(() => expect(outbox.changes).toHaveLength(2))
  expect(outbox.changes[1]).toMatchObject({ action: 'trash', base: { volume: 40 } }) // what the list holds of it
  expect(sent).toEqual([])
})

it('editing a refuelling added offline changes the waiting add instead of queueing another change', async () => {
  const { ui } = await openRefuelings()
  await addOffline(ui)
  await screen.findByRole('button', { name: /New · not synced/ })
  const newRow = screen.getByRole('button', { name: /New · not synced/ }).closest('[role="row"]') as HTMLElement

  await ui.click(within(newRow).getByRole('button', { name: /^Edit the refuelling of/ }))
  const dialog = await screen.findByRole('dialog', { name: /Edit refuelling/ })
  await waitFor(() => expect(within(dialog).getByLabelText(/^Volume/)).toHaveValue('30'))
  await ui.clear(within(dialog).getByLabelText(/^Volume/))
  await ui.type(within(dialog).getByLabelText(/^Volume/), '31')
  await ui.click(within(dialog).getByRole('button', { name: /^Save/ }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

  expect(outbox.changes).toHaveLength(1)
  expect(outbox.changes[0]).toMatchObject({ action: 'add', input: { volume: 31 } })
  expect(screen.getByText('1 change waiting to sync')).toBeInTheDocument()
})

it('the changes survive a restart, and while they wait the device answers for the vehicle even when the server is back', async () => {
  const first = await openRefuelings()
  await addOffline(first.ui)
  await screen.findByRole('button', { name: /New · not synced/ })
  first.view.unmount()

  // The next start: the server is reachable again, but nothing has been sent yet.
  await deviceData.boot(storage)
  await outbox.reload()
  await act(() => connectivity.reset())
  renderWithApollo(<App />, '/vehicles/v1?tab=refuelings')

  expect(await screen.findByRole('button', { name: /New · not synced/ })).toBeInTheDocument()
  await waitFor(() => expect(screen.getAllByRole('row')).toHaveLength(5))
  expect(sent).toEqual([]) // the grid did not ask the server, which would not know the new refuelling
})

it('Undo after Keep queues nothing: the trash was already taken back, so there is nothing to restore', async () => {
  const { ui } = await openRefuelings()
  const [row] = screen.getAllByRole('button', { name: /^Delete the refuelling of/ })
  await ui.click(row)
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Move to trash' }))

  await ui.click(await screen.findByRole('button', { name: /^Keep / }))
  await waitFor(() => expect(outbox.changes).toEqual([]))
  await ui.click(screen.getByRole('button', { name: 'Undo' })) // the toast with Undo is still shown; Kept waits behind it

  await waitFor(() => expect(screen.queryByRole('button', { name: 'Undo' })).not.toBeInTheDocument())
  expect(outbox.changes).toEqual([]) // never a restore of something the server has not trashed
  expect(sent).toEqual([])
})

it('a Keep that fails says why, and the row stays marked', async () => {
  const { ui } = await openRefuelings()
  const [row] = screen.getAllByRole('button', { name: /^Delete the refuelling of/ })
  await ui.click(row)
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Move to trash' }))
  const discard = vi.spyOn(outbox, 'discard').mockRejectedValueOnce(new Error('storage failed'))

  await ui.click(await screen.findByRole('button', { name: /^Keep / }))

  // An error does not wait behind the toast with Undo.
  expect(await screen.findByText('Cannot reach the server. Check your connection and try again.')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /To be removed · not synced/ })).toBeInTheDocument()
  expect(outbox.changes).toMatchObject([{ action: 'trash' }])
  discard.mockRestore()
})

it('an entry added offline and then removed is gone, with nothing to undo or send', async () => {
  const { ui } = await openRefuelings()
  await addOffline(ui)
  const newRow = (await screen.findByRole('button', { name: /New · not synced/ })).closest('[role="row"]') as HTMLElement

  await ui.click(within(newRow).getByRole('button', { name: /^Delete the refuelling of/ }))
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Move to trash' }))

  // Toasts come one after another: this one follows the add's "Saved on this device" (4 s).
  expect(await screen.findByText('Removed from this device; it had not been sent to the server.', {}, { timeout: 8000 })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Undo' })).not.toBeInTheDocument()
  await waitFor(() => expect(screen.getAllByRole('row')).toHaveLength(4))
  expect(outbox.changes).toEqual([])
  expect(sent).toEqual([])
})
