import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../src/frontend/App.tsx'
import { server } from './server.ts'
import {
  fakeExpenseBackend,
  fakeRecurring,
  fakeRecurringBackend,
  fakeVehicle,
  fakeVehicleBackend,
  healthHandler,
  renderWithApollo,
  sessionHandler,
  stubViewport,
  type FakeRecurring,
} from './mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

const overdue = fakeRecurring({
  id: 'rc2',
  title: 'Tyres',
  category: null,
  kind: 'ODOMETER',
  intervalMonths: null,
  intervalDistance: 40000,
  lastDoneOdometer: 20000,
  status: { state: 'OVERDUE', limit: 'ODOMETER', dueDate: null, dueOdometer: 60000, daysLeft: null, distanceLeft: -300 },
})
const items = [overdue, fakeRecurring()]

function setup(vehicle = fakeVehicle(), initial: FakeRecurring[] = items) {
  stubViewport('desktop')
  const expenses = fakeExpenseBackend(vehicle, [])
  const recurring = fakeRecurringBackend(initial)
  server.use(sessionHandler('NONE', () => null), healthHandler, ...expenses.handlers, ...recurring.handlers)
  renderWithApollo(<App />, '/vehicles/v1?tab=recurring')
  return { ...recurring, ui: userEvent.setup() }
}

const row = async (title: string) => (await screen.findByRole('rowheader', { name: new RegExp(title) })).closest('tr')!

it('lists the schedules with what repeats, when it was last done, when it is due next and where it stands', async () => {
  setup()

  const oil = within(await row('Oil change'))
  expect(oil.getByText('Every 12 months or 15,000 km, whichever comes first')).toBeInTheDocument()
  expect(oil.getByText('Jan 15, 2026')).toBeInTheDocument()
  expect(oil.getByText('Jan 15, 2027 or at 65,000 km')).toBeInTheDocument()
  expect(oil.getByText('Upcoming')).toBeInTheDocument()
  expect(oil.getByText('in 106 days')).toBeInTheDocument()

  const tyres = within(await row('Tyres'))
  expect(tyres.getByText('Every 40,000 km')).toBeInTheDocument()
  expect(tyres.getByText('Overdue')).toBeInTheDocument()
  expect(tyres.getByText('300 km over')).toBeInTheDocument()
  expect(screen.getAllByRole('rowheader').map((c) => c.textContent?.slice(0, 5))).toEqual(['Tyres', 'Oil c']) // the server's order: most urgent first
})

it('explains the empty state', async () => {
  setup(fakeVehicle(), [])

  expect(await screen.findByText(/Nothing recurring yet/)).toBeInTheDocument()
})

