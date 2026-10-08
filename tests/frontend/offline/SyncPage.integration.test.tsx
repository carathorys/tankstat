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
import { createPushEngine } from '../../../src/frontend/offline/push.ts'
import { provideOfflineSync } from '../../../src/frontend/offline/runtime.ts'
import { fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport, user } from '../support/mocks.tsx'
import { fakeFeed, now } from '../support/offlineFeed.ts'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  provideOfflineSync(null)
})
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

/** A change the server parked, as `ParkedChanges` answers it: of Octavia, sent by `by`. */
function parkedRow(id: string, kind: string, change: object, reason: string, over: Record<string, unknown> = {}) {
  return {
    __typename: 'SyncChangeInfo', id, kind, status: 'PARKED', vehicleId: 'v1', targetId: (change as { id?: string }).id ?? null, change: JSON.stringify(change), receivedAt: '2026-10-06T09:00:00Z',
    reason: { __typename: 'SyncReasonInfo', key: reason, args: [] }, vehicle: { __typename: 'SyncVehicleRef', id: 'v1', name: 'Octavia', distanceUnit: 'KILOMETERS', volumeUnit: 'LITERS' },
    submittedBy: { __typename: 'UserRef', id: 'u2', displayName: 'Bob' }, canResolve: true, ...over,
  }
}

it('Sync now sends what waits; what the server could not apply is listed from the server with its reason, and the top bar says so', async () => {
  await downloaded()
  await outbox.enqueue({ id: 'n1', entity: 'refuelings', action: 'add', vehicleId: 'v1', targetId: 'n1', input: { id: 'n1', vehicleId: 'v1', date: '2026-10-05', volume: 30, totalCost: 90, currency: 'EUR', odometer: 2000 } })
  await outbox.enqueue({ id: 't1', entity: 'refuelings', action: 'trash', vehicleId: 'v1', targetId: 'a', expectedVersion: 1 })
  connectivity.reset()
  const parked: object[] = []
  server.use(
    graphql.mutation('SyncChanges', ({ variables }) => {
      const results = (variables.input.changes as { id: string }[]).map((c) => {
        if (c.id !== 't1') return { __typename: 'SyncChangeResultInfo', id: c.id, status: 'APPLIED', entityId: c.id, version: 1, reason: null }
        parked.push(parkedRow('t1', 'DELETE_REFUELING', { id: 't1', expectedVersion: 1, deleteRefueling: 'a' }, 'sync.versionMismatch', { targetId: 'a', submittedBy: { __typename: 'UserRef', id: 'u1', displayName: 'Anonymous' } }))
        return { __typename: 'SyncChangeResultInfo', id: c.id, status: 'PARKED', entityId: null, version: null, reason: { __typename: 'SyncReasonInfo', key: 'sync.versionMismatch', args: [] } }
      })
      return HttpResponse.json({ data: { syncChanges: { __typename: 'SyncResultInfo', applied: 1, parked: 1, results } } })
    }),
    graphql.query('ParkedChanges', () => HttpResponse.json({ data: { parkedChanges: parked } })),
  )
  provideOfflineSync(createPushEngine({ client: createApolloClient('http://localhost/graphql'), pull: async () => undefined }))
  renderWithApollo(<App />, '/sync')
  const ui = userEvent.setup()

  await ui.click(await screen.findByRole('button', { name: 'Sync now' }))

  expect(await screen.findByText(/^Last synced .*: 1 applied, 1 not applied\.$/)).toBeInTheDocument()
  const notApplied = within(await screen.findByRole('region', { name: 'Not applied' }))
  expect(await notApplied.findByRole('heading', { name: 'Refuelling to the trash' })).toBeInTheDocument()
  expect(notApplied.getByText(/^It was changed meanwhile by someone else/)).toBeInTheDocument()
  expect(notApplied.getByText(/^Sent by Anonymous /)).toBeInTheDocument()
  expect(notApplied.queryByRole('button', { name: /^Remove the change/ })).not.toBeInTheDocument() // the server has it now
  expect(screen.getByRole('link', { name: '1 change could not be applied' })).toBeInTheDocument()
  expect(await screen.findByText('Nothing is waiting: everything made on this device is on the server.')).toBeInTheDocument()
  expect(outbox.changes).toEqual([])
})

