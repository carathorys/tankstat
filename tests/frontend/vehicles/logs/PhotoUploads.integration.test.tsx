import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../../src/frontend/App.tsx'
import { isKept } from '../../../../src/frontend/offline/changes.ts'
import { connectivity } from '../../../../src/frontend/offline/connectivity.ts'
import { keptPhotos } from '../../../../src/frontend/offline/keptPhotos.ts'
import { outbox } from '../../../../src/frontend/offline/outbox.ts'
import { server } from '../../support/server.ts'
import { fakeExpense, fakeExpenseBackend, fakePhotoStore, fakeVehicle, healthHandler, renderWithApollo, sessionHandler, stubViewport } from '../../support/mocks.tsx'

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
  vi.restoreAllMocks()
})
afterAll(() => server.close())

const photo = () => new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'receipt.png', { type: 'image/png' })
const library = (dialog: HTMLElement) => within(dialog).getByTestId('photo-library') as HTMLInputElement
const status = (dialog: HTMLElement) => within(dialog).getByRole('status', { name: 'Upload status' })
/** The error messages in a dialog (once the connection is lost, the note that the dialog works offline is an alert too). */
const errors = (dialog: HTMLElement) => within(dialog).queryAllByRole('alert').filter((a) => !a.textContent?.startsWith('You are offline.'))

/** Nothing answers: the connection drops while the photo goes up (or is removed). */
const lostConnection = () => HttpResponse.error()

/** The expenses of Octavia: "Oil change" (e1) with one saved photo. */
function setup() {
  stubViewport('desktop')
  const photos = fakePhotoStore({ e1: [{ id: 'old1', url: '/media/old1' }] })
  const backend = fakeExpenseBackend(fakeVehicle(), [fakeExpense({ id: 'e1', title: 'Oil change' })], photos)
  server.use(sessionHandler('NONE', () => null), healthHandler, ...backend.handlers)
  renderWithApollo(<App />, '/vehicles/v1?tab=expenses')
  return { ...backend, photos, ui: userEvent.setup() }
}

async function openAdd(ui: ReturnType<typeof userEvent.setup>) {
  await ui.click(await screen.findByRole('button', { name: 'Add expense' }, { timeout: 10_000 }))
  const dialog = await screen.findByRole('dialog', { name: 'Add expense' })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))
  return dialog
}

async function openEdit(ui: ReturnType<typeof userEvent.setup>) {
  await ui.click(await screen.findByRole('button', { name: 'Edit the expense Oil change' }, { timeout: 10_000 }))
  const dialog = await screen.findByRole('dialog', { name: 'Edit expense' })
  await within(dialog).findByRole('link', { name: 'Open photo 1' })
  return dialog
}

// ---- choosing photos ----------------------------------------------------------------------------------------

it('"Take photo" opens the camera and "Add photos" the library', async () => {
  const { ui } = setup()
  const dialog = await openAdd(ui)
  const opened = vi.spyOn(HTMLInputElement.prototype, 'click').mockImplementation(() => undefined)

  await ui.click(within(dialog).getByRole('button', { name: 'Take photo' }))
  await ui.click(within(dialog).getByRole('button', { name: 'Add photos' }))

  expect(opened.mock.contexts).toEqual([within(dialog).getByTestId('photo-camera'), library(dialog)])
})

it('choosing no file at all changes nothing', async () => {
  const { ui, photos } = setup()
  const dialog = await openAdd(ui)

  fireEvent.change(library(dialog), { target: { files: [] } })

  expect(status(dialog)).toBeEmptyDOMElement()
  expect(within(dialog).queryByRole('link', { name: /^Open photo/ })).not.toBeInTheDocument()
  expect(photos.state.draftPuts).toEqual([])
})

// ---- a new entry's photos -----------------------------------------------------------------------------------

it('a photo whose upload lost the connection is kept on this device instead', async () => {
  const { ui } = setup()
  const dialog = await openAdd(ui)
  server.use(http.put('/media/vehicles/:vehicleId/photo-drafts', lostConnection))

  await ui.upload(library(dialog), photo())

  expect(await within(dialog).findByText('Kept on this device: the photos go up with the entry when it syncs.')).toBeInTheDocument()
  expect(within(dialog).getByText('On this device: uploaded when synced')).toBeInTheDocument()
  expect(within(dialog).queryByText('Not uploaded')).not.toBeInTheDocument()
})

