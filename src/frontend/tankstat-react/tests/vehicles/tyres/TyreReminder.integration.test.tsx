import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../src/App.tsx'
import { connectivity } from '../../../src/offline/connectivity.ts'
import { outbox } from '../../../src/offline/outbox.ts'
import { reminderSeasonOf } from '../../../src/tyres/tyreMonths.ts'
import { todayIso } from '../../../src/forms/dates.ts'
import { fakeExpenseBackend, fakeVehicle, healthHandler, renderWithApollo, sessionHandler, stubViewport } from '../../support/mocks.tsx'
import { answered, server } from '../../support/server.ts'
import { fakeTyres, tyreSummer, tyreWinter, type FakeTyreSet } from '../../support/tyres.ts'

// The swap reminder on the Tyres tab and in the set's forms (#292).

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

/** The season of November today, as the server names it: what the due winter set carries. */
const season = reminderSeasonOf(11, todayIso())
const dueWinter: FakeTyreSet = { ...tyreWinter, reminder: season }

function setup(sets: FakeTyreSet[], vehicle = fakeVehicle()) {
  stubViewport('desktop')
  const expenses = fakeExpenseBackend(vehicle, [])
  const tyres = fakeTyres(sets)
  server.use(sessionHandler('NONE', () => null), healthHandler, ...expenses.handlers, ...tyres.handlers)
  renderWithApollo(<App />, '/vehicles/v1?tab=tyres')
  return { ...tyres, ui: userEvent.setup() }
}

const banner = () => screen.queryByText('Time to fit the winter tyres')

it('says above the sets that a stored set’s month has come, and opens the swap with that set chosen', async () => {
  const { ui, state } = setup([tyreSummer, dueWinter])

  expect(await screen.findByText('Time to fit the winter tyres')).toBeInTheDocument()
  expect(screen.getByText('Continental WinterContact is in storage, and you asked to be reminded from November.')).toBeInTheDocument()
  expect(within(screen.getByRole('article', { name: 'Continental WinterContact' })).getByText('Reminder from November.')).toBeInTheDocument()
  expect(within(screen.getByRole('article', { name: 'Michelin Primacy 4' })).queryByText(/Reminder/)).not.toBeInTheDocument() // on the car: nothing to say

  await ui.click(screen.getByRole('button', { name: 'Swap tyres: fit Continental WinterContact' }))
  const dialog = await screen.findByRole('dialog', { name: 'Swap tyres' })
  await ui.click(within(dialog).getByRole('button', { name: 'Swap tyres' }))
  await waitFor(() => expect(state.calls.SwapTyres).toHaveLength(1))
  expect(state.calls.SwapTyres[0].input).toMatchObject({ setId: 's2' })
})

it('Not this season turns it off for the season, and Undo brings it back', async () => {
  const { ui, state } = setup([tyreSummer, dueWinter])

  await ui.click(await screen.findByRole('button', { name: 'Not this season: Continental WinterContact' }))

  await waitFor(() => expect(banner()).not.toBeInTheDocument())
  expect(state.calls.DismissTyreReminder).toEqual([{ input: { id: 's2', season } }])
  expect(await screen.findByText('Reminder off for this season.', { selector: '.MuiSnackbar-root *' })).toBeInTheDocument()
  expect(within(screen.getByRole('article', { name: 'Continental WinterContact' })).getByText('Reminder off for this season.')).toBeInTheDocument()

  await ui.click(screen.getByRole('button', { name: 'Undo' }))
  expect(await screen.findByText('Time to fit the winter tyres')).toBeInTheDocument()
  expect(state.calls.RestoreTyreReminder).toEqual([{ id: 's2' }])
})

it('someone who may only view reads the reminder, without its buttons', async () => {
  setup([tyreSummer, dueWinter], fakeVehicle({ canEdit: false, logAccess: 'VIEW' }))

  expect(await screen.findByText('Time to fit the winter tyres')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /Not this season/ })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /Swap tyres: fit/ })).not.toBeInTheDocument()
})

