import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../src/App.tsx'
import { connectivity } from '../../../src/offline/connectivity.ts'
import { outbox } from '../../../src/offline/outbox.ts'
import { findDateField, typeDate } from '../../support/dates.ts'
import { fakeExpenseBackend, fakeSettingsBackend, fakeVehicle, healthHandler, renderWithApollo, sessionHandler, stubViewport, type FakeTimeline } from '../../support/mocks.tsx'
import { answered, server } from '../../support/server.ts'
import { fakeTyres, tyreSummer, tyreWinter, type FakeTyreSet } from '../../support/tyres.ts'

// The tyre timeline on the Tyres tab (#332): the picture's buttons, the Show menu and its saved choices, zoom, the table, moving a swap
// and filling a gap.

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
  window.localStorage.clear()
})
afterAll(() => server.close())

// The winter set came off on 1 March, the summer one went on on the 28th: a gap nobody recorded.
const winterEarly: FakeTyreSet = { ...tyreWinter, installations: [{ ...tyreWinter.installations[0], until: '2026-03-01' }] }
const spare: FakeTyreSet = { id: 's3', name: 'Spare all-season', season: 'ALL_SEASON', roughDistance: 0, onTheCar: false, installations: [] }

function setup(sets: FakeTyreSet[] = [tyreSummer, tyreWinter], { vehicle = fakeVehicle(), timeline = null as FakeTimeline | null, lines = {} as Parameters<typeof fakeTyres>[2] } = {}) {
  stubViewport('desktop')
  const tyres = fakeTyres(sets, [{ id: 'e1', kind: 'PUNCTURE_REPAIRED', setId: 's1', day: '2026-07-19', odometer: null, note: null }], lines)
  const settings = fakeSettingsBackend({ timeline })
  server.use(sessionHandler('NONE', () => null), healthHandler, ...settings.handlers, ...fakeExpenseBackend(vehicle, []).handlers, ...tyres.handlers)
  renderWithApollo(<App />, '/vehicles/v1?tab=tyres')
  return { ...tyres, settings, ui: userEvent.setup() }
}

const card = async () => (await screen.findByRole('heading', { name: 'Timeline' })).closest('section')!
const SWAP = 'Swap on Mar 28, 2026: Continental WinterContact off, Michelin Primacy 4 on. Move the swap'