it('a photo that can neither be uploaded nor kept on this device is marked, to be tried again', async () => {
  const { ui } = setup()
  const dialog = await openAdd(ui)
  server.use(http.put('/media/vehicles/:vehicleId/photo-drafts', lostConnection))
  vi.spyOn(keptPhotos, 'keep').mockRejectedValue(new Error('quota exceeded'))

  await ui.upload(library(dialog), photo())

  expect(await within(dialog).findByText('Not uploaded')).toBeInTheDocument()
  expect(within(dialog).getByRole('button', { name: 'Upload photo 1 again' })).toBeInTheDocument()
  expect(within(dialog).getByRole('button', { name: 'Add expense' })).toBeDisabled()
})

it('trying a photo again: refused again it stays marked, and once the connection is lost it is kept on this device', async () => {
  const { ui, photos } = setup()
  photos.state.failWith = { key: 'photo.tooManyDrafts', args: { max: 20 } }
  const dialog = await openAdd(ui)
  await ui.upload(library(dialog), photo())
  expect(await within(dialog).findByText('Not uploaded')).toBeInTheDocument()

  await ui.click(within(dialog).getByRole('button', { name: 'Upload photo 1 again' }))

  await waitFor(() => expect(photos.state.draftPuts).toHaveLength(2))
  expect(await within(dialog).findByRole('alert')).toHaveTextContent('At most 20 photos can wait for an entry to be saved.')
  expect(within(dialog).getByText('Not uploaded')).toBeInTheDocument()

  server.use(http.put('/media/vehicles/:vehicleId/photo-drafts', lostConnection))
  await ui.click(within(dialog).getByRole('button', { name: 'Upload photo 1 again' }))

  await waitFor(() => expect(status(dialog)).toHaveTextContent('Kept on this device: the photos go up with the entry when it syncs.'))
  expect(within(dialog).queryByText('Not uploaded')).not.toBeInTheDocument()
  expect(errors(dialog)).toEqual([])
})

it('removing a photo that was never uploaded has no draft to delete', async () => {
  const { ui, photos } = setup()
  photos.state.failWith = { key: 'photo.tooManyDrafts', args: { max: 20 } }
  const dialog = await openAdd(ui)
  await ui.upload(library(dialog), photo())
  expect(await within(dialog).findByText('Not uploaded')).toBeInTheDocument()

  await ui.click(within(dialog).getByRole('button', { name: 'Remove photo 1' }))

  await waitFor(() => expect(status(dialog)).toHaveTextContent('Photo removed.'))
  expect(within(dialog).queryByRole('link', { name: /^Open photo/ })).not.toBeInTheDocument()
  expect(photos.state.draftDeletes).toEqual([])
})

it('removing a picked photo whose draft cannot be deleted leaves the draft to expire on the server', async () => {
  const { ui, photos } = setup()
  const dialog = await openAdd(ui)
  await ui.upload(library(dialog), photo())
  await waitFor(() => expect(status(dialog)).toHaveTextContent('Photos uploaded'))
  let asked = 0
  server.use(http.delete('/media/photo-drafts/:id', () => (asked++, lostConnection())))

  await ui.click(within(dialog).getByRole('button', { name: 'Remove photo 1' }))

  await waitFor(() => expect(status(dialog)).toHaveTextContent('Photo removed.'))
  await waitFor(() => expect(asked).toBe(1))
  expect(errors(dialog)).toEqual([])
  expect(photos.state.drafts.map((d) => d.id)).toEqual(['draft1']) // still on the server, until it expires
})

it('removing a photo kept on this device removes it from the device, and a failure to do so is not the dialog’s business', async () => {
  const { ui } = setup()
  const dialog = await openAdd(ui)
  server.use(http.put('/media/vehicles/:vehicleId/photo-drafts', lostConnection))
  const kept = vi.spyOn(keptPhotos, 'keep')
  await ui.upload(library(dialog), photo())
  await waitFor(() => expect(status(dialog)).toHaveTextContent('Kept on this device'))
  const key = await kept.mock.results[0].value
  const removed = vi.spyOn(keptPhotos, 'remove').mockRejectedValue(new Error('storage gone'))

  await ui.click(within(dialog).getByRole('button', { name: 'Remove photo 1' }))

  await waitFor(() => expect(status(dialog)).toHaveTextContent('Photo removed.'))
  expect(removed).toHaveBeenCalledWith([key])
  expect(isKept(key)).toBe(true)
  expect(errors(dialog)).toEqual([])
})

