import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../src/App.tsx'
import { connectivity } from '../../src/offline/connectivity.ts'
import { outbox } from '../../src/offline/outbox.ts'
import { graphql } from '../support/graphql.ts'
import { fakeExpenseBackend, fakeLogBackend, fakeRecurring, fakeRecurringBackend, fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport, type FakeVehicle } from '../support/mocks.tsx'
import { answered, server } from '../support/server.ts'
import { fakeTyres, tyreSummer, tyreWinter } from '../support/tyres.ts'

// The swap reminder's row on a home card (#292): after the schedules, with Swap and Not this season for whoever may log.

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

const SEASON = '2026-11-01'
const reminder = (over: Partial<FakeVehicle['tyreReminders'][number]> = {}): FakeVehicle['tyreReminders'][number] => ({
  __typename: 'VehicleTyreReminder', setId: 's2', name: 'Continental WinterContact', setSeason: 'WINTER', month: 11, season: SEASON, ...over,
})
const overdue = (id: string, title: string) =>
  fakeRecurring({ id, title, status: { state: 'OVERDUE', limit: 'TIME', dueDate: '2026-01-01', dueOdometer: null, daysLeft: -10, distanceLeft: null } })

function setup(vehicle = fakeVehicle({ tyreReminders: [reminder()] })) {
  stubViewport('desktop')
  const backend = fakeVehicleBackend([vehicle])
  const first = backend.state.vehicles[0]
  const tyres = fakeTyres([tyreSummer, { ...tyreWinter, reminder: SEASON }])
  // The server's card follows a tyre change: the row leaves once its set is fitted or dismissed, and is back when that is undone.
  const leaves = (name: string) => graphql.mutation(name, () => void (first.tyreReminders = []))
  const returns = graphql.mutation('RestoreTyreReminder', () => void (first.tyreReminders = [reminder()]))
  server.use(
    sessionHandler('NONE', () => null), healthHandler, leaves('SwapTyres'), leaves('DismissTyreReminder'), returns,
    ...backend.handlers, ...fakeLogBackend(first, []).handlers, ...fakeExpenseBackend(first, []).handlers, ...fakeRecurringBackend(first.recurring, first).handlers, ...tyres.handlers,
  )
  renderWithApollo(<App />, '/')
  return { backend, tyres, ui: userEvent.setup() }
}

const card = async () => (await screen.findByRole('link', { name: 'Open Octavia' })).closest('li')!
const rows = (li: HTMLElement) => within(within(li).getByRole('list', { name: 'Needs attention' })).getAllByRole('listitem')

it('shows a due set after the schedules, within the card’s three rows', async () => {
  setup(fakeVehicle({ recurring: [overdue('rc1', 'Oil change'), overdue('rc2', 'Inspection')], tyreReminders: [reminder(), reminder({ setId: 's3', name: 'Spare winter' })] }))

  const list = rows(await card())
  expect(list).toHaveLength(3)
  expect(list[0]).toHaveTextContent('Oil change')
  expect(list[1]).toHaveTextContent('Inspection')
  expect(list[2]).toHaveTextContent('Time to swap')
  expect(list[2]).toHaveTextContent('Continental WinterContact')
  expect(within(list[2]).getByRole('button', { name: 'Swap to Continental WinterContact on Octavia' })).toBeInTheDocument()
  expect(within(list[2]).getByRole('button', { name: 'Not this season: Continental WinterContact' })).toBeInTheDocument()
})

it('Swap opens the swap dialog on the home page with the set chosen; swapped, the row leaves and the focus lands on the title', async () => {
  const { ui, tyres, backend } = setup()

  await ui.click(within(await card()).getByRole('button', { name: 'Swap to Continental WinterContact on Octavia' }))
  const dialog = await screen.findByRole('dialog', { name: 'Swap tyres' })
  await ui.click(within(dialog).getByRole('button', { name: 'Swap tyres' }))

  await waitFor(() => expect(tyres.state.calls.SwapTyres).toHaveLength(1))
  expect(tyres.state.calls.SwapTyres[0].input).toMatchObject({ vehicleId: 'v1', setId: 's2' })
  await waitFor(() => expect(screen.queryByText('Time to swap')).not.toBeInTheDocument())
  await waitFor(() => expect(screen.getByRole('link', { name: 'Open Octavia' })).toHaveFocus())
  expect(backend.state.requests.Welcome).toHaveLength(1) // only this card was asked again
  expect(backend.state.requests.VehicleCard).toEqual([{ id: 'v1' }])
})

it('Not this season turns the row off with a toast, and Undo brings it back', async () => {
  const { ui, tyres } = setup()

  await ui.click(within(await card()).getByRole('button', { name: 'Not this season: Continental WinterContact' }))

  await waitFor(() => expect(tyres.state.calls.DismissTyreReminder).toEqual([{ input: { id: 's2', season: SEASON } }]))
  await waitFor(() => expect(screen.queryByText('Time to swap')).not.toBeInTheDocument())
  expect(await screen.findByText('Reminder off for this season.')).toBeInTheDocument()

  await ui.click(screen.getByRole('button', { name: 'Undo' }))
  expect(await screen.findByText('Time to swap')).toBeInTheDocument()
  expect(tyres.state.calls.RestoreTyreReminder).toEqual([{ id: 's2' }])
})

it('someone who may only view sees the row without its buttons', async () => {
  setup(fakeVehicle({ canEdit: false, logAccess: 'VIEW', tyreReminders: [reminder()] }))

  const list = rows(await card())
  expect(list[0]).toHaveTextContent('Time to swap')
  expect(within(list[0]).queryByRole('button')).not.toBeInTheDocument()
})

it('out of reach, Not this season is kept on the device and the row hides at once', async () => {
  const { ui, tyres } = setup()
  // The device holds the car's tyres (a download keeps them; here the swap dialog, opened and cancelled, asked for them).
  await ui.click(within(await card()).getByRole('button', { name: 'Swap to Continental WinterContact on Octavia' }))
  await ui.click(within(await screen.findByRole('dialog', { name: 'Swap tyres' })).getByRole('button', { name: 'Cancel' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  await answered()
  act(() => connectivity.failed())

  await ui.click(within(await card()).getByRole('button', { name: 'Not this season: Continental WinterContact' }))

  await waitFor(() => expect(outbox.changes).toMatchObject([{ entity: 'tyres', action: 'dismissTyreReminder', vehicleId: 'v1', input: { id: 's2', season: SEASON } }]))
  await waitFor(() => expect(screen.queryByText('Time to swap')).not.toBeInTheDocument())
  expect(tyres.state.calls.DismissTyreReminder).toBeUndefined()
})

it('a cancelled swap hands the focus back to the row’s button, and the row stays', async () => {
  const { ui } = setup()
  const swap = within(await card()).getByRole('button', { name: 'Swap to Continental WinterContact on Octavia' })

  await ui.click(swap)
  await ui.click(within(await screen.findByRole('dialog', { name: 'Swap tyres' })).getByRole('button', { name: 'Cancel' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(screen.getByText('Time to swap')).toBeInTheDocument()
  await waitFor(() => expect(swap).toHaveFocus())
})