it('a parked change of anyone who works with the vehicle can be applied anyway or discarded, by those who may', async () => {
  await downloaded()
  connectivity.reset()
  const update = parkedRow('c1', 'UPDATE_REFUELING', { id: 'c1', expectedVersion: 1, updateRefueling: { id: 'b', date: '2026-09-20', volume: 45, totalCost: 70, currency: 'EUR', odometer: 1500, isFullTank: true } }, 'sync.versionMismatch', { targetId: 'b' })
  const gone = parkedRow('c2', 'DELETE_EXPENSE', { id: 'c2', expectedVersion: 2, deleteExpense: 'x9' }, 'expense.notFound', { targetId: 'x9' })
  const notMine = parkedRow('c3', 'LOG_REFUELING', { id: 'c3', logRefueling: { id: 'c3', vehicleId: 'v1', date: '2026-09-25', volume: 20, totalCost: 30, currency: 'EUR', odometer: 1400 } }, 'odometer.aboveNext', { canResolve: false, targetId: 'c3' })
  let parked = [update, gone, notMine]
  let refuse = true
  const resolved: unknown[] = []
  server.use(
    graphql.query('ParkedChanges', () => HttpResponse.json({ data: { parkedChanges: parked } })),
    graphql.mutation('ResolveSyncChange', ({ variables }) => {
      const { id, action } = variables.input as { id: string; action: string }
      resolved.push(variables.input)
      if (action === 'APPLY' && refuse) {
        refuse = false
        return HttpResponse.json({ data: null, errors: [{ message: 'Below.', extensions: { code: 'VALIDATION_FAILED', key: 'odometer.belowPrevious', args: { previous: '1600', date: '2026-09-21' } } }] })
      }
      const row = parked.find((p) => p.id === id)!
      parked = parked.filter((p) => p.id !== id)
      return HttpResponse.json({ data: { resolveSyncChange: { ...row, status: action === 'APPLY' ? 'APPLIED' : 'DISCARDED' } } })
    }),
  )
  renderWithApollo(<App />, '/sync')
  const ui = userEvent.setup()

  const region = within(await screen.findByRole('region', { name: 'Not applied' }))
  const octavia = within(await region.findByRole('region', { name: /^Octavia/ }))
  expect(octavia.getByText('3 changes')).toBeInTheDocument()
  expect(octavia.getByText(/45 L · €70\.00$/)).toBeInTheDocument() // what the change carries
  expect(octavia.queryByRole('button', { name: /New refuelling/ })).not.toBeInTheDocument() // may not decide about it
  expect(octavia.queryByRole('button', { name: /^Apply anyway: Expense to the trash/ })).not.toBeInTheDocument() // what it changes is gone
  expect((await axe(document.body, { rules: { 'color-contrast': { enabled: false } } })).violations.map((v) => v.id)).toEqual([])

  await ui.click(octavia.getByRole('button', { name: /^Apply anyway: Changed refuelling/ }))
  const confirm = await screen.findByRole('alertdialog', { name: 'Apply this change anyway?' })
  expect(within(confirm).getByText(/^It was not applied because: It was changed meanwhile/)).toBeInTheDocument()
  await ui.click(within(confirm).getByRole('button', { name: 'Apply anyway' }))
  expect(await screen.findByText('Still not applied: The odometer cannot be lower than 1600, the reading on 2026-09-21.')).toBeInTheDocument()

  await ui.click(octavia.getByRole('button', { name: /^Apply anyway: Changed refuelling/ }))
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Apply anyway' }))
  expect(await screen.findByText('Applied.')).toBeInTheDocument()
  await waitFor(() => expect(octavia.queryByRole('heading', { name: 'Changed refuelling' })).not.toBeInTheDocument())

  await ui.click(octavia.getByRole('button', { name: /^Discard: Expense to the trash/ }))
  await ui.click(within(await screen.findByRole('alertdialog', { name: 'Discard this change?' })).getByRole('button', { name: 'Discard' }))
  expect(await screen.findByText('Discarded: it will not be applied.')).toBeInTheDocument()
  expect(resolved).toEqual([{ id: 'c1', action: 'APPLY' }, { id: 'c1', action: 'APPLY' }, { id: 'c2', action: 'DISCARD' }])
  expect(screen.getByRole('heading', { name: 'Waiting to sync', level: 1 })).toHaveFocus()
})

