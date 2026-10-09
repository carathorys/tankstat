import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../../src/frontend/App.tsx'
import { isKept } from '../../../../src/frontend/offline/changes.ts'
import { outbox } from '../../../../src/frontend/offline/outbox.ts'
import { server } from '../../support/server.ts'
import { fakeExpenseBackend, fakePhotoStore, fakeRecognition, fakeRecurring, fakeRecurringBackend, fakeVehicle, gqlError, healthHandler, renderWithApollo, sessionHandler, stubViewport } from '../../support/mocks.tsx'
import { findDateField, typeDate } from '../../support/dates.ts'

// jsdom cannot decode pictures: the browser-side resize is covered in Media.unit.test.ts, here it passes the file through.
vi.mock('../../../../src/frontend/pictures/resizeImage.ts', async (original) => ({
  ...(await original<typeof import('../../../../src/frontend/pictures/resizeImage.ts')>()),
  resizeImage: vi.fn(async (file: Blob) => file),
}))

beforeAll(() => {
  server.listen({ onUnhandledRequest: 'error' })
  let n = 0
  URL.createObjectURL = () => `blob:preview-${n++}`
  URL.revokeObjectURL = () => undefined
})
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

/** Polls come every 1.5 seconds: a reading that is queued at first arrives with the second one. */
const READ_WAIT = { timeout: 5000 }

const photo = () => new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'dashboard.png', { type: 'image/png' })

/** The Recurring tab of Octavia with one combined schedule, "Oil change". */
function setup({ items = [fakeRecurring()], recognition = fakeRecognition() } = {}) {
  stubViewport('desktop')
  const expenses = fakeExpenseBackend(fakeVehicle(), [], fakePhotoStore())
  const recurring = fakeRecurringBackend(items)
  server.use(sessionHandler('NONE', () => null), healthHandler, ...expenses.handlers, ...recurring.handlers, ...recognition.handlers)
  renderWithApollo(<App />, '/vehicles/v1?tab=recurring')
  return { ...recurring, ui: userEvent.setup() }
}

async function openDone(ui: ReturnType<typeof userEvent.setup>, waitForCurrency: string | null = 'HUF') {
  await screen.findByRole('rowheader', { name: /Oil change/ }, { timeout: 10_000 })
  await ui.click(screen.getByRole('button', { name: 'Mark Oil change as done' }))
  const dialog = await screen.findByRole('dialog', { name: 'Mark as done: Oil change' })
  if (waitForCurrency !== null) await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue(waitForCurrency))
  return dialog
}

// ---- marking done ---------------------------------------------------------------------------------------------

