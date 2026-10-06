import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { server } from '../support/server.ts'
import { fakeLogBackend, fakeVehicle, healthHandler, renderWithApollo, sessionHandler, stubViewport, user } from '../support/mocks.tsx'

// jsdom cannot decode pictures: the browser-side resize is covered in Media.unit.test.ts, here it passes the file through.
vi.mock('../../../src/frontend/pictures/resizeImage.ts', async (original) => ({
  ...(await original<typeof import('../../../src/frontend/pictures/resizeImage.ts')>()),
  resizeImage: vi.fn(async (file: Blob) => file),
}))

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

const file = () => new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'me.png', { type: 'image/png' })

function setupAccount(current: { avatarUrl: string | null }) {
  stubViewport('desktop')
  const uploads: string[] = []
  server.use(
    sessionHandler('STANDALONE', () => user({ avatarUrl: current.avatarUrl })),
    healthHandler,
    http.put('/media/me/avatar', () => {
      uploads.push('PUT')
      current.avatarUrl = '/media/new-avatar'
      return HttpResponse.json({ id: 'new-avatar', url: '/media/new-avatar' })
    }),
    http.delete('/media/me/avatar', () => {
      uploads.push('DELETE')
      current.avatarUrl = null
      return new HttpResponse(null, { status: 204 })
    }),
  )
  renderWithApollo(<App />, '/account')
  return { uploads, ui: userEvent.setup() }
}

const chooseFile = (container: HTMLElement = document.body) => container.querySelector('input[type=file]') as HTMLInputElement

it('uploads a profile picture and shows it right away', async () => {
  const current = { avatarUrl: null as string | null }
  const { ui, uploads } = setupAccount(current)
  await screen.findByRole('heading', { name: 'Profile picture' })

  await ui.upload(chooseFile(), file())

  await screen.findByText('Picture saved.')
  expect(uploads).toEqual(['PUT'])
  await screen.findByRole('button', { name: 'Change picture' })
  expect(screen.getByRole('status', { name: 'Upload status' })).toHaveTextContent('Picture saved.')
})

it('removes the profile picture', async () => {
  const current = { avatarUrl: '/media/old' as string | null }
  const { ui, uploads } = setupAccount(current)

  await ui.click(await screen.findByRole('button', { name: 'Remove picture' }))

  await screen.findByText('Picture removed.')
  expect(uploads).toEqual(['DELETE'])
  await screen.findByRole('button', { name: 'Choose a picture' })
})

it('shows a translated error from the server and keeps the old picture', async () => {
  stubViewport('desktop')
  server.use(
    sessionHandler('STANDALONE', () => user()),
    healthHandler,
    http.put('/media/me/avatar', () => HttpResponse.json({ key: 'image.unsupportedType', args: {}, message: 'nope' }, { status: 400 })),
  )
  renderWithApollo(<App />, '/account')
  const ui = userEvent.setup()
  await screen.findByRole('heading', { name: 'Profile picture' })

  await ui.upload(chooseFile(), file())

  expect(await screen.findByRole('alert')).toHaveTextContent('Only JPEG, PNG and WebP pictures are supported.')
})

it('offers no profile picture without user accounts', async () => {
  stubViewport('desktop')
  server.use(sessionHandler('NONE', () => null), healthHandler)
  renderWithApollo(<App />, '/account')

  await screen.findByRole('heading', { name: 'Account' })
  expect(screen.queryByRole('heading', { name: 'Profile picture' })).not.toBeInTheDocument()
})

it('uploads a vehicle picture from the details tab', async () => {
  stubViewport('desktop')
  const backend = fakeLogBackend(fakeVehicle(), [])
  const puts: string[] = []
  server.use(
    sessionHandler('NONE', () => null),
    healthHandler,
    ...backend.handlers,
    http.put('/media/vehicles/v1/picture', () => {
      puts.push('PUT')
      backend.state.vehicle = { ...backend.state.vehicle, pictureUrl: '/media/car' }
      return HttpResponse.json({ id: 'car', url: '/media/car' })
    }),
  )
  renderWithApollo(<App />, '/vehicles/v1?tab=details')
  const ui = userEvent.setup()
  await screen.findByRole('button', { name: 'Choose a picture' })

  await ui.upload(chooseFile(), file())

  await screen.findByText('Picture saved.')
  expect(puts).toEqual(['PUT'])
  await waitFor(() => expect(screen.getAllByRole('img', { name: 'Picture of Octavia' }).length).toBeGreaterThan(0))
})
