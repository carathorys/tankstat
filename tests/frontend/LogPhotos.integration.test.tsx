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
  fakeRefueling,
  fakeVehicle,
  healthHandler,
  renderWithApollo,
  sessionHandler,
  stubViewport,
} from './mocks.tsx'

// jsdom cannot decode pictures: the browser-side resize is covered in Media.unit.test.ts, here it passes the file through.
vi.mock('../../src/frontend/pictures/resizeImage.ts', async (original) => ({
  ...(await original<typeof import('../../src/frontend/pictures/resizeImage.ts')>()),
  resizeImage: vi.fn(async (file: Blob) => file),
}))

beforeAll(() => {
  server.listen({ onUnhandledRequest: 'error' })
  // jsdom has no object URLs (the previews of photos that are not saved yet)
  let n = 0
  URL.createObjectURL = () => `blob:preview-${n++}`
  URL.revokeObjectURL = () => undefined
})
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

const photo = (name = 'receipt.png', bytes = [0x89, 0x50, 0x4e, 0x47]) => new File([new Uint8Array(bytes)], name, { type: 'image/png' })
const library = (within_: HTMLElement) => within(within_).getByTestId('photo-library') as HTMLInputElement
const camera = (within_: HTMLElement) => within(within_).getByTestId('photo-camera') as HTMLInputElement

function setupExpenses(photos = fakePhotoStore()) {
  stubViewport('desktop')
  const backend = fakeExpenseBackend(fakeVehicle(), [fakeExpense({ id: 'e1', title: 'Oil change' })], photos)
  server.use(sessionHandler('NONE', () => null), healthHandler, ...backend.handlers)
  renderWithApollo(<App />, '/vehicles/v1?tab=expenses')
  return { ...backend, ui: userEvent.setup() }
}

function setupRefuelings(photos = fakePhotoStore()) {
  stubViewport('desktop')
  const backend = fakeLogBackend(fakeVehicle(), [fakeRefueling({ id: 'r1' })], photos)
  server.use(sessionHandler('NONE', () => null), healthHandler, ...backend.handlers)
  renderWithApollo(<App />, '/vehicles/v1?tab=refuelings')
  return { ...backend, ui: userEvent.setup() }
}