it('a visit can be marked done on an earlier day, its cost paid in another currency than the usual one', async () => {
  const { ui, state } = setup()
  const dialog = await openDone(ui)

  await typeDate(ui, await findDateField('Done on', dialog), '2026-09-20')
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '62000')
  await ui.type(within(dialog).getByLabelText('Amount (optional)'), '120')
  await ui.clear(within(dialog).getByLabelText('Currency'))
  await ui.type(within(dialog).getByLabelText('Currency'), 'eur')
  await ui.click(within(dialog).getByRole('button', { name: 'Mark as done' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.MarkRecurringExpensesDone).toEqual([{ input: expect.objectContaining({ ids: ['rc1'], date: '2026-09-20', odometer: 62000, amount: 120, currency: 'EUR' }) }])
})

it('never overwrites a typed amount: the photo only offers its own, and "Use it" takes it', async () => {
  const { ui } = setup({ recognition: fakeRecognition({ results: [[{ name: 'ODOMETER', value: '62480' }, { name: 'TOTAL', value: '35000' }]] }) })
  const dialog = await openDone(ui)
  await ui.type(within(dialog).getByLabelText('Amount (optional)'), '30000')

  await ui.upload(within(dialog).getByTestId('photo-camera'), photo())

  await waitFor(() => expect(within(dialog).getByLabelText(/^Odometer/)).toHaveValue('62480'), READ_WAIT) // an untouched field is filled
  expect(within(dialog).getByLabelText('Amount (optional)')).toHaveValue('30000')
  await ui.click(within(dialog).getByRole('button', { name: 'Use 35000 from the photo for Amount (optional)' }))

  expect(within(dialog).getByLabelText('Amount (optional)')).toHaveValue('35000')
})

it('a vehicle without a reading or a currency yet starts the visit from empty fields', async () => {
  const { ui } = setup()
  server.use(graphql.query('LogDefaults', () => HttpResponse.json({ data: { logDefaults: { lastOdometer: null, lastDate: null, currency: null } } })))

  const dialog = await openDone(ui, null)

  expect(await within(dialog).findByLabelText(/^Odometer/)).toHaveValue('')
  expect(within(dialog).getByLabelText('Currency')).toHaveValue('')
})

it('says why the visit cannot be noted when its starting values cannot be loaded', async () => {
  const { ui, state } = setup()
  server.use(graphql.query('LogDefaults', () => HttpResponse.json(gqlError('Vehicle not found', 'NOT_FOUND', 'vehicle.notFound'))))

  const dialog = await openDone(ui, null)

  expect(await within(dialog).findByText('This vehicle does not exist.')).toBeInTheDocument()
  expect(within(dialog).queryByRole('button', { name: 'Mark as done' })).not.toBeInTheDocument()
  expect(state.calls.MarkRecurringExpensesDone).toBeUndefined()
})

it('a photo kept on this device when its upload lost the connection is read later, so the visit logs its expense without an amount', async () => {
  const { ui, state } = setup({ recognition: fakeRecognition({ results: [[{ name: 'TOTAL', value: '35000' }]] }) })
  const dialog = await openDone(ui)
  server.use(http.put('/media/vehicles/:vehicleId/photo-drafts', () => HttpResponse.error()))

  await ui.upload(within(dialog).getByTestId('photo-camera'), photo())

  expect(await within(dialog).findByText('Kept on this device: the photos go up with the entry when it syncs.')).toBeInTheDocument()
  expect(within(dialog).getByText(/^The photos are read once this reaches the server/)).toBeInTheDocument()
  expect(within(dialog).getByLabelText('Expense title')).toBeRequired() // an expense is logged: the photo may give its amount
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '62000')
  await ui.click(within(dialog).getByRole('button', { name: 'Mark as done' }))

  expect(await screen.findByText('Saved on this device · syncs when online.')).toBeInTheDocument()
  expect(state.calls.MarkRecurringExpensesDone).toBeUndefined()
  const [visit] = outbox.changes
  expect(visit).toMatchObject({ entity: 'recurring', action: 'markDone', targetIds: ['rc1'], input: { ids: ['rc1'], odometer: 62000, amount: null } })
  const input = visit.input as { expenseId: string; photoIds: string[] }
  expect(visit.targetId).toBe(input.expenseId)
  expect(input.photoIds).toHaveLength(1)
  expect(isKept(input.photoIds[0])).toBe(true)
})

// ---- the schedule dialog --------------------------------------------------------------------------------------

it('cancelling a new schedule closes the dialog and saves nothing', async () => {
  const { ui, state } = setup()
  await screen.findByRole('rowheader', { name: /Oil change/ }, { timeout: 10_000 })

  await ui.click(screen.getByRole('button', { name: 'Add recurring expense' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add recurring expense' })
  await ui.type(await within(dialog).findByLabelText('Title'), 'Insurance')
  await ui.click(within(dialog).getByRole('button', { name: 'Cancel' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.AddRecurringExpense).toBeUndefined()
})

it('says why a new schedule cannot be added when its starting values cannot be loaded', async () => {
  const { ui } = setup()
  await screen.findByRole('rowheader', { name: /Oil change/ }, { timeout: 10_000 })
  server.use(graphql.query('LogDefaults', () => HttpResponse.json(gqlError('Vehicle not found', 'NOT_FOUND', 'vehicle.notFound'))))

  await ui.click(screen.getByRole('button', { name: 'Add recurring expense' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add recurring expense' })

  expect(await within(dialog).findByText('This vehicle does not exist.')).toBeInTheDocument()
  expect(within(dialog).queryByLabelText('Title')).not.toBeInTheDocument()
})
