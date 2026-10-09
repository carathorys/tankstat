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
import { UUID } from '../support/ids.ts'
import { fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport } from '../support/mocks.tsx'
import { fakeFeed, now } from '../support/offlineFeed.ts'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

let sent: string[]

/** Octavia (v1) with a refuelling and an "Insurance" schedule, downloaded for offline use; then the server goes out of reach. */
beforeEach(async () => {
  deviceData.reset(memoryStorage())
  stubViewport('desktop')
  const feed = fakeFeed()
  feed.add('a', '2026-09-01')
  sent = []
  const changes = ['AddExpense', 'UpdateExpense', 'DeleteExpense', 'RestoreExpense', 'MarkRecurringExpensesDone']
  server.use(
    sessionHandler('NONE', () => null),
    healthHandler,
    ...fakeVehicleBackend([fakeVehicle()]).handlers,
    ...feed.handlers,
    // Anything that would change something on the server is counted: nothing should get there.
    ...changes.map((name) => graphql.mutation(name, () => (sent.push(name), HttpResponse.error()))),
  )
  const online = renderWithApollo(<App />, '/')
  await screen.findByText('Octavia')
  await deviceData.settled()
  await createPullEngine({ client: createApolloClient('http://localhost/graphql'), now }).run()
  online.unmount()
  connectivity.failed()
})

it('an expense added offline is marked new in its row, and removing it again leaves nothing to undo or send', async () => {
  renderWithApollo(<App />, '/vehicles/v1?tab=expenses')
  const ui = userEvent.setup()
  await ui.click(await screen.findByRole('button', { name: 'Add expense' }, { timeout: 10_000 }))
  const dialog = await screen.findByRole('dialog', { name: 'Add expense' })
  await ui.type(within(dialog).getByLabelText('Title'), 'Parking')
  await ui.type(within(dialog).getByLabelText('Amount'), '12')
  await ui.clear(within(dialog).getByLabelText('Currency'))
  await ui.type(within(dialog).getByLabelText('Currency'), 'EUR')
  await ui.click(within(dialog).getByRole('button', { name: 'Add expense' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

  // The row's sync state is named after the expense.
  const mark = await screen.findByRole('button', { name: 'Parking: New · not synced' })
  const row = mark.closest<HTMLElement>('[role="row"]')!
  await ui.click(within(row).getByRole('button', { name: 'Delete the expense Parking' }))
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Move to trash' }))

  // Toasts come one after another: this one follows the add's "Saved on this device" (4 s).
  expect(await screen.findByText('Removed from this device; it had not been sent to the server.', {}, { timeout: 8000 })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Undo' })).not.toBeInTheDocument()
  await waitFor(() => expect(screen.queryByText('Parking')).not.toBeInTheDocument())
  expect(outbox.changes).toEqual([])
  expect(sent).toEqual([])
})

it('a visit marked done offline with an amount waits with the expense it will log', async () => {
  renderWithApollo(<App />, '/vehicles/v1?tab=recurring')
  const ui = userEvent.setup()
  await screen.findByText('Insurance')

  await ui.click(screen.getByRole('button', { name: 'Mark Insurance as done' }))
  const dialog = await screen.findByRole('dialog', { name: 'Mark as done: Insurance' })
  await ui.type(within(dialog).getByLabelText('Amount (optional)'), '250')
  await ui.clear(within(dialog).getByLabelText('Currency'))
  await ui.type(within(dialog).getByLabelText('Currency'), 'EUR')
  await ui.click(within(dialog).getByRole('button', { name: 'Mark as done' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

  expect(await screen.findByText('Saved on this device · syncs when online.')).toBeInTheDocument()
  expect(outbox.changes).toHaveLength(1)
  const [visit] = outbox.changes
  expect(visit).toMatchObject({ entity: 'recurring', action: 'markDone', targetIds: ['s-v1'], input: { ids: ['s-v1'], amount: 250, currency: 'EUR' } })
  expect(visit.targetId).toMatch(UUID)
  expect(visit.input).toMatchObject({ expenseId: visit.targetId })
  expect(sent).toEqual([])
})
