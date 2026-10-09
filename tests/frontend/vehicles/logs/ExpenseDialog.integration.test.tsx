import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../../src/frontend/App.tsx'
import { isKept } from '../../../../src/frontend/offline/changes.ts'
import { outbox } from '../../../../src/frontend/offline/outbox.ts'
import { server } from '../../support/server.ts'
import { fakeExpense, fakeExpenseBackend, fakePhotoStore, fakeRecognition, fakeVehicle, healthHandler, renderWithApollo, sessionHandler, stubViewport } from '../../support/mocks.tsx'
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

const photo = () => new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'receipt.png', { type: 'image/png' })

function setup({ expenses = [fakeExpense({ id: 'e1', title: 'Oil change' })], recognition = fakeRecognition() } = {}) {
  stubViewport('desktop')
  const photos = fakePhotoStore()
  const backend = fakeExpenseBackend(fakeVehicle(), expenses, photos)
  server.use(sessionHandler('NONE', () => null), healthHandler, ...backend.handlers, ...recognition.handlers)
  renderWithApollo(<App />, '/vehicles/v1?tab=expenses')
  return { ...backend, photos, ui: userEvent.setup() }
}

async function openAdd(ui: ReturnType<typeof userEvent.setup>) {
  await screen.findAllByRole('button', { name: /^Edit the expense/ }, { timeout: 10_000 })
  await ui.click(screen.getByRole('button', { name: 'Add expense' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add expense' })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))
  return dialog
}

it('adds an expense paid on an earlier day and in another currency than the usual one', async () => {
  const { ui, state } = setup()
  const dialog = await openAdd(ui)

  await typeDate(ui, await findDateField('Date', dialog), '2026-09-20')
  await ui.type(within(dialog).getByLabelText('Title'), 'Vignette')
  await ui.type(within(dialog).getByLabelText('Amount'), '15')
  await ui.clear(within(dialog).getByLabelText('Currency'))
  await ui.type(within(dialog).getByLabelText('Currency'), 'eur')
  await ui.click(within(dialog).getByRole('button', { name: 'Add expense' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.AddExpense).toEqual([{ input: expect.objectContaining({ date: '2026-09-20', title: 'Vignette', amount: 15, currency: 'EUR' }) }])
})

it('never overwrites a typed amount: the receipt only offers its own, and "Use it" takes it', async () => {
  const { ui } = setup({ recognition: fakeRecognition({ results: [[{ name: 'TITLE', value: 'Car wash' }, { name: 'TOTAL', value: '25870' }]] }) })
  const dialog = await openAdd(ui)
  await ui.type(within(dialog).getByLabelText('Amount'), '20000')

  await ui.upload(within(dialog).getByTestId('photo-camera'), photo())

  await waitFor(() => expect(within(dialog).getByLabelText('Title')).toHaveValue('Car wash'), READ_WAIT) // an untouched field is filled
  expect(within(dialog).getByLabelText('Amount')).toHaveValue('20000')
  expect(within(dialog).getByText('The photo shows 25870')).toBeInTheDocument()
  await ui.click(within(dialog).getByRole('button', { name: 'Use 25870 from the photo for Amount' }))

  expect(within(dialog).getByLabelText('Amount')).toHaveValue('25870')
  expect(within(dialog).queryByText('The photo shows 25870')).not.toBeInTheDocument()
})

it('an expense still waiting for its photos has no currency yet: its edit dialog starts from the usual one', async () => {
  const { ui } = setup({ expenses: [fakeExpense({ id: 'e1', title: 'Oil change', amount: null, currency: null, reviewState: 'AWAITING_PHOTOS' })] })

  await ui.click(await screen.findByRole('button', { name: 'Edit the expense Oil change' }, { timeout: 10_000 }))
  const dialog = await screen.findByRole('dialog', { name: 'Edit expense' })

  await waitFor(() => expect(within(dialog).getByLabelText('Title')).toHaveValue('Oil change'))
  expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF')
  expect(within(dialog).getByLabelText('Amount')).toHaveValue('')
})

it('a photo kept on this device when its upload lost the connection is read later, so the amount may be left to it', async () => {
  const { ui, state } = setup({ recognition: fakeRecognition({ results: [[{ name: 'TOTAL', value: '25870' }]] }) })
  const dialog = await openAdd(ui)
  server.use(http.put('/media/vehicles/:vehicleId/photo-drafts', () => HttpResponse.error()))

  await ui.upload(within(dialog).getByTestId('photo-camera'), photo())

  expect(await within(dialog).findByText('Kept on this device: the photos go up with the entry when it syncs.')).toBeInTheDocument()
  expect(within(dialog).getByText(/^The photos are read once this reaches the server/)).toBeInTheDocument()
  expect(within(dialog).getByLabelText('Amount')).not.toBeRequired()
  await ui.type(within(dialog).getByLabelText('Title'), 'Car wash')
  await ui.click(within(dialog).getByRole('button', { name: 'Add expense' }))

  expect(await screen.findByText('Saved on this device · syncs when online.')).toBeInTheDocument()
  expect(state.calls.AddExpense).toBeUndefined()
  const [kept] = outbox.changes
  expect(kept).toMatchObject({ entity: 'expenses', action: 'add', vehicleId: 'v1', input: { title: 'Car wash', amount: null } })
  const photoIds = (kept.input as { photoIds: string[] }).photoIds
  expect(photoIds).toHaveLength(1)
  expect(isKept(photoIds[0])).toBe(true)
})