it('out of reach, Not this season is kept on the device and the reminder hides at once; Undo drops the kept change', async () => {
  const { ui, state } = setup([tyreSummer, dueWinter])
  await screen.findByText('Time to fit the winter tyres')
  await answered()
  act(() => connectivity.failed())

  await ui.click(screen.getByRole('button', { name: 'Not this season: Continental WinterContact' }))

  await waitFor(() => expect(banner()).not.toBeInTheDocument())
  expect(state.calls.DismissTyreReminder).toBeUndefined()
  expect(outbox.changes).toMatchObject([{ entity: 'tyres', action: 'dismissTyreReminder', vehicleId: 'v1', targetId: 's2', input: { id: 's2', season } }])
  expect(within(screen.getByRole('article', { name: 'Continental WinterContact' })).getByText('Changed · not synced')).toBeInTheDocument()

  await ui.click(await screen.findByRole('button', { name: 'Undo' }))
  expect(await screen.findByText('Time to fit the winter tyres')).toBeInTheDocument()
  expect(outbox.changes).toHaveLength(0)
})

it('the month is offered in the add form by the season, follows a change of season until changed, and is sent with the set', async () => {
  const { ui, state } = setup([tyreSummer])

  await ui.click(await screen.findByRole('button', { name: 'Add a tyre set' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add a tyre set' })
  const month = () => within(dialog).getByRole('combobox', { name: 'Remind me to fit them from' })
  expect(month()).toHaveTextContent('March') // a summer set's
  expect(month()).toHaveAccessibleDescription(/A row on the home page, a note on the Tyres tab and a notification/)
  await ui.click(within(dialog).getByRole('radio', { name: 'Winter' }))
  expect(month()).toHaveTextContent('November')
  await ui.click(within(dialog).getByRole('radio', { name: 'All-season' }))
  expect(month()).toHaveTextContent('No reminder')

  await ui.click(month())
  await ui.click(await screen.findByRole('option', { name: 'October' }))
  expect(within(dialog).getByText('Changed from its season’s.'.replace('’', "'"))).toBeInTheDocument()
  await ui.click(within(dialog).getByRole('radio', { name: 'Winter' }))
  expect(month()).toHaveTextContent('October') // changed by hand: it stays

  await ui.type(within(dialog).getByLabelText(/^Name/), 'Nokian')
  await ui.click(within(dialog).getByLabelText('These tyres are new'))
  await ui.click(within(dialog).getByRole('button', { name: 'Save set' }))
  await waitFor(() => expect(state.calls.AddTyreSet).toHaveLength(1))
  expect(state.calls.AddTyreSet[0].input).toMatchObject({ name: 'Nokian', season: 'WINTER', reminderMonth: 10 })
})

it('Edit set opens with the set’s own month, and a set without a reminder says so on its card', async () => {
  const { ui, state } = setup([tyreSummer, { ...tyreWinter, reminderMonth: 0 }])

  const card = await screen.findByRole('article', { name: 'Continental WinterContact' })
  expect(within(card).getByText('No reminder.')).toBeInTheDocument()
  expect(banner()).not.toBeInTheDocument()
  await ui.click(within(card).getByRole('button', { name: 'History' }))
  await ui.click(within(await screen.findByRole('dialog', { name: 'Continental WinterContact' })).getByRole('button', { name: 'Edit set' }))
  const dialog = await screen.findByRole('dialog', { name: 'Edit tyre set' })
  const month = within(dialog).getByRole('combobox', { name: 'Remind me to fit them from' })
  expect(month).toHaveTextContent('No reminder')
  await ui.click(month)
  await ui.click(await screen.findByRole('option', { name: 'November' }))
  await ui.click(within(dialog).getByRole('button', { name: 'Save changes' }))

  await waitFor(() => expect(state.calls.UpdateTyreSet).toHaveLength(1))
  expect(state.calls.UpdateTyreSet[0].input).toMatchObject({ id: 's2', season: 'WINTER', reminderMonth: 11 })
})