async function openAddExpense(ui: ReturnType<typeof userEvent.setup>) {
  await screen.findByText('Oil change')
  await ui.click(screen.getByRole('button', { name: 'Add expense' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add expense' })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))
  return dialog
}

it('offers the phone camera and the library as two separate ways to add photos', async () => {
  const { ui } = setupExpenses()
  const dialog = await openAddExpense(ui)

  expect(within(dialog).getByRole('group', { name: 'Photos' })).toBeInTheDocument()
  expect(camera(dialog)).toHaveAttribute('capture', 'environment') // opens the camera right away on a phone
  expect(camera(dialog)).not.toHaveAttribute('multiple')
  expect(library(dialog)).toHaveAttribute('multiple')
  expect(library(dialog)).not.toHaveAttribute('capture')
  expect(within(dialog).getByRole('button', { name: 'Take photo' })).toBeEnabled()
  expect(within(dialog).getByRole('button', { name: 'Add photos' })).toBeEnabled()
})

it('adds an expense with photos chosen in the same dialog: they are sent once the expense is saved', async () => {
  const photos = fakePhotoStore()
  const { ui, state } = setupExpenses(photos)
  const dialog = await openAddExpense(ui)
  await ui.type(within(dialog).getByLabelText('Title'), 'Tyres')
  await ui.type(within(dialog).getByLabelText('Amount'), '120000')

  await ui.upload(camera(dialog), photo('camera.png', [1, 2, 3]))
  await ui.upload(library(dialog), [photo('a.png', [1, 2, 3, 4]), photo('b.png', [1, 2, 3, 4, 5])])

  expect(await within(dialog).findAllByRole('button', { name: /^Remove photo/ })).toHaveLength(3)
  expect(within(dialog).getByText(/3 of 10 photos/)).toBeInTheDocument()
  expect(photos.state.puts).toEqual([]) // nothing is sent before the expense exists
  await ui.click(within(dialog).getByRole('button', { name: 'Add expense' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  const newId = state.expenses.find((e) => e.title === 'Tyres')!.id
  expect(photos.state.puts).toEqual([
    { kind: 'expenses', logId: newId, bytes: 3 },
    { kind: 'expenses', logId: newId, bytes: 4 },
    { kind: 'expenses', logId: newId, bytes: 5 },
  ])
})

it('removes a photo that was only chosen so far without calling the server', async () => {
  const photos = fakePhotoStore()
  const { ui } = setupExpenses(photos)
  const dialog = await openAddExpense(ui)
  await ui.upload(library(dialog), [photo('a.png'), photo('b.png')])

  await ui.click((await within(dialog).findAllByRole('button', { name: /^Remove photo/ }))[0])

  expect(within(dialog).getAllByRole('button', { name: /^Remove photo/ })).toHaveLength(1)
  expect(photos.state.deletes).toEqual([])
})

it('shows the photos of an expense when editing, and adds and removes them right away', async () => {
  const photos = fakePhotoStore({ e1: [{ id: 'old1', url: '/media/old1' }, { id: 'old2', url: '/media/old2' }] })
  const { ui } = setupExpenses(photos)
  await screen.findByText('Oil change')

  await ui.click(screen.getByRole('button', { name: 'Edit the expense Oil change' }))
  const dialog = await screen.findByRole('dialog', { name: 'Edit expense' })
  const links = await within(dialog).findAllByRole('link', { name: /^Open photo/ })
  expect(links.map((l) => l.getAttribute('href'))).toEqual(['/media/old1', '/media/old2'])

  await ui.upload(library(dialog), photo('new.png', [9, 9]))
  await waitFor(() => expect(within(dialog).getAllByRole('link', { name: /^Open photo/ })).toHaveLength(3))
  expect(photos.state.puts).toEqual([{ kind: 'expenses', logId: 'e1', bytes: 2 }])
  expect(within(dialog).getByRole('status', { name: 'Upload status' })).toHaveTextContent('Photos saved.')

  await ui.click(within(dialog).getByRole('button', { name: 'Remove photo 1' }))
  await waitFor(() => expect(within(dialog).getAllByRole('link', { name: /^Open photo/ })).toHaveLength(2))
  expect(photos.state.deletes).toEqual([{ kind: 'expenses', logId: 'e1', imageId: 'old1' }])
  expect(within(dialog).getByRole('status', { name: 'Upload status' })).toHaveTextContent('Photo removed.')
})

it('stops at ten photos: the buttons are disabled and a bigger choice is cut off', async () => {
  const ten = Array.from({ length: 9 }, (_, i) => ({ id: `p${i}`, url: `/media/p${i}` }))
  const photos = fakePhotoStore({ e1: ten })
  const { ui } = setupExpenses(photos)
  await screen.findByText('Oil change')
  await ui.click(screen.getByRole('button', { name: 'Edit the expense Oil change' }))
  const dialog = await screen.findByRole('dialog', { name: 'Edit expense' })
  await within(dialog).findAllByRole('link', { name: /^Open photo/ })

  await ui.upload(library(dialog), [photo('a.png'), photo('b.png'), photo('c.png')]) // room for one

  await waitFor(() => expect(within(dialog).getAllByRole('link', { name: /^Open photo/ })).toHaveLength(10))
  expect(photos.state.puts).toHaveLength(1)
  expect(within(dialog).getByRole('button', { name: 'Take photo' })).toBeDisabled()
  expect(within(dialog).getByRole('button', { name: 'Add photos' })).toBeDisabled()
})

it('keeps the saved expense and shows the photos with the error when an upload fails after saving', async () => {
  const photos = fakePhotoStore()
  photos.state.failWith = { key: 'photo.tooMany', args: { max: 10 } }
  const { ui, state } = setupExpenses(photos)
  const dialog = await openAddExpense(ui)
  await ui.type(within(dialog).getByLabelText('Title'), 'Tyres')
  await ui.type(within(dialog).getByLabelText('Amount'), '120000')
  await ui.upload(library(dialog), photo())

  await ui.click(within(dialog).getByRole('button', { name: 'Add expense' }))

  expect(await within(dialog).findByRole('alert')).toHaveTextContent('The entry was saved, but not all photos could be sent. A log can have at most 10 photos.')
  expect(state.calls.AddExpense).toHaveLength(1) // saved exactly once
  expect(within(dialog).queryByLabelText('Title')).not.toBeInTheDocument() // the form is gone, so it cannot be saved a second time
  expect(within(dialog).getByRole('group', { name: 'Photos' })).toBeInTheDocument()

  expect(within(dialog).getByText('Photos not sent yet: 1.')).toBeInTheDocument() // the one that failed is kept for another try
  photos.state.failWith = undefined
  await ui.click(within(dialog).getByRole('button', { name: 'Try again' }))

  await waitFor(() => expect(within(dialog).getAllByRole('link', { name: /^Open photo/ })).toHaveLength(1))
  expect(within(dialog).queryByText(/Photos not sent yet/)).not.toBeInTheDocument()
  expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
  expect(photos.state.puts).toHaveLength(2) // the failed one, then the retry
  await ui.click(within(dialog).getByRole('button', { name: 'Done' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.AddExpense).toHaveLength(1)
})

it('shows a translated server error when a photo is refused while editing', async () => {
  const photos = fakePhotoStore()
  photos.state.failWith = { key: 'image.unsupportedType' }
  const { ui } = setupExpenses(photos)
  await screen.findByText('Oil change')
  await ui.click(screen.getByRole('button', { name: 'Edit the expense Oil change' }))
  const dialog = await screen.findByRole('dialog', { name: 'Edit expense' })

  await ui.upload(library(dialog), photo())

  expect(await within(dialog).findByRole('alert')).toHaveTextContent('Only JPEG, PNG and WebP pictures are supported.')
})

it('refuelings take photos the same way', async () => {
  const photos = fakePhotoStore({ r1: [{ id: 'fuel1', url: '/media/fuel1' }] })
  const { ui } = setupRefuelings(photos)
  await screen.findAllByRole('button', { name: /^Edit the refuelling/ })

  await ui.click((await screen.findAllByRole('button', { name: /^Edit the refuelling/ }))[0])
  const dialog = await screen.findByRole('dialog', { name: 'Edit refuelling' })
  expect((await within(dialog).findByRole('link', { name: 'Open photo 1' })).getAttribute('href')).toBe('/media/fuel1')
  await ui.upload(camera(dialog), photo('pump.png', [4, 4, 4]))

  await waitFor(() => expect(within(dialog).getAllByRole('link', { name: /^Open photo/ })).toHaveLength(2))
  expect(photos.state.puts).toEqual([{ kind: 'refuelings', logId: 'r1', bytes: 3 }])
})

it('adds a refueling with a photo taken in the add dialog', async () => {
  const photos = fakePhotoStore()
  const { ui, state } = setupRefuelings(photos)
  await screen.findAllByRole('button', { name: /^Edit the refuelling/ })
  await ui.click(screen.getByRole('button', { name: 'Add refuelling' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add refuelling' })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))
  await ui.type(within(dialog).getByLabelText(/^Volume/), '40')
  await ui.type(within(dialog).getByLabelText('Total cost'), '20000')
  await ui.clear(within(dialog).getByLabelText(/^Odometer/))
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '13000')
  await ui.upload(camera(dialog), photo('pump.png', [1, 1]))

  await ui.click(within(dialog).getByRole('button', { name: 'Add refuelling' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  const newId = state.logs.find((l) => l.odometer === 13000)!.id
  expect(photos.state.puts).toEqual([{ kind: 'refuelings', logId: newId, bytes: 2 }])
})

/** Makes the next photo "take a while" to be prepared, like a large camera picture; call the result to finish it. */
function slowResize(file: Blob) {
  let finish!: () => void
  vi.mocked(resizeImage).mockImplementationOnce(() => new Promise<Blob>((resolve) => (finish = () => resolve(file))))
  return () => finish()
}

it('waits for photos that are still being prepared before the expense can be saved, so none is left out', async () => {
  const photos = fakePhotoStore()
  const { ui, state } = setupExpenses(photos)
  const dialog = await openAddExpense(ui)
  await ui.type(within(dialog).getByLabelText('Title'), 'Tyres')
  await ui.type(within(dialog).getByLabelText('Amount'), '120000')
  const finish = slowResize(photo('big.png', [1, 2, 3]))

  await ui.upload(camera(dialog), photo('big.png', [1, 2, 3]))

  expect(within(dialog).getByRole('button', { name: 'Add expense' })).toBeDisabled()
  finish()
  await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Add expense' })).toBeEnabled())
  await ui.click(within(dialog).getByRole('button', { name: 'Add expense' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(photos.state.puts).toEqual([{ kind: 'expenses', logId: state.expenses.find((e) => e.title === 'Tyres')!.id, bytes: 3 }])
})

it('does not bring a photo back into a dialog that was closed while it was being prepared', async () => {
  const photos = fakePhotoStore()
  const { ui } = setupExpenses(photos)
  const dialog = await openAddExpense(ui)
  const finish = slowResize(photo('big.png'))
  await ui.upload(library(dialog), photo('big.png'))

  await ui.click(within(dialog).getByRole('button', { name: 'Cancel' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  finish()
  await new Promise((resolve) => setTimeout(resolve, 20))

  const again = await openAddExpense(ui)
  expect(within(again).queryAllByRole('button', { name: /^Remove photo/ })).toHaveLength(0)
  expect(within(again).getByText(/0 of 10 photos/)).toBeInTheDocument()
})
