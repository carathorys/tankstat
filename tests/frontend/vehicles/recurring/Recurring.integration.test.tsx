import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../../src/frontend/App.tsx'
import { server } from '../../support/server.ts'
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
} from '../../support/mocks.tsx'
import { dateValue, findDateField } from '../../support/dates.ts'

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
  await ui.type(within(dialog).getByLabelText(/^Odometer on that day/), '50000')
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

it('starts the warnings of a new schedule from the instance defaults', async () => {
  const { ui, state } = setup(fakeVehicle(), [])
  state.warnDefaults = { recurringWarnDays: 14, recurringWarnDistance: 1000 }
  await screen.findByText(/Nothing recurring yet/)

  await ui.click(screen.getByRole('button', { name: 'Add recurring expense' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add recurring expense' })

  expect(await within(dialog).findByLabelText(/^Warn me this many days before/)).toHaveValue('14')
  expect(within(dialog).getByLabelText(/^Warn me this far before \(kilometers\)/)).toHaveValue('1000')
})

it('starts a new schedule from today and the latest odometer reading, both prefilled and changeable', async () => {
  const { ui, state } = setup(fakeVehicle(), [])
  server.use(graphql.query('LogDefaults', () => HttpResponse.json({ data: { logDefaults: { lastOdometer: 71500, lastDate: '2026-09-30', currency: 'HUF' } } })))
  await screen.findByText(/Nothing recurring yet/)
  await ui.click(screen.getByRole('button', { name: 'Add recurring expense' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add recurring expense' })
  await waitFor(() => expect(within(dialog).getByLabelText(/^Odometer on that day/)).toHaveValue('71500'))
  expect(dateValue(await findDateField('Counting from', dialog))).toMatch(/^\d{4}-\d{2}-\d{2}$/) // today, to be changed for something done earlier
  await ui.type(within(dialog).getByLabelText('Title'), 'Tyres')
  await ui.type(within(dialog).getByLabelText(/^Every \(kilometers\)/), '40000')
  await ui.clear(within(dialog).getByLabelText(/^Odometer on that day/))
  await ui.type(within(dialog).getByLabelText(/^Odometer on that day/), '71000') // the person knows better
  await ui.click(within(dialog).getByRole('button', { name: 'Add recurring expense' }))

  await screen.findByText('Tyres')
  expect(state.calls.AddRecurringExpense).toEqual([{ input: expect.objectContaining({ title: 'Tyres', lastDoneOdometer: 71000, lastDoneDate: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/) }) }])
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
  expect(within(dialog).queryByLabelText(/^Odometer on that day/)).not.toBeInTheDocument()
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
  await ui.type(within(dialog).getByLabelText(/^Odometer on that day/), '1')

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

const doneDialog = async (ui: ReturnType<typeof userEvent.setup>, title: string) => {
  await ui.click(screen.getByRole('button', { name: `Mark ${title} as done` }))
  const dialog = await screen.findByRole('dialog', { name: `Mark as done: ${title}` })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))
  return dialog
}

it('marks what was done at one visit: the clicked one and the due ones start ticked, and one amount covers all of them', async () => {
  const { ui, state } = setup()
  await row('Oil change')

  const dialog = await doneDialog(ui, 'Oil change')
  const visit = within(within(dialog).getByRole('group', { name: 'Done at this visit' }))
  expect(visit.getByRole('checkbox', { name: 'Oil change' })).toBeChecked()
  expect(visit.getByRole('checkbox', { name: 'Tyres' })).toBeChecked() // overdue: probably done at the same visit
  expect(within(dialog).getByText('2 ticked')).toBeInTheDocument()
  expect(within(dialog).getByLabelText('Expense title')).toHaveValue('Tyres, Oil change')
  expect(within(dialog).getByLabelText('Expense category (optional)')).toHaveValue('Service')
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '62000')
  await ui.type(within(dialog).getByLabelText('Amount (optional)'), '35000')
  await ui.click(within(dialog).getByRole('button', { name: 'Mark as done' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.MarkRecurringExpensesDone).toEqual([
    {
      input: {
        ids: ['rc2', 'rc1'],
        date: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/),
        odometer: 62000,
        amount: 35000,
        currency: 'HUF',
        title: 'Tyres, Oil change',
        category: 'Service',
        photoIds: [],
      },
    },
  ])
})

it('an unticked schedule stays as it is, and a title typed by hand is kept when the ticks change', async () => {
  const { ui, state } = setup()
  await row('Oil change')
  const dialog = await doneDialog(ui, 'Oil change')

  await ui.click(within(dialog).getByRole('checkbox', { name: 'Tyres' }))
  expect(within(dialog).getByLabelText('Expense title')).toHaveValue('Oil change')
  await ui.clear(within(dialog).getByLabelText('Expense title'))
  await ui.type(within(dialog).getByLabelText('Expense title'), 'Yearly service')
  await ui.click(within(dialog).getByRole('checkbox', { name: 'Tyres' }))
  expect(within(dialog).getByLabelText('Expense title')).toHaveValue('Yearly service')
  await ui.click(within(dialog).getByRole('checkbox', { name: 'Tyres' }))
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '62000')
  await ui.type(within(dialog).getByLabelText('Amount (optional)'), '35000')
  await ui.click(within(dialog).getByRole('button', { name: 'Mark as done' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.MarkRecurringExpensesDone).toEqual([{ input: expect.objectContaining({ ids: ['rc1'], title: 'Yearly service' }) }])
})

it('the prefilled category can be cleared: the expense then has none', async () => {
  const { ui, state } = setup()
  await row('Oil change')
  const dialog = await doneDialog(ui, 'Oil change')

  await ui.clear(within(dialog).getByLabelText('Expense category (optional)'))
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '62000')
  await ui.type(within(dialog).getByLabelText('Amount (optional)'), '35000')
  await ui.click(within(dialog).getByRole('button', { name: 'Mark as done' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.MarkRecurringExpensesDone).toEqual([{ input: expect.objectContaining({ category: '' }) }])
})

it("a row's own Done leaves the selection in the list alone", async () => {
  const { ui, state } = setup(fakeVehicle(), [fakeRecurring({ kind: 'TIME', intervalDistance: null, lastDoneOdometer: null }), fakeRecurring({ id: 'rc4', title: 'Wipers', kind: 'TIME', intervalDistance: null, lastDoneOdometer: null })])
  await row('Oil change')
  await ui.click(screen.getByRole('checkbox', { name: 'Select Wipers' }))

  const dialog = await doneDialog(ui, 'Oil change')
  await ui.click(within(dialog).getByRole('button', { name: 'Mark as done' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.MarkRecurringExpensesDone).toEqual([{ input: expect.objectContaining({ ids: ['rc1'] }) }])
  expect(screen.getByRole('checkbox', { name: 'Select Wipers' })).toBeChecked()
})

it('without an amount only the schedules move on: nothing is required beyond the day', async () => {
  const { ui, state } = setup(fakeVehicle(), [fakeRecurring({ kind: 'TIME', intervalDistance: null, lastDoneOdometer: null })])
  await row('Oil change')

  const dialog = await doneDialog(ui, 'Oil change')
  expect(within(dialog).queryByRole('switch')).not.toBeInTheDocument()
  await ui.click(within(dialog).getByRole('button', { name: 'Mark as done' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.MarkRecurringExpensesDone).toEqual([
    { input: { ids: ['rc1'], date: expect.any(String), odometer: null, amount: null, currency: null, title: 'Oil change', category: 'Service', photoIds: [] } },
  ])
})

it('a dialog with nothing ticked says so instead of saving', async () => {
  const { ui, state } = setup()
  await row('Oil change')
  const dialog = await doneDialog(ui, 'Oil change')

  await ui.click(within(dialog).getByRole('checkbox', { name: 'Tyres' }))
  await ui.click(within(dialog).getByRole('checkbox', { name: 'Oil change' }))
  await ui.click(within(dialog).getByRole('button', { name: 'Mark as done' }))

  expect(within(dialog).getByRole('alert')).toHaveTextContent('Tick at least one recurring expense.')
  expect(state.calls.MarkRecurringExpensesDone).toBeUndefined()
})

it('several schedules can be selected in the list and marked done together', async () => {
  const { ui, state } = setup()
  await row('Oil change')
  const markSelected = () => screen.getByRole('button', { name: /selected as done/ })
  expect(markSelected()).toBeDisabled()

  await ui.click(screen.getByRole('checkbox', { name: 'Select Oil change' }))
  expect(markSelected()).toHaveAccessibleName('Mark 1 selected as done')
  await ui.click(markSelected())
  const dialog = await screen.findByRole('dialog', { name: 'Mark as done' })
  expect(within(dialog).getByRole('checkbox', { name: 'Oil change' })).toBeChecked()
  expect(within(dialog).getByRole('checkbox', { name: 'Tyres' })).not.toBeChecked() // only what was selected
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '62000')
  await ui.click(within(dialog).getByRole('button', { name: 'Mark as done' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.MarkRecurringExpensesDone).toEqual([{ input: expect.objectContaining({ ids: ['rc1'], amount: null }) }])
  await waitFor(() => expect(screen.getByRole('checkbox', { name: 'Select Oil change' })).not.toBeChecked()) // the selection is spent

  await ui.click(screen.getByRole('checkbox', { name: 'Select all recurring expenses' }))
  expect(markSelected()).toHaveAccessibleName('Mark 2 selected as done')
})

it('a schedule that refuses names itself, and nothing is saved', async () => {
  const { ui, state } = setup()
  state.failWith = { message: 'Tyres: odometer too low', key: 'recurring.odometerBelowLast', args: { title: 'Tyres', last: '63,000' } }
  await row('Oil change')
  const dialog = await doneDialog(ui, 'Oil change')
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '62000')
  await ui.click(within(dialog).getByRole('button', { name: 'Mark as done' }))

  expect(await within(dialog).findByRole('alert')).toHaveTextContent('For Tyres the odometer cannot be lower than 63,000, where it was last done.')
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