it('draws a bar per period, a handle on the swap and the same as a table, after the sets’ notes and before the sets', async () => {
  setup()
  const tl = within(await card())

  // Each thing has two buttons of one name, said by what it does: the picture's, and the table's.
  expect(tl.getAllByRole('button', { name: 'Michelin Primacy 4, on the car Mar 28, 2026 to today, 7,700 km. Edit' })).toHaveLength(2)
  expect(tl.getAllByRole('button', { name: 'Continental WinterContact, on the car Nov 15, 2025 to Mar 28, 2026, 4,512 km. Edit' })).toHaveLength(2)
  expect(tl.getAllByRole('button', { name: SWAP })).toHaveLength(2)
  expect(tl.getByRole('group', { name: 'Timeline picture, scrolls sideways' })).toHaveAttribute('tabindex', '0')
  const rows = within(tl.getByRole('table', { name: 'The timeline as a table' })).getAllByRole('row')
  expect(rows.map((r) => r.textContent)).toEqual([
    'SetFromUntilDistanceActions',
    'Michelin Primacy 4Jul 19, 2026–Puncture repaired',
    'Michelin Primacy 4Mar 28, 2026on the car7,700 kmEditMove the swap',
    'Continental WinterContactNov 15, 2025Mar 28, 20264,512 kmEdit',
    'Michelin Primacy 4Mar 25, 2023Mar 28, 2026Before tracking · ≈ 32,900 km', // up to the day its rough figure stands for
    'Continental WinterContactMar 25, 2023Nov 15, 2025Before tracking · ≈ 20,200 km', // up to its first period
  ])
  // Before the sets' cards in the page.
  const first = screen.getByRole('article', { name: 'Michelin Primacy 4' })
  expect(tl.getByRole('heading', { name: 'Timeline' }).compareDocumentPosition(first) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
})

it('someone who may only view sees the picture and the table, with nothing to press in them', async () => {
  setup([tyreSummer, winterEarly], { vehicle: fakeVehicle({ canEdit: false, logAccess: 'VIEW' }) })
  const tl = within(await card())

  expect(tl.queryByRole('button', { name: /Edit$/ })).not.toBeInTheDocument()
  expect(tl.queryByRole('button', { name: /Move the swap$|Fill in$/ })).not.toBeInTheDocument()
  expect(tl.getByRole('columnheader', { name: 'Distance' })).toBeInTheDocument()
  expect(tl.queryByRole('columnheader', { name: 'Actions' })).not.toBeInTheDocument()
  expect(tl.getByRole('rowheader', { name: 'No set recorded' })).toBeInTheDocument()
  expect(tl.getByRole('button', { name: 'Zoom in' })).toBeEnabled() // looking is everyone's
})

it('moves a swap from its handle: the line whole with the new day, and Undo sends the old day back', async () => {
  const { ui, state } = setup([tyreSummer, tyreWinter], { lines: { r1: { odometer: 141_000, note: 'At the garage' } } })
  const tl = within(await card())

  await ui.click(tl.getAllByRole('button', { name: SWAP })[0])
  const dialog = await screen.findByRole('dialog', { name: 'Move the swap' })
  expect(within(dialog).getByText('Continental WinterContact came off and Michelin Primacy 4 went on that day. A new day moves both.')).toBeInTheDocument()
  const day = await findDateField(/Day of the swap/, dialog)
  expect(day).toHaveAccessibleDescription(/The reading typed with the swap moves with it\./)
  await typeDate(ui, day, '2026-04-04')
  await ui.click(within(dialog).getByRole('button', { name: 'Save' }))

  await waitFor(() => expect(state.calls.UpdateTyreEntry).toEqual([{ input: { id: 'r1', kind: 'SWAP', setId: 's1', day: '2026-04-04', odometer: 141_000, note: 'At the garage' } }]))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(await screen.findByText('Swap moved to Apr 4, 2026.')).toBeInTheDocument()
  await ui.click(screen.getByRole('button', { name: 'Undo' }))
  await waitFor(() => expect(state.calls.UpdateTyreEntry).toHaveLength(2))
  expect(state.calls.UpdateTyreEntry[1]).toEqual({ input: { id: 'r1', kind: 'SWAP', setId: 's1', day: '2026-03-28', odometer: 141_000, note: 'At the garage' } })
})

it('a refused move says why in the dialog, which stays', async () => {
  const { ui, state } = setup()
  state.failNext.push({ key: 'tyres.swapBeforePrevious', args: { set: 'Continental WinterContact', from: '2025-11-15' } })

  await ui.click(within(await card()).getAllByRole('button', { name: SWAP })[1]) // the table's
  const dialog = await screen.findByRole('dialog', { name: 'Move the swap' })
  await typeDate(ui, await findDateField(/Day of the swap/, dialog), '2025-11-01')
  await ui.click(within(dialog).getByRole('button', { name: 'Save' }))

  expect(await within(dialog).findByRole('alert')).toHaveTextContent(/Continental WinterContact/)
  expect(screen.getByRole('dialog', { name: 'Move the swap' })).toBeInTheDocument()
})

it('a bar opens its period’s edit', async () => {
  const { ui } = setup()

  await ui.click(within(await card()).getAllByRole('button', { name: /^Continental WinterContact, on the car .* Edit$/ })[0])

  const dialog = await screen.findByRole('dialog', { name: 'Edit installation' })
  expect(await findDateField(/^First day on the car/, dialog)).toBeInTheDocument()
})

it('a gap asks which set was on the car: the set before it stretches, the set after it starts earlier, another one gets a period', async () => {
  const { ui, state } = setup([tyreSummer, winterEarly, spare])
  const tl = within(await card())
  const gap = () => tl.getAllByRole('button', { name: 'No set recorded from Mar 1, 2026 to Mar 28, 2026. Fill in' })[0]

  // The set before the gap is offered first: it simply stayed on.
  await ui.click(gap())
  let dialog = await screen.findByRole('dialog', { name: 'Which set was on the car?' })
  expect(within(dialog).getByText('Nothing is recorded from Mar 1, 2026 to Mar 28, 2026.')).toBeInTheDocument()
  expect(within(dialog).getByRole('combobox', { name: 'Set' })).toHaveTextContent('Winter: Continental WinterContact')
  await ui.click(within(dialog).getByRole('button', { name: 'Save' }))
  await waitFor(() => expect(state.calls.UpdateTyreInstallation).toEqual([{ input: { id: 'r2', from: '2025-11-15', until: '2026-03-28', pairOneFront: true, odometerFrom: null, odometerUntil: null } }]))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

  // The set after it: its first day moves back, and the form says its reading moves with it.
  await ui.click(gap())
  dialog = await screen.findByRole('dialog', { name: 'Which set was on the car?' })
  await ui.click(within(dialog).getByRole('combobox', { name: 'Set' }))
  await ui.click(await screen.findByRole('option', { name: 'Summer: Michelin Primacy 4' }))
  expect(await findDateField(/^First day on the car/, dialog)).toHaveAccessibleDescription(/The reading typed with the swap moves with it\./)
  await ui.click(within(dialog).getByRole('button', { name: 'Save' }))
  await waitFor(() => expect(state.calls.UpdateTyreInstallation).toHaveLength(2))
  expect(state.calls.UpdateTyreInstallation[1]).toEqual({ input: { id: 'r1', from: '2026-03-01', until: null, pairOneFront: true, odometerFrom: null, odometerUntil: null } })
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

  // Another set: a period of its own.
  await ui.click(gap())
  dialog = await screen.findByRole('dialog', { name: 'Which set was on the car?' })
  await ui.click(within(dialog).getByRole('combobox', { name: 'Set' }))
  await ui.click(await screen.findByRole('option', { name: 'All-season: Spare all-season' }))
  await ui.click(within(dialog).getByRole('button', { name: 'Save' }))
  await waitFor(() => expect(state.calls.AddTyreInstallation).toHaveLength(1))
  expect(state.calls.AddTyreInstallation[0].input).toMatchObject({ setId: 's3', from: '2026-03-01', until: '2026-03-28', pairOneFront: null })
})

it('the Show menu chooses the range, the layers and the sets, says what is chosen, and saves the choices with the account', async () => {
  const { ui, settings } = setup([tyreSummer, winterEarly])
  const tl = within(await card())
  const menu = tl.getByRole('button', { name: 'Show' })
  expect(menu).toHaveAttribute('aria-expanded', 'false')
  expect(tl.getByText('All · 4 of 5 shown')).toBeInTheDocument()

  await ui.click(menu)
  expect(menu).toHaveAttribute('aria-expanded', 'true')
  await ui.click(tl.getByRole('checkbox', { name: 'Gaps' }))
  expect(tl.queryByRole('button', { name: /Fill in$/ })).not.toBeInTheDocument()
  expect(tl.queryByRole('rowheader', { name: 'No set recorded' })).not.toBeInTheDocument()
  await ui.click(tl.getByRole('checkbox', { name: 'Continental WinterContact' }))
  expect(tl.getByText('All · 3 of 5 shown · 1 set hidden')).toBeInTheDocument()
  expect(tl.queryByRole('button', { name: /^Continental WinterContact, on the car/ })).not.toBeInTheDocument()
  await ui.click(tl.getByRole('radio', { name: 'Last 12 months' }))
  expect(tl.getByText('Last 12 months · 3 of 5 shown · 1 set hidden')).toBeInTheDocument()

  await waitFor(() => expect(settings.state.settings.timeline).toMatchObject({ range: 'TWELVE_MONTHS', layers: ['BEFORE_TRACKING', 'MARKS', 'TABLE'], hiddenSets: ['s2'] }))
})

it('opens with the account’s choices, keeps the ids of other cars’ hidden sets, and with Table off keeps the table for screen readers as text', async () => {
  const { ui, settings } = setup([tyreSummer, tyreWinter], { timeline: { range: 'TWO_YEARS', layers: ['GAPS'], hiddenSets: ['another-cars-set'] } })
  const tl = within(await card())

  expect(await tl.findByText('Last 2 years · 1 of 5 shown')).toBeInTheDocument() // the other car's set is not this one's to count
  const table = tl.getByRole('table', { name: 'The timeline as a table', hidden: true })
  expect(within(table).queryByRole('button', { hidden: true })).not.toBeInTheDocument()
  expect(within(table).getByRole('rowheader', { name: 'Michelin Primacy 4', hidden: true })).toBeInTheDocument()

  await ui.click(tl.getByRole('button', { name: 'Show' }))
  await ui.click(tl.getByRole('checkbox', { name: 'Michelin Primacy 4' }))
  await waitFor(() => expect(settings.state.settings.timeline).toMatchObject({ hiddenSets: ['another-cars-set', 's1'] }))
})

it('zooms in steps from 1× to 16×, and back to 1× when the range changes', async () => {
  const { ui } = setup()
  const tl = within(await card())
  const [out, zoomIn] = [tl.getByRole('button', { name: 'Zoom out' }), tl.getByRole('button', { name: 'Zoom in' })]
  expect(out).toBeDisabled()

  for (const level of ['2×', '4×', '8×', '16×']) {
    await ui.click(zoomIn)
    expect(tl.getByText(level)).toBeInTheDocument()
  }
  expect(zoomIn).toBeDisabled()
  await ui.click(out)
  expect(tl.getByText('8×')).toBeInTheDocument()

  await ui.click(tl.getByRole('button', { name: 'Show' }))
  await ui.click(tl.getByRole('radio', { name: 'Last 2 years' }))
  expect(tl.getByText('1×')).toBeInTheDocument()
})

it('a set with no swaps yet shows only its time before tracking, and offers Add past swaps', async () => {
  setup([{ ...tyreWinter, installations: [] }])
  const tl = within(await card())

  expect(tl.getByText('No swaps are recorded yet, so only the time before tracking shows.')).toBeInTheDocument()
  expect(tl.getByRole('button', { name: 'Add past swaps' })).toBeInTheDocument()
})

it('out of reach, a move is kept on the device, and its sets’ rows say Changed · not synced', async () => {
  const { ui, state } = setup()
  const tl = within(await card())
  await answered()
  act(() => connectivity.failed())

  await ui.click(tl.getAllByRole('button', { name: SWAP })[0])
  const dialog = await screen.findByRole('dialog', { name: 'Move the swap' })
  expect(within(dialog).getByText(/You are offline\. What you save is kept on this device/)).toBeInTheDocument()
  await typeDate(ui, await findDateField(/Day of the swap/, dialog), '2026-04-04')
  await ui.click(within(dialog).getByRole('button', { name: 'Save' }))

  await waitFor(() => expect(outbox.changes).toMatchObject([{ entity: 'tyres', action: 'updateTyreEntry', input: { id: 'r1', day: '2026-04-04' } }]))
  expect(state.calls.UpdateTyreEntry).toBeUndefined()
  const again = within(await card())
  await waitFor(() => expect(again.getAllByText('Changed · not synced')).toHaveLength(2))
  expect(again.getAllByRole('button', { name: 'Michelin Primacy 4, on the car Apr 4, 2026 to today, updated once synced. Edit' })).toHaveLength(2)
})
