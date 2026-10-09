import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, beforeEach, expect, it, vi } from 'vitest'
import { axe } from 'vitest-axe'
import App from '../../../src/frontend/App.tsx'
import { createApolloClient } from '../../../src/frontend/apolloClient.ts'
import { isKept } from '../../../src/frontend/offline/changes.ts'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { deviceData } from '../../../src/frontend/offline/deviceData.ts'
import { memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import { keptPhotos } from '../../../src/frontend/offline/keptPhotos.ts'
import { outbox } from '../../../src/frontend/offline/outbox.ts'
import { createPullEngine } from '../../../src/frontend/offline/pull.ts'
import { snapshotKey } from '../../../src/frontend/offline/snapshotPolicy.ts'
import { fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport } from '../support/mocks.tsx'
import { fakeFeed, now } from '../support/offlineFeed.ts'
import { server } from '../support/server.ts'

// jsdom cannot decode pictures: the browser-side resize is covered in Media.unit.test.ts, here it passes the file through.
vi.mock('../../../src/frontend/pictures/resizeImage.ts', async (original) => ({
  ...(await original<typeof import('../../../src/frontend/pictures/resizeImage.ts')>()),
  resizeImage: vi.fn(async (file: Blob) => file),
}))

let uploads: string[]

beforeAll(() => {
  server.listen({ onUnhandledRequest: 'error' })
  let n = 0
  URL.createObjectURL = () => `blob:preview-${n++}`
  URL.revokeObjectURL = () => undefined
})
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

/** Octavia (v1) with two refuellings downloaded for offline use; then the server goes out of reach. Every upload is counted. */
beforeEach(async () => {
  deviceData.reset(memoryStorage())
  stubViewport('desktop')
  const feed = fakeFeed()
  feed.add('a', '2026-09-01')
  feed.add('b', '2026-09-15')
  uploads = []
  server.use(
    sessionHandler('NONE', () => null),
    healthHandler,
    ...fakeVehicleBackend([fakeVehicle()]).handlers,
    ...feed.handlers,
    http.put('/media/*', ({ request }) => (uploads.push(new URL(request.url).pathname), HttpResponse.error())),
    graphql.mutation('LogRefueling', () => HttpResponse.error()),
  )
  const online = renderWithApollo(<App />, '/')
  await screen.findByText('Octavia')
  await deviceData.settled()
  await createPullEngine({ client: createApolloClient('http://localhost/graphql'), now }).run()
  online.unmount()
  connectivity.failed()
})

const photo = (bytes = [0x89, 0x50, 0x4e, 0x47]) => new File([new Uint8Array(bytes)], 'receipt.png', { type: 'image/png' })

async function openRefuelings() {
  renderWithApollo(<App />, '/vehicles/v1?tab=refuelings')
  await waitFor(() => expect(screen.getAllByRole('row')).toHaveLength(3), { timeout: 10_000 })
  return userEvent.setup()
}

it('a photo picked for a new refuelling offline is kept on this device and saved with the refuelling, nothing uploaded', async () => {
  const ui = await openRefuelings()
  await ui.click(screen.getByRole('button', { name: 'Add refuelling' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add refuelling' })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('EUR'))

  await ui.upload(within(dialog).getByTestId('photo-library'), photo())

  // The photo says it is kept (to screen readers in its own item; on screen by a mark on it), one note says what the mark means, and
  // the status says what happened: kept, not uploaded.
  const [item] = await within(dialog).findAllByRole('listitem')
  expect(item).toHaveTextContent('On this device: uploaded when synced')
  expect(within(dialog).getByText('Photos with this mark are kept on this device and uploaded when it syncs.')).toBeInTheDocument()
  expect(within(dialog).getByRole('status', { name: 'Upload status' })).toHaveTextContent('Kept on this device: the photos go up with the entry when it syncs.')
  expect((await axe(dialog, { rules: { 'color-contrast': { enabled: false } } })).violations.map((v) => v.id)).toEqual([])
  await ui.type(within(dialog).getByLabelText(/^Volume/), '30')
  await ui.type(within(dialog).getByLabelText('Total cost'), '90')
  await ui.clear(within(dialog).getByLabelText(/^Odometer/))
  await ui.type(within(dialog).getByLabelText(/^Odometer/), '2000')
  await ui.click(within(dialog).getByRole('button', { name: 'Add refuelling' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

  const [add] = outbox.changes
  const [key] = (add.input?.photoIds ?? []) as string[]
  expect(isKept(key)).toBe(true)
  expect(await keptPhotos.get(key)).toMatchObject({ vehicleId: 'v1', type: 'image/png', reading: { purpose: 'refueling' } })
  expect(uploads).toEqual([])
})

it('with photo reading on (as last heard), the amounts a kept photo would give may be left empty, and the dialog says why', async () => {
  await deviceData.keep(snapshotKey('RecognitionStatus', {}), { recognitionStatus: { __typename: 'RecognitionStatusInfo', available: true } })
  const ui = await openRefuelings()
  await ui.click(screen.getByRole('button', { name: 'Add refuelling' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add refuelling' })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('EUR'))

  await ui.upload(within(dialog).getByTestId('photo-library'), photo())

  expect(await within(dialog).findByText(/^The photos are read once this reaches the server/)).toBeInTheDocument()
  await ui.click(within(dialog).getByRole('button', { name: 'Add refuelling' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(outbox.changes[0].input).toMatchObject({ volume: null, totalCost: null })
})

it('a photo added to a saved refuelling offline waits on this device, shows in the dialog, and taken back it is gone', async () => {
  const ui = await openRefuelings()
  const row = screen.getAllByRole('row')[1]
  await ui.click(within(row).getByRole('button', { name: /^Edit the refuelling of/ }))
  const dialog = await screen.findByRole('dialog', { name: /Edit refuelling/ })

  await ui.upload(await within(dialog).findByTestId('photo-library'), photo())

  expect(await within(dialog).findByText('On this device: uploaded when synced')).toBeInTheDocument()
  expect(within(dialog).getByRole('status', { name: 'Upload status' })).toHaveTextContent('Kept on this device: the photos are added when it syncs.')
  const [change] = outbox.changes
  expect(change).toMatchObject({ entity: 'refuelings', action: 'addPhoto', vehicleId: 'v1' })
  const key = change.input?.key as string

  await ui.click(within(dialog).getByRole('button', { name: 'Remove photo 1' }))

  await waitFor(() => expect(within(dialog).queryByText('On this device: uploaded when synced')).not.toBeInTheDocument())
  expect(outbox.changes).toEqual([])
  expect(await keptPhotos.get(key)).toBeUndefined()
  expect(uploads).toEqual([])
})