it('adds a schedule that counts time or distance, whichever comes first', async () => {
  const { ui, state } = setup(fakeVehicle(), [])
  await screen.findByText(/Nothing recurring yet/)

  await ui.click(screen.getByRole('button', { name: 'Add recurring expense' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add recurring expense' })
  await ui.type(within(dialog).getByLabelText('Title'), 'Insurance')
  await ui.type(within(dialog).getByLabelText(/^Every \(kilometers\)/), '15000')
  await ui.type(within(dialog).getByLabelText(/^Odometer then/), '50000')
  await ui.click(within(dialog).getByRole('button', { name: 'Add recurring expense' }))

  await screen.findByText('Insurance')
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  expect(state.calls.AddRecurringExpense).toEqual([
    {
      input: {
        vehicleId: 'v1',
        title: 'Insurance',
        category: null,
        note: null,
        kind: 'COMBINED',
        intervalMonths: 12,
        intervalDistance: 15000,
        lastDoneDate: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/),
        lastDoneOdometer: 50000,
        warnDays: 30,
        warnDistance: 500,
      },
    },
  ])
})

it('shows only the fields of the chosen kind', async () => {
  const { ui, state } = setup(fakeVehicle(), [])
  await screen.findByText(/Nothing recurring yet/)
  await ui.click(screen.getByRole('button', { name: 'Add recurring expense' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add recurring expense' })
  expect(within(dialog).getByLabelText(/^Every \(kilometers\)/)).toBeInTheDocument()

  await ui.click(within(dialog).getByRole('combobox', { name: 'Repeats' }))
  await ui.click(await screen.findByRole('option', { name: 'By time' }))

  expect(within(dialog).queryByLabelText(/^Every \(kilometers\)/)).not.toBeInTheDocument()
  expect(within(dialog).queryByLabelText(/^Odometer then/)).not.toBeInTheDocument()
  expect(within(dialog).getByLabelText('Every (months)')).toBeInTheDocument()
  await ui.type(within(dialog).getByLabelText('Title'), 'Inspection')
  await ui.click(within(dialog).getByRole('button', { name: 'Add recurring expense' }))
  await screen.findByText('Inspection')
  expect(state.calls.AddRecurringExpense).toEqual([{ input: expect.objectContaining({ kind: 'TIME', intervalMonths: 12, intervalDistance: null, lastDoneOdometer: null }) }])
})

it('keeps the dialog open and shows a translated message when the server refuses', async () => {
  const { ui, state } = setup(fakeVehicle(), [])
  state.failWith = { message: 'The title can be at most 120 characters.', key: 'recurring.titleTooLong', args: { max: 120 } }
  await screen.findByText(/Nothing recurring yet/)
  await ui.click(screen.getByRole('button', { name: 'Add recurring expense' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add recurring expense' })
  await ui.type(within(dialog).getByLabelText('Title'), 'x')
  await ui.type(within(dialog).getByLabelText(/^Every \(kilometers\)/), '1000')
  await ui.type(within(dialog).getByLabelText(/^Odometer then/), '1')

  await ui.click(within(dialog).getByRole('button', { name: 'Add recurring expense' }))

  expect(await within(dialog).findByText('The title can be at most 120 characters.')).toBeInTheDocument()
  expect(screen.getByRole('dialog', { name: 'Add recurring expense' })).toBeInTheDocument()
})

it('edits a schedule, starting from its current values', async () => {
  const { ui, state } = setup()
  await row('Oil change')

  await ui.click(screen.getByRole('button', { name: 'Edit the recurring expense Oil change' }))
  const dialog = await screen.findByRole('dialog', { name: 'Edit recurring expense' })
  expect(within(dialog).getByLabelText('Title')).toHaveValue('Oil change')
  expect(within(dialog).getByLabelText('Every (months)')).toHaveValue('12')
  expect(within(dialog).getByLabelText(/^Every \(kilometers\)/)).toHaveValue('15000')
  await ui.clear(within(dialog).getByLabelText('Title'))
  await ui.type(within(dialog).getByLabelText('Title'), 'Oil and filter')
  await ui.click(within(dialog).getByRole('button', { name: 'Save changes' }))

  await screen.findByText('Oil and filter')
  expect(state.calls.UpdateRecurringExpense).toEqual([{ input: expect.objectContaining({ id: 'rc1', title: 'Oil and filter', kind: 'COMBINED', intervalMonths: 12, intervalDistance: 15000 }) }])
})

it('marks a schedule as done: the odometer and the cost go along, and the next interval starts', async () => {
  const { ui, state } = setup()
  await row('Oil change')

  await ui.click(screen.getByRole('button', { name: 'Mark Oil change as done' }))
  const dialog = await screen.findByRole('dialog', { name: 'Mark as done: Oil change' })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '62000')
  await ui.type(within(dialog).getByLabelText('Amount'), '35000')
  await ui.click(within(dialog).getByRole('button', { name: 'Mark as done' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.MarkRecurringExpenseDone).toEqual([
    { input: { id: 'rc1', date: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/), odometer: 62000, createExpense: true, amount: 35000, currency: 'HUF' } },
  ])
})

it('can mark it done without logging an expense', async () => {
  const { ui, state } = setup(fakeVehicle(), [fakeRecurring({ kind: 'TIME', intervalDistance: null, lastDoneOdometer: null })])
  await row('Oil change')

  await ui.click(screen.getByRole('button', { name: 'Mark Oil change as done' }))
  const dialog = await screen.findByRole('dialog', { name: 'Mark as done: Oil change' })
  await ui.click(await within(dialog).findByRole('switch', { name: 'Also log it as an expense' }))
  expect(within(dialog).queryByLabelText('Amount')).not.toBeInTheDocument()
  await ui.click(within(dialog).getByRole('button', { name: 'Mark as done' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.MarkRecurringExpenseDone).toEqual([{ input: { id: 'rc1', date: expect.any(String), odometer: null, createExpense: false, amount: null, currency: null } }])
})

it('deletes a schedule after a confirmation', async () => {
  const { ui, state } = setup()
  await row('Oil change')

  await ui.click(screen.getByRole('button', { name: 'Delete the recurring expense Oil change' }))
  expect(state.calls.DeleteRecurringExpense).toBeUndefined()
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Delete' }))

  await waitFor(() => expect(screen.queryByRole('rowheader', { name: /Oil change/ })).not.toBeInTheDocument())
  expect(state.calls.DeleteRecurringExpense).toEqual([{ id: 'rc1' }])
})

it('view-only access: the schedules can be seen but not added, finished, changed or deleted', async () => {
  setup(fakeVehicle({ canEdit: false, logAccess: 'VIEW' }))

  await row('Oil change')

  expect(screen.queryByRole('button', { name: 'Add recurring expense' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /Mark .* as done/ })).not.toBeInTheDocument()
  expect(screen.getAllByText('view only')).toHaveLength(2)
})

it('the home page shows what is overdue or due soon on each card, and nothing for what is merely upcoming', async () => {
  stubViewport('desktop')
  const vehicle = fakeVehicle({
    recurring: [
      { id: 'rc2', title: 'Tyres', kind: 'ODOMETER', status: overdue.status },
      { id: 'rc3', title: 'Insurance', kind: 'TIME', status: { state: 'DUE_SOON', limit: 'TIME', dueDate: '2026-10-20', dueOdometer: null, daysLeft: 12, distanceLeft: null } },
      { id: 'rc1', title: 'Oil change', kind: 'COMBINED', status: fakeRecurring().status },
    ] as never,
  })
  server.use(sessionHandler('NONE', () => null), healthHandler, ...fakeVehicleBackend([vehicle]).handlers)
  renderWithApollo(<App />, '/')

  const card = within((await screen.findByRole('link', { name: 'Open Octavia' })).closest('li')!)
  const list = card.getByRole('list', { name: 'Needs attention' })
  expect(within(list).getAllByRole('listitem').map((li) => li.textContent)).toEqual(['OverdueTyres · 300 km over', 'Due soonInsurance · in 12 days'])
  expect(card.queryByText(/Oil change/)).not.toBeInTheDocument()
})

it('a card without anything to attend to has no such list', async () => {
  stubViewport('desktop')
  server.use(sessionHandler('NONE', () => null), healthHandler, ...fakeVehicleBackend([fakeVehicle()]).handlers)
  renderWithApollo(<App />, '/')

  await screen.findByRole('link', { name: 'Open Octavia' })

  expect(screen.queryByRole('list', { name: 'Needs attention' })).not.toBeInTheDocument()
})