it('a photo kept on this device after its dialog was closed is removed from the device, not brought back', async () => {
  const { ui } = setup()
  const dialog = await openAdd(ui)
  server.use(http.put('/media/vehicles/:vehicleId/photo-drafts', lostConnection))
  const keep = keptPhotos.keep.bind(keptPhotos)
  let release!: () => void
  const held = new Promise<void>((resolve) => (release = resolve))
  const kept = vi.spyOn(keptPhotos, 'keep').mockImplementation(async (...args) => (await held, keep(...args)))
  const removed = vi.spyOn(keptPhotos, 'remove')
  await ui.upload(library(dialog), photo())
  await waitFor(() => expect(kept).toHaveBeenCalled())

  await ui.click(within(dialog).getByRole('button', { name: 'Cancel' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  release()

  const key = await kept.mock.results[0].value
  await waitFor(() => expect(removed).toHaveBeenCalledWith([key]))
  const again = await openAdd(ui)
  expect(within(again).queryByRole('link', { name: /^Open photo/ })).not.toBeInTheDocument()
})

// ---- a saved entry's photos ---------------------------------------------------------------------------------

it('a photo added to a saved expense whose upload lost the connection waits on this device', async () => {
  const { ui, photos } = setup()
  const dialog = await openEdit(ui)
  server.use(http.put('/media/expenses/:logId/photos', lostConnection))

  await ui.upload(library(dialog), photo())

  await waitFor(() => expect(outbox.changes).toMatchObject([{ entity: 'expenses', action: 'addPhoto', vehicleId: 'v1', targetId: 'e1' }]))
  expect(isKept((outbox.changes[0].input as { key: string }).key)).toBe(true)
  expect(photos.photosOf('e1').map((p) => p.id)).toEqual(['old1'])
})

it('a saved photo whose removal lost the connection is removed when the expense syncs', async () => {
  const { ui, photos } = setup()
  const dialog = await openEdit(ui)
  server.use(http.delete('/media/expenses/:logId/photos/:imageId', lostConnection))

  await ui.click(within(dialog).getByRole('button', { name: 'Remove photo 1' }))

  await waitFor(() => expect(outbox.changes).toMatchObject([{ entity: 'expenses', action: 'removePhoto', vehicleId: 'v1', targetId: 'e1', input: { imageId: 'old1' } }]))
  expect(photos.photosOf('e1').map((p) => p.id)).toEqual(['old1']) // the server still has it
})

it('a saved photo the server refuses to remove stays, and the dialog says why', async () => {
  const { ui, photos } = setup()
  const dialog = await openEdit(ui)
  server.use(http.delete('/media/expenses/:logId/photos/:imageId', () => HttpResponse.json({ key: 'expense.notFound', args: {}, message: 'refused' }, { status: 404 })))

  await ui.click(within(dialog).getByRole('button', { name: 'Remove photo 1' }))

  expect(await within(dialog).findByRole('alert')).toHaveTextContent('This expense does not exist.')
  expect(within(dialog).getByRole('link', { name: 'Open photo 1' })).toBeInTheDocument()
  expect(outbox.changes).toEqual([])
  expect(photos.photosOf('e1').map((p) => p.id)).toEqual(['old1'])
})

it('a photo added to a saved expense while the server is out of reach and nothing can be kept says so', async () => {
  const { ui, photos } = setup()
  const dialog = await openEdit(ui)
  vi.spyOn(keptPhotos, 'keep').mockResolvedValue(null) // no account's data is open on this device
  act(() => connectivity.failed())

  await ui.upload(library(dialog), photo())

  expect(await within(dialog).findByText('The server cannot be reached right now. This works again once you are back online.')).toBeInTheDocument()
  expect(outbox.changes).toEqual([])
  expect(photos.state.puts).toEqual([])
})
