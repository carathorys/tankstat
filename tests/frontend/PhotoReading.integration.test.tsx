import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../src/frontend/App.tsx'
import { resizeImage } from '../../src/frontend/pictures/resizeImage.ts'
import { server } from './server.ts'
import {
  fakeExpense,
  fakeExpenseBackend,
  fakeLogBackend,
  fakePhotoStore,
  fakeRecognition,
  fakeRecurring,
  fakeRecurringBackend,
  fakeRefueling,
  fakeVehicle,
  healthHandler,
  renderWithApollo,
  sessionHandler,
  stubViewport,
  type FakeReadValue,
} from './mocks.tsx'

// jsdom cannot decode pictures: the browser-side resize is covered in Media.unit.test.ts, here it passes the file through.
vi.mock('../../src/frontend/pictures/resizeImage.ts', async (original) => ({
  ...(await original<typeof import('../../src/frontend/pictures/resizeImage.ts')>()),
  resizeImage: vi.fn(async (file: Blob) => file),
}))

beforeAll(() => {
  server.listen({ onUnhandledRequest: 'error' })
  let n = 0
  URL.createObjectURL = () => `blob:preview-${n++}`
  URL.revokeObjectURL = () => undefined
})
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

/** Polls come every 1.5 seconds: a reading that is queued at first arrives with the second one. */
const READ_WAIT = { timeout: 5000 }

const photo = (name = 'receipt.png') => new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], name, { type: 'image/png' })
const camera = (dialog: HTMLElement) => within(dialog).getByTestId('photo-camera') as HTMLInputElement

const fuelReceipt: FakeReadValue[] = [
  { name: 'TOTAL', value: '24687' },
  { name: 'VOLUME', value: '38.52' },
  { name: 'CURRENCY', value: 'EUR' },
  { name: 'DATE', value: '2026-09-17' },
]

function setupRefuelings(recognition = fakeRecognition({ results: [fuelReceipt] })) {
  stubViewport('desktop')
  const photos = fakePhotoStore()
  const backend = fakeLogBackend(fakeVehicle(), [fakeRefueling({ id: 'r1', odometer: 12000 })], photos)
  server.use(sessionHandler('NONE', () => null), healthHandler, ...backend.handlers, ...recognition.handlers)
  renderWithApollo(<App />, '/vehicles/v1?tab=refuelings')
  return { ...backend, photos, recognition, ui: userEvent.setup() }
}

