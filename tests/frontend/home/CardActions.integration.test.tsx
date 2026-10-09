import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { outbox } from '../../../src/frontend/offline/outbox.ts'
import { server } from '../support/server.ts'
import {
  fakeExpenseBackend,
  fakeLogBackend,
  fakeRecurring,
  fakeRecurringBackend,
  fakeVehicle,
  fakeVehicleBackend,
  healthHandler,
  renderWithApollo,
  sessionHandler,
  silenceConsoleError,
  stubViewport,
} from '../support/mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

const tyres = () => fakeRecurring({ id: 'rc2', title: 'Tyres', kind: 'ODOMETER', intervalMonths: null, status: { state: 'OVERDUE', limit: 'ODOMETER', dueDate: null, dueOdometer: 60000, daysLeft: null, distanceLeft: -300 } })

/** The home page with Octavia and the backends its quick actions talk to (the backends' own vehicle object, see `fakeVehicleBackend`). */
function setup() {
  stubViewport('desktop')
  const backend = fakeVehicleBackend([fakeVehicle({ recurring: [tyres()] })])
  const first = backend.state.vehicles[0]
  const logs = fakeLogBackend(first, [])
  const expenses = fakeExpenseBackend(first, [])
  const recurring = fakeRecurringBackend(first.recurring, first)
  server.use(sessionHandler('NONE', () => null), healthHandler, ...backend.handlers, ...logs.handlers, ...expenses.handlers, ...recurring.handlers)
  renderWithApollo(<App />, '/')
  return { ...backend, logs, expenses, recurring, ui: userEvent.setup() }
}

const card = async () => within((await screen.findByRole('link', { name: 'Open Octavia' })).closest('li')!)

/** The connection drops just as the change is sent: nothing answers the mutation. */
const dropConnection = (...mutations: string[]) => server.use(...mutations.map((name) => graphql.mutation(name, () => HttpResponse.error())))

async function openFromCard(ui: ReturnType<typeof userEvent.setup>, button: string, title: string) {
  await ui.click((await card()).getByRole('button', { name: button }))
  const dialog = await screen.findByRole('dialog', { name: title })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))
  return dialog
}

it('a refuelling from the card whose connection drops is kept on this device, and the card is not asked again', async () => {
  const { ui, state } = setup()
  dropConnection('LogRefueling')

  const dialog = await openFromCard(ui, 'Refuel Octavia', 'Add refuelling')
  await ui.type(within(dialog).getByLabelText(/^Volume/), '38,2')
  await ui.type(within(dialog).getByLabelText('Total cost'), '19100')
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '12450')
  await ui.click(within(dialog).getByRole('button', { name: 'Add refuelling' }))

  expect(await screen.findByText('Saved on this device · syncs when online.')).toBeInTheDocument()
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  expect(outbox.changes).toMatchObject([{ entity: 'refuelings', action: 'add', vehicleId: 'v1', input: { volume: 38.2, totalCost: 19100, odometer: 12450 } }])
  expect(state.requests.VehicleCard).toEqual([])
})

it('an expense from the card whose connection drops is kept on this device', async () => {
  const { ui, state } = setup()
  dropConnection('AddExpense')

  const dialog = await openFromCard(ui, 'Expense for Octavia', 'Add expense')
  await ui.type(within(dialog).getByLabelText('Title'), 'Car wash')
  await ui.type(within(dialog).getByLabelText('Amount'), '4000')
  await ui.click(within(dialog).getByRole('button', { name: 'Add expense' }))

  expect(await screen.findByText('Saved on this device · syncs when online.')).toBeInTheDocument()
  expect(outbox.changes).toMatchObject([{ entity: 'expenses', action: 'add', vehicleId: 'v1', input: { title: 'Car wash', amount: 4000 } }])
  expect(state.requests.VehicleCard).toEqual([])
})

it('a visit with an amount from the card whose connection drops is kept on this device with the id of the expense it logs', async () => {
  const { ui } = setup()
  dropConnection('MarkRecurringExpensesDone')

  const dialog = await openFromCard(ui, 'Mark Tyres of Octavia as done', 'Mark as done: Tyres')
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '62000')
  await ui.type(within(dialog).getByLabelText('Amount (optional)'), '35000')
  await ui.click(within(dialog).getByRole('button', { name: 'Mark as done' }))

  expect(await screen.findByText('Saved on this device · syncs when online.')).toBeInTheDocument()
  const [visit] = outbox.changes
  expect(visit).toMatchObject({ entity: 'recurring', action: 'markDone', vehicleId: 'v1', targetIds: ['rc2'], input: { ids: ['rc2'], odometer: 62000, amount: 35000 } })
  expect(visit.targetId).toBe((visit.input as { expenseId: string }).expenseId)
})

it('a visit without an amount from the card whose connection drops is kept on this device too, logging no expense', async () => {
  const { ui } = setup()
  dropConnection('MarkRecurringExpensesDone')

  const dialog = await openFromCard(ui, 'Mark Tyres of Octavia as done', 'Mark as done: Tyres')
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '62000')
  await ui.click(within(dialog).getByRole('button', { name: 'Mark as done' }))

  expect(await screen.findByText('Saved on this device · syncs when online.')).toBeInTheDocument()
  expect(outbox.changes).toMatchObject([{ entity: 'recurring', action: 'markDone', targetIds: ['rc2'], input: { ids: ['rc2'], amount: null } }])
  expect(outbox.changes[0].input).not.toHaveProperty('expenseId')
})

it('a saved refuelling never looks like an error when the card cannot be asked again afterwards', async () => {
  silenceConsoleError()
  const { ui, logs } = setup()
  server.use(graphql.query('VehicleCard', () => HttpResponse.json({ data: null, errors: [{ message: 'Boom', extensions: { code: 'UNEXPECTED' } }] })))

  const dialog = await openFromCard(ui, 'Refuel Octavia', 'Add refuelling')
  await ui.type(within(dialog).getByLabelText(/^Volume/), '38,2')
  await ui.type(within(dialog).getByLabelText('Total cost'), '19100')
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '12450')
  await ui.click(within(dialog).getByRole('button', { name: 'Add refuelling' }))

  expect(await screen.findByText('Saved.')).toBeInTheDocument()
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  expect(logs.state.calls.LogRefueling).toHaveLength(1)
  expect(outbox.changes).toEqual([])
})
