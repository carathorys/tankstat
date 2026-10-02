import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, http, HttpResponse } from 'msw'
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

it('uploads photos picked for a new expense right away, and saving the expense attaches them', async () => {
  const photos = fakePhotoStore()
  const { ui, state } = setupExpenses(photos)
  const dialog = await openAddExpense(ui)
  await ui.type(within(dialog).getByLabelText('Title'), 'Tyres')
  await ui.type(within(dialog).getByLabelText('Amount'), '120000')

  await ui.upload(camera(dialog), photo('camera.png', [1, 2, 3]))
  await ui.upload(library(dialog), [photo('a.png', [1, 2, 3, 4]), photo('b.png', [1, 2, 3, 4, 5])])

  expect(await within(dialog).findAllByRole('button', { name: /^Remove photo/ })).toHaveLength(3)
  expect(within(dialog).getByText(/3 of 10 photos/)).toBeInTheDocument()
  expect(photos.state.draftPuts).toEqual([
    { vehicleId: 'v1', bytes: 3 },
    { vehicleId: 'v1', bytes: 4 },
    { vehicleId: 'v1', bytes: 5 },
  ]) // on the server already, before the expense exists
  expect(within(dialog).getByRole('status', { name: 'Upload status' })).toHaveTextContent('Photos uploaded; they are attached to the entry when you save it.')
  await ui.click(within(dialog).getByRole('button', { name: 'Add expense' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  const newId = state.expenses.find((e) => e.title === 'Tyres')!.id
  expect(state.calls.AddExpense).toEqual([{ input: expect.objectContaining({ title: 'Tyres', photoIds: ['draft1', 'draft2', 'draft3'] }) }])
  expect(photos.photosOf(newId).map((p) => p.id)).toEqual(['draft1', 'draft2', 'draft3'])
  expect(photos.state.puts).toEqual([]) // nothing is sent after the save any more
  expect(photos.state.draftDeletes).toEqual([]) // and the attached drafts are not thrown away
})

it('removing a picked photo deletes its draft', async () => {
  const photos = fakePhotoStore()
  const { ui } = setupExpenses(photos)
  const dialog = await openAddExpense(ui)
  await ui.upload(library(dialog), [photo('a.png'), photo('b.png')])

  await ui.click((await within(dialog).findAllByRole('button', { name: /^Remove photo/ }))[0])

  expect(within(dialog).getAllByRole('button', { name: /^Remove photo/ })).toHaveLength(1)
  await waitFor(() => expect(photos.state.draftDeletes).toEqual(['draft1']))
  expect(photos.state.deletes).toEqual([])
})

it('closing the dialog without saving deletes the photos uploaded for it', async () => {
  const photos = fakePhotoStore()
  const { ui, state } = setupExpenses(photos)
  const dialog = await openAddExpense(ui)
  await ui.upload(library(dialog), [photo('a.png'), photo('b.png')])
  await within(dialog).findAllByRole('button', { name: /^Remove photo/ })

  await ui.click(within(dialog).getByRole('button', { name: 'Cancel' }))

  await waitFor(() => expect(photos.state.draftDeletes).toEqual(['draft1', 'draft2']))
  expect(state.calls.AddExpense).toBeUndefined()
  const again = await openAddExpense(ui)
  expect(within(again).getByText(/0 of 10 photos/)).toBeInTheDocument()
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
  expect(photos.state.draftPuts).toEqual([]) // a saved expense takes its photos directly
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

it('marks a photo that could not be uploaded, and the expense waits until it is tried again or removed', async () => {
  const photos = fakePhotoStore()
  photos.state.failWith = { key: 'photo.tooManyDrafts', args: { max: 20 } }
  const { ui, state } = setupExpenses(photos)
  const dialog = await openAddExpense(ui)
  await ui.type(within(dialog).getByLabelText('Title'), 'Tyres')
  await ui.type(within(dialog).getByLabelText('Amount'), '120000')

  await ui.upload(library(dialog), photo())

  expect(await within(dialog).findByRole('alert')).toHaveTextContent('At most 20 photos can wait for an entry to be saved.')
  expect(within(dialog).getByText('Not uploaded')).toBeInTheDocument()
  expect(within(dialog).getByText(/Try them again or remove them/)).toBeInTheDocument()
  expect(within(dialog).getByRole('button', { name: 'Add expense' })).toBeDisabled() // it would be left out silently

  photos.state.failWith = undefined
  await ui.click(within(dialog).getByRole('button', { name: 'Upload photo 1 again' }))

  await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Add expense' })).toBeEnabled())
  expect(within(dialog).queryByText('Not uploaded')).not.toBeInTheDocument()
  expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
  await ui.click(within(dialog).getByRole('button', { name: 'Add expense' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.AddExpense).toEqual([{ input: expect.objectContaining({ photoIds: ['draft1'] }) }])
})

it('says so instead of closing when the server could not attach a photo, and the entry is not saved twice', async () => {
  const photos = fakePhotoStore()
  photos.state.unattachable.add('draft2')
  const { ui, state } = setupExpenses(photos)
  const dialog = await openAddExpense(ui)
  await ui.type(within(dialog).getByLabelText('Title'), 'Tyres')
  await ui.type(within(dialog).getByLabelText('Amount'), '120000')
  await ui.upload(library(dialog), [photo('a.png'), photo('b.png')])
  await within(dialog).findAllByRole('button', { name: /^Remove photo/ })

  await ui.click(within(dialog).getByRole('button', { name: 'Add expense' }))

  expect(await within(dialog).findByRole('alert')).toHaveTextContent('The entry was saved, but 1 photo could not be attached. Add it again by editing the entry.')
  expect(within(dialog).queryByLabelText('Title')).not.toBeInTheDocument() // the form is gone, so it cannot be saved a second time
  await ui.click(within(dialog).getByRole('button', { name: 'Done' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.AddExpense).toHaveLength(1)
  expect(photos.state.draftDeletes).toEqual([]) // the left-over draft is not the dialog's to delete any more; it expires
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
  await within(dialog).findByRole('button', { name: 'Remove photo 1' })

  await ui.click(within(dialog).getByRole('button', { name: 'Add refuelling' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  const newId = state.logs.find((l) => l.odometer === 13000)!.id
  expect(photos.state.draftPuts).toEqual([{ vehicleId: 'v1', bytes: 2 }])
  expect(photos.photosOf(newId).map((p) => p.id)).toEqual(['draft1'])
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
  expect(photos.photosOf(state.expenses.find((e) => e.title === 'Tyres')!.id)).toHaveLength(1)
})

it('waits for photos that are still being uploaded before the expense can be saved', async () => {
  const photos = fakePhotoStore()
  const { ui, state } = setupExpenses(photos)
  const dialog = await openAddExpense(ui)
  await ui.type(within(dialog).getByLabelText('Title'), 'Tyres')
  await ui.type(within(dialog).getByLabelText('Amount'), '120000')
  let release!: () => void
  const gate = new Promise<void>((resolve) => (release = resolve))
  server.use(
    http.put('/media/vehicles/:vehicleId/photo-drafts', async () => {
      await gate
      photos.state.drafts.push({ id: 'slow1', url: '/media/slow1' }) // the server has it now, so saving can attach it
      return HttpResponse.json({ id: 'slow1', url: '/media/slow1' })
    }),
  )

  await ui.upload(library(dialog), photo())

  expect(await within(dialog).findByText('Uploading…')).toBeInTheDocument()
  expect(within(dialog).getByRole('button', { name: 'Add expense' })).toBeDisabled()
  release()
  await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Add expense' })).toBeEnabled())
  await ui.click(within(dialog).getByRole('button', { name: 'Add expense' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.AddExpense).toEqual([{ input: expect.objectContaining({ photoIds: ['slow1'] }) }])
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
  expect(photos.state.draftPuts).toEqual([]) // it was never uploaded
})

it('deletes a photo whose upload finishes after its dialog was closed', async () => {
  const photos = fakePhotoStore()
  const { ui } = setupExpenses(photos)
  const dialog = await openAddExpense(ui)
  let release!: () => void
  const gate = new Promise<void>((resolve) => (release = resolve))
  server.use(
    http.put('/media/vehicles/:vehicleId/photo-drafts', async () => {
      await gate
      return HttpResponse.json({ id: 'late1', url: '/media/late1' })
    }),
  )
  await ui.upload(library(dialog), photo())
  await within(dialog).findByText('Uploading…')

  await ui.click(within(dialog).getByRole('button', { name: 'Cancel' }))
  release()

  await waitFor(() => expect(photos.state.draftDeletes).toEqual(['late1']))
})

it('queues photos where crypto.randomUUID does not exist (plain HTTP)', async () => {
  vi.stubGlobal('crypto', { getRandomValues: crypto.getRandomValues.bind(crypto) })
  const { ui } = setupExpenses()
  const dialog = await openAddExpense(ui)

  await ui.upload(library(dialog), [photo('a.png', [1]), photo('b.png', [1, 2])])

  expect(await within(dialog).findAllByRole('button', { name: /^Remove photo/ })).toHaveLength(2)
  expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
})

it('keeps the dialog open and the photos locked while the expense is being saved', async () => {
  const photos = fakePhotoStore()
  const { ui } = setupExpenses(photos)
  const dialog = await openAddExpense(ui)
  await ui.type(within(dialog).getByLabelText('Title'), 'Tyres')
  await ui.type(within(dialog).getByLabelText('Amount'), '120000')
  await ui.upload(library(dialog), photo('a.png', [1, 2, 3]))
  expect(await within(dialog).findAllByRole('button', { name: /^Remove photo/ })).toHaveLength(1)

  let release!: () => void
  const gate = new Promise<void>((resolve) => (release = resolve))
  server.use(
    graphql.mutation('AddExpense', async () => {
      await gate
      return HttpResponse.json({ data: { addExpense: { id: 'e9', photos: [{ id: 'draft1' }] } } })
    }),
  )
  await ui.click(within(dialog).getByRole('button', { name: 'Add expense' }))

  await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Add photos' })).toBeDisabled())
  expect(within(dialog).getByRole('button', { name: 'Remove photo 1' })).toBeDisabled()
  await ui.keyboard('{Escape}')
  expect(screen.getByRole('dialog', { name: 'Add expense' })).toBeInTheDocument()

  release()
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(photos.state.draftDeletes).toEqual([])
})