it('offline, it says the changes the server could not apply are shown once it can be reached', async () => {
  await downloaded()
  renderWithApollo(<App />, '/sync')

  expect(await screen.findByText('Changes the server could not apply are shown here when it can be reached again.')).toBeInTheDocument()
})

it('Edit, on a change waiting here, opens its entry’s dialog with what it carries, and saving keeps the change, edited', async () => {
  await downloaded()
  await outbox.enqueue({ id: 'n1', entity: 'refuelings', action: 'add', vehicleId: 'v1', targetId: 'n1', input: { id: 'n1', vehicleId: 'v1', date: '2026-10-05', volume: 30, totalCost: 90, currency: 'EUR', odometer: 2000, isFullTank: true, missedPreviousFillUp: false, note: null, photoIds: ['local:kept'] } })
  renderWithApollo(<App />, '/sync')
  const ui = userEvent.setup()

  await ui.click(await screen.findByRole('button', { name: /^Edit the change: New refuelling/ }, { timeout: 10_000 }))
  const dialog = await screen.findByRole('dialog', { name: 'Edit the change' })
  await waitFor(() => expect(within(dialog).getByLabelText(/^Volume/)).toHaveValue('30'))
  expect(within(dialog).queryByRole('group', { name: 'Photos' })).not.toBeInTheDocument()
  await ui.clear(within(dialog).getByLabelText(/^Volume/))
  await ui.type(within(dialog).getByLabelText(/^Volume/), '32')
  await ui.click(within(dialog).getByRole('button', { name: 'Save the change' }))

  expect(await screen.findByText('The change is saved on this device; it is sent when the server can be reached.')).toBeInTheDocument()
  expect(outbox.changes).toHaveLength(1)
  expect(outbox.changes[0]).toMatchObject({ id: 'n1', action: 'add', input: { id: 'n1', volume: 32, photoIds: ['local:kept'] } }) // still one add, with its photo
})

it('Edit and apply sends the change as edited; a refusal stays in the dialog with its reason, and the change stays parked', async () => {
  await downloaded()
  connectivity.reset()
  const row = parkedRow('c1', 'UPDATE_REFUELING', { id: 'c1', expectedVersion: 1, updateRefueling: { id: 'b', date: '2026-09-20', volume: 45, totalCost: 70, currency: 'EUR', odometer: 1500, isFullTank: true } }, 'sync.versionMismatch', { targetId: 'b' })
  let parked = [row]
  const sent: unknown[] = []
  server.use(
    graphql.query('ParkedChanges', () => HttpResponse.json({ data: { parkedChanges: parked } })),
    graphql.query('LogDefaults', () => HttpResponse.json({ data: { logDefaults: { lastOdometer: 1500, lastDate: '2026-09-20', currency: 'EUR' } } })),
    graphql.mutation('ResolveSyncChange', ({ variables }) => {
      sent.push(variables.input)
      if (sent.length === 1) return HttpResponse.json({ data: null, errors: [{ message: 'Below.', extensions: { code: 'VALIDATION_FAILED', key: 'odometer.belowPrevious', args: { previous: '1600', date: '2026-09-21' } } }] })
      parked = []
      return HttpResponse.json({ data: { resolveSyncChange: { ...row, status: 'APPLIED' } } })
    }),
  )
  renderWithApollo(<App />, '/sync')
  const ui = userEvent.setup()

  await ui.click(await screen.findByRole('button', { name: /^Edit and apply: Changed refuelling/ }, { timeout: 10_000 }))
  const dialog = await screen.findByRole('dialog', { name: 'Edit and apply the change' })
  await waitFor(() => expect(within(dialog).getByLabelText(/^Volume/)).toHaveValue('45'))
  await ui.clear(within(dialog).getByLabelText(/^Volume/))
  await ui.type(within(dialog).getByLabelText(/^Volume/), '44')
  await ui.click(within(dialog).getByRole('button', { name: 'Apply' }))

  expect(await within(dialog).findByText('The odometer cannot be lower than 1600, the reading on 2026-09-21.')).toBeInTheDocument()
  await ui.click(within(dialog).getByRole('button', { name: 'Apply' }))

  expect(await screen.findByText('Applied.')).toBeInTheDocument()
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  expect(sent).toHaveLength(2)
  expect(sent[1]).toMatchObject({ id: 'c1', action: 'APPLY', change: { id: 'c1', updateRefueling: { id: 'b', volume: 44, odometer: 1500 } } })
})
