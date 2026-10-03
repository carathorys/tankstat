import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../src/frontend/App.tsx'
import { server } from './server.ts'
import {
  fakeExpense,
  fakeExpenseBackend,
  fakeLogBackend,
  fakePhotoStore,
  fakeRecognition,
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
