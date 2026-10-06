import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../src/frontend/App.tsx'
import { fakeLogBackend, fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport, type FakeVehicle } from './mocks.tsx'
import { server } from './server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

const three = () => [fakeVehicle({ id: 'a', name: 'Alpha', licensePlate: null }), fakeVehicle({ id: 'b', name: 'Beta', licensePlate: 'BBB-2' }), fakeVehicle({ id: 'c', name: 'Gamma', licensePlate: null })]

function setup(vehicles: FakeVehicle[]) {
  stubViewport('desktop')
  const backend = fakeVehicleBackend(vehicles)
  server.use(sessionHandler('NONE', () => null), healthHandler, ...backend.handlers, ...fakeLogBackend(vehicles[0], []).handlers)
  renderWithApollo(<App />, '/')
  return { ...backend, ui: userEvent.setup() }
}

const cardNames = () => screen.getAllByRole('link', { name: /^Open / }).map((l) => l.textContent)

it('offers to arrange the vehicles from two on', async () => {
  setup([fakeVehicle()])
  await screen.findByRole('link', { name: 'Open Octavia' })
  expect(screen.queryByRole('button', { name: 'Arrange' })).not.toBeInTheDocument()
})

it('moves vehicles up and down, says where they went, keeps the focus with the row, and saves the whole order', async () => {
  const { ui, state } = setup(three())
  await screen.findByRole('link', { name: 'Open Alpha' })
  expect(cardNames()).toEqual(['Alpha', 'Beta', 'Gamma'])

  await ui.click(screen.getByRole('button', { name: 'Arrange' }))
  const dialog = await screen.findByRole('dialog', { name: 'Arrange vehicles' })
  const list = await within(dialog).findByRole('list', { name: 'Vehicles in display order' })
  expect(within(list).getAllByRole('listitem').map((li) => li.textContent)).toEqual(['1.Alpha', '2.BetaBBB-2', '3.Gamma'])
  expect(within(dialog).getByRole('button', { name: 'Move Alpha up' })).toBeDisabled()
  expect(within(dialog).getByRole('button', { name: 'Move Gamma down' })).toBeDisabled()

  await ui.click(within(dialog).getByRole('button', { name: 'Move Gamma up' }))
  await ui.click(within(dialog).getByRole('button', { name: 'Move Gamma up' }))

  expect(within(list).getAllByRole('listitem').map((li) => li.textContent)).toEqual(['1.Gamma', '2.Alpha', '3.BetaBBB-2'])
  expect(within(dialog).getByText('Gamma is now 1 of 3.')).toBeInTheDocument()
  expect(within(dialog).getByRole('button', { name: 'Move Gamma up' })).toBeDisabled()
  expect(document.activeElement).toBe(within(dialog).getByRole('button', { name: 'Move Gamma down' })) // the focus stayed with the row

  await ui.click(within(dialog).getByRole('button', { name: 'Save order' }))

  await waitFor(() => expect(state.calls.SetVehicleOrder).toEqual([{ vehicleIds: ['c', 'a', 'b'] }]))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  await waitFor(() => expect(cardNames()).toEqual(['Gamma', 'Alpha', 'Beta'])) // the home page was asked again
  expect(state.requests.Welcome.length).toBeGreaterThanOrEqual(2)
})

it('Cancel sends nothing, and every opening asks for the list as it is now', async () => {
  const { ui, state } = setup(three())
  await screen.findByRole('link', { name: 'Open Alpha' })

  await ui.click(screen.getByRole('button', { name: 'Arrange' }))
  let dialog = await screen.findByRole('dialog', { name: 'Arrange vehicles' })
  await ui.click(await within(dialog).findByRole('button', { name: 'Move Beta up' }))
  await ui.click(within(dialog).getByRole('button', { name: 'Cancel' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(state.calls.SetVehicleOrder).toBeUndefined()

  await ui.click(screen.getByRole('button', { name: 'Arrange' }))
  dialog = await screen.findByRole('dialog', { name: 'Arrange vehicles' })
  const list = await within(dialog).findByRole('list', { name: 'Vehicles in display order' })
  expect(within(list).getAllByRole('listitem').map((li) => li.textContent)).toEqual(['1.Alpha', '2.BetaBBB-2', '3.Gamma']) // the unsaved move is gone
  expect(state.requests.ArrangeVehicles).toBe(2)
})

it('the buttons work from the keyboard', async () => {
  const { ui } = setup(three())
  await screen.findByRole('link', { name: 'Open Alpha' })
  await ui.click(screen.getByRole('button', { name: 'Arrange' }))
  const dialog = await screen.findByRole('dialog', { name: 'Arrange vehicles' })
  const down = await within(dialog).findByRole('button', { name: 'Move Alpha down' })

  down.focus()
  await ui.keyboard('{Enter}')

  expect(within(dialog).getByText('Alpha is now 2 of 3.')).toBeInTheDocument()
  expect(document.activeElement).toBe(within(dialog).getByRole('button', { name: 'Move Alpha down' }))
})