async function openAddRefueling(ui: ReturnType<typeof userEvent.setup>) {
  await screen.findAllByRole('button', { name: /^Edit the refuelling/ })
  await ui.click(screen.getByRole('button', { name: 'Add refuelling' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add refuelling' })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))
  return dialog
}

it('fills a new refuelling from a receipt photo read on the server, marks what it filled and says so', async () => {
  const { ui, photos, recognition, state } = setupRefuelings()
  const dialog = await openAddRefueling(ui)

  await ui.upload(camera(dialog), photo())

  expect(await within(dialog).findByText('Reading…')).toBeInTheDocument() // queued at first
  await waitFor(() => expect(within(dialog).getByLabelText(/^Volume/)).toHaveValue('38.52'), READ_WAIT)
  expect(within(dialog).getByLabelText('Total cost')).toHaveValue('24687')
  expect(within(dialog).getByLabelText('Currency')).toHaveValue('EUR') // the receipt's currency replaces the last one used
  expect(within(dialog).getByLabelText('Date')).toHaveValue('2026-09-17')
  expect(within(dialog).getByLabelText(/^Volume/)).toHaveAccessibleDescription(/Read from the photo; check it\./)
  expect(within(dialog).getAllByText('Read from the photo; check it.')).toHaveLength(4)
  expect(within(dialog).getByRole('status', { name: 'Photo reading status' })).toHaveTextContent(
    'Filled in from the photo: Date, Volume (L), Total cost, Currency. Check them before saving.',
  )
  expect(within(dialog).queryByText('Reading…')).not.toBeInTheDocument()
  expect(photos.state.draftQueries).toEqual(['?form=refueling&locale=en'])
  expect(recognition.state.asked.at(-1)).toEqual(['draft1'])
  expect(vi.mocked(resizeImage)).toHaveBeenCalledWith(expect.anything(), { maxEdge: 1600, format: 'jpeg' }) // a photo that is read goes as JPEG

  await ui.type(within(dialog).getByLabelText(/^Odometer/), '12500')
  await ui.click(within(dialog).getByRole('button', { name: 'Add refuelling' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.LogRefueling).toEqual([
    { input: expect.objectContaining({ date: '2026-09-17', volume: 38.52, totalCost: 24687, currency: 'EUR', odometer: 12500, photoIds: ['draft1'] }) },
  ])
})

it('never overwrites what the user typed: it offers the photo value, and "Use it" takes it', async () => {
  const { ui } = setupRefuelings()
  const dialog = await openAddRefueling(ui)
  await ui.type(within(dialog).getByLabelText(/^Volume/), '40')

  await ui.upload(camera(dialog), photo())

  expect(await within(dialog).findByText('The photo shows 38.52', {}, READ_WAIT)).toBeInTheDocument()
  expect(within(dialog).getByLabelText(/^Volume/)).toHaveValue('40')
  expect(within(dialog).getByLabelText('Total cost')).toHaveValue('24687') // untouched fields are still filled
  await ui.click(within(dialog).getByRole('button', { name: 'Use 38.52 from the photo for Volume (L)' }))

  expect(within(dialog).getByLabelText(/^Volume/)).toHaveValue('38.52')
  expect(within(dialog).queryByText('The photo shows 38.52')).not.toBeInTheDocument()
})

it('fills the odometer from a dashboard photo', async () => {
  const { ui } = setupRefuelings(fakeRecognition({ results: [[{ name: 'ODOMETER', value: '12480' }]], queuedPolls: 0 }))
  const dialog = await openAddRefueling(ui)

  await ui.upload(camera(dialog), photo('dashboard.png'))

  await waitFor(() => expect(within(dialog).getByLabelText(/^Odometer/)).toHaveValue('12480'), READ_WAIT)
  expect(within(dialog).getByRole('status', { name: 'Photo reading status' })).toHaveTextContent('Filled in from the photo: Odometer (kilometers).')
})

it('says so when nothing could be read from the photos', async () => {
  const { ui } = setupRefuelings(fakeRecognition({ results: [[]], queuedPolls: 0 }))
  const dialog = await openAddRefueling(ui)

  await ui.upload(camera(dialog), photo())

  expect(await within(dialog).findByText('Nothing could be read from the photos.', {}, READ_WAIT)).toBeInTheDocument()
  expect(within(dialog).getByLabelText(/^Volume/)).toHaveValue('')
})

it('without photo reading on the server, photos are only uploaded and nothing waits for readings', async () => {
  const { ui, photos, recognition } = setupRefuelings(fakeRecognition({ available: false, results: [fuelReceipt] }))
  const dialog = await openAddRefueling(ui)

  await ui.upload(camera(dialog), photo())

  await within(dialog).findByRole('button', { name: 'Remove photo 1' })
  expect(within(dialog).queryByText('Reading…')).not.toBeInTheDocument()
  expect(recognition.state.asked).toEqual([])
  expect(photos.state.draftQueries).toEqual(['?form=refueling&locale=en']) // the server decides; it reads nothing when it is off
  expect(within(dialog).getByLabelText(/^Volume/)).toHaveValue('')
})

it('asks again after an upload when photo reading was off as the dialog opened, and fills in once the reader is back', async () => {
  const { ui, recognition } = setupRefuelings(fakeRecognition({ available: false, results: [fuelReceipt], queuedPolls: 0 }))
  const dialog = await openAddRefueling(ui)

  await ui.upload(camera(dialog), photo())
  await within(dialog).findByRole('button', { name: 'Remove photo 1' })
  recognition.state.available = true // the reader was only briefly unreachable; the server read the queued photo meanwhile

  await waitFor(() => expect(within(dialog).getByLabelText(/^Volume/)).toHaveValue('38.52'), { timeout: 8000 })
  expect(recognition.state.asked.at(-1)).toEqual(['draft1'])
}, 10_000)

it('fills a new expense from its receipt: the shop becomes the title', async () => {
  stubViewport('desktop')
  const photos = fakePhotoStore()
  const recognition = fakeRecognition({
    results: [[{ name: 'TITLE', value: 'Csillag Autómosó Kft.' }, { name: 'TOTAL', value: '25870' }, { name: 'DATE', value: '2026-09-25' }]],
  })
  const backend = fakeExpenseBackend(fakeVehicle(), [fakeExpense({ id: 'e1', title: 'Oil change' })], photos)
  server.use(sessionHandler('NONE', () => null), healthHandler, ...backend.handlers, ...recognition.handlers)
  renderWithApollo(<App />, '/vehicles/v1?tab=expenses')
  const ui = userEvent.setup()
  await screen.findByText('Oil change')
  await ui.click(screen.getByRole('button', { name: 'Add expense' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add expense' })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))

  await ui.upload(camera(dialog), photo())

  await waitFor(() => expect(within(dialog).getByLabelText('Title')).toHaveValue('Csillag Autómosó Kft.'), READ_WAIT)
  expect(within(dialog).getByLabelText('Amount')).toHaveValue('25870')
  expect(within(dialog).getByLabelText('Date')).toHaveValue('2026-09-25')
  expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF') // the receipt showed none: the last one stays
  expect(photos.state.draftQueries).toEqual(['?form=expense&locale=en'])
})

function setupRecurring(recognition = fakeRecognition({ results: [[{ name: 'ODOMETER', value: '62480' }, { name: 'TOTAL', value: '35000' }]], queuedPolls: 0 })) {
  stubViewport('desktop')
  const photos = fakePhotoStore()
  const expenses = fakeExpenseBackend(fakeVehicle(), [], photos)
  const recurring = fakeRecurringBackend([fakeRecurring()])
  server.use(sessionHandler('NONE', () => null), healthHandler, ...expenses.handlers, ...recurring.handlers, ...recognition.handlers)
  renderWithApollo(<App />, '/vehicles/v1?tab=recurring')
  return { ...recurring, photos, ui: userEvent.setup() }
}

async function openDone(ui: ReturnType<typeof userEvent.setup>) {
  await screen.findByRole('rowheader', { name: /Oil change/ })
  await ui.click(screen.getByRole('button', { name: 'Mark Oil change as done' }))
  const dialog = await screen.findByRole('dialog', { name: 'Mark as done: Oil change' })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))
  return dialog
}

it('marking a schedule done takes the odometer and cost from a photo, which goes with the logged expense', async () => {
  const { ui, state, photos } = setupRecurring()
  const dialog = await openDone(ui)

  await ui.upload(camera(dialog), photo('dashboard.png'))

  await waitFor(() => expect(within(dialog).getByLabelText(/^Odometer/)).toHaveValue('62480'), READ_WAIT)
  expect(within(dialog).getByLabelText('Amount')).toHaveValue('35000')
  expect(photos.state.draftQueries).toEqual(['?form=expense&locale=en'])
  await ui.click(within(dialog).getByRole('button', { name: 'Mark as done' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.MarkRecurringExpenseDone).toEqual([
    { input: expect.objectContaining({ id: 'rc1', odometer: 62480, createExpense: true, amount: 35000, currency: 'HUF', photoIds: ['draft1'] }) },
  ])
  expect(photos.state.draftDeletes).toEqual([])
})

it('marking a schedule done without an expense keeps no photos: they are not sent and are deleted', async () => {
  const { ui, state, photos } = setupRecurring()
  const dialog = await openDone(ui)
  await ui.upload(camera(dialog), photo('dashboard.png'))
  await waitFor(() => expect(within(dialog).getByLabelText(/^Odometer/)).toHaveValue('62480'), READ_WAIT)

  await ui.click(within(dialog).getByRole('switch', { name: 'Also log it as an expense' }))
  expect(within(dialog).getByText('Photos are only kept with a logged expense.')).toBeInTheDocument()
  await ui.click(within(dialog).getByRole('button', { name: 'Mark as done' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.MarkRecurringExpenseDone).toEqual([{ input: expect.objectContaining({ odometer: 62480, createExpense: false, photoIds: [] }) }])
  await waitFor(() => expect(photos.state.draftDeletes).toEqual(['draft1']))
})

it('saves a refuelling while its photo is still being read, leaving the odometer to it, and lists it as waiting', async () => {
  const { ui, state } = setupRefuelings(fakeRecognition({ results: [[{ name: 'ODOMETER', value: '12480' }]], queuedPolls: 1000 }))
  const dialog = await openAddRefueling(ui)
  await ui.upload(camera(dialog), photo('dashboard.png'))
  expect(await within(dialog).findByText('Reading…')).toBeInTheDocument()

  expect(within(dialog).getByLabelText(/^Odometer/)).toHaveAccessibleDescription(/Leave it empty: it is filled in from the photo after saving\./)
  expect(within(dialog).getByLabelText(/^Odometer/)).not.toBeRequired()
  await ui.type(within(dialog).getByLabelText(/^Volume/), '40')
  await ui.type(within(dialog).getByLabelText('Total cost'), '24000')
  await ui.click(within(dialog).getByRole('button', { name: 'Add refuelling' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.LogRefueling).toEqual([{ input: expect.objectContaining({ volume: 40, totalCost: 24000, odometer: null, photoIds: ['draft1'] }) }])
  expect(await screen.findByText('Reading photo…')).toBeInTheDocument()
})

it('without a photo being read, every value of a refuelling is required', async () => {
  const { ui, state } = setupRefuelings()
  const dialog = await openAddRefueling(ui)

  await ui.type(within(dialog).getByLabelText(/^Volume/), '40')
  await ui.type(within(dialog).getByLabelText('Total cost'), '24000')
  await ui.click(within(dialog).getByRole('button', { name: 'Add refuelling' }))

  expect(await within(dialog).findByText('Odometer (kilometers) is required')).toBeInTheDocument()
  expect(within(dialog).queryByText(/Leave it empty/)).not.toBeInTheDocument()
  expect(state.calls.LogRefueling).toBeUndefined()
})

it('a refuelling filled in from its photo is marked in the list, and its edit dialog says what to check', async () => {
  stubViewport('desktop')
  const backend = fakeLogBackend(fakeVehicle(), [fakeRefueling({ id: 'r1', odometer: 12480, reviewState: 'NEEDS_REVIEW', filledFromPhoto: ['ODOMETER'] })])
  server.use(sessionHandler('NONE', () => null), healthHandler, ...backend.handlers, ...fakeRecognition().handlers)
  renderWithApollo(<App />, '/vehicles/v1?tab=refuelings')
  const ui = userEvent.setup()

  expect(await screen.findByText('Check values')).toBeInTheDocument()
  await ui.click(screen.getByRole('button', { name: /^Edit the refuelling/ }))
  const dialog = await screen.findByRole('dialog', { name: 'Edit refuelling' })

  expect(await within(dialog).findByText('Some values were read from its photos (marked below). Check them and save.')).toBeInTheDocument()
  expect(within(dialog).getByLabelText(/^Odometer/)).toHaveAccessibleDescription(/Read from the photo; check it\./)
  expect(within(dialog).getByLabelText(/^Volume/)).not.toHaveAccessibleDescription(/Read from the photo/)
  await ui.click(within(dialog).getByRole('button', { name: 'Save changes' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(backend.state.calls.UpdateRefueling).toEqual([{ input: expect.objectContaining({ id: 'r1', odometer: 12480 }) }])
  await waitFor(() => expect(screen.queryByText('Check values')).not.toBeInTheDocument())
})

it('saves an expense while its receipt is still being read, leaving the amount to it', async () => {
  stubViewport('desktop')
  const photos = fakePhotoStore()
  const recognition = fakeRecognition({ results: [[{ name: 'TOTAL', value: '25870' }]], queuedPolls: 1000 })
  const backend = fakeExpenseBackend(fakeVehicle(), [fakeExpense({ id: 'e1', title: 'Oil change' })], photos)
  server.use(sessionHandler('NONE', () => null), healthHandler, ...backend.handlers, ...recognition.handlers)
  renderWithApollo(<App />, '/vehicles/v1?tab=expenses')
  const ui = userEvent.setup()
  await screen.findByText('Oil change')
  await ui.click(screen.getByRole('button', { name: 'Add expense' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add expense' })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))

  await ui.upload(camera(dialog), photo())
  expect(await within(dialog).findByText('Reading…')).toBeInTheDocument()
  await ui.type(within(dialog).getByLabelText('Title'), 'Car wash')
  await ui.click(within(dialog).getByRole('button', { name: 'Add expense' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(backend.state.calls.AddExpense).toEqual([{ input: expect.objectContaining({ title: 'Car wash', amount: null, photoIds: ['draft1'] }) }])
  expect(await screen.findByText('Reading photo…')).toBeInTheDocument()
})
