import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../src/App.tsx'
import { connectivity } from '../../../src/offline/connectivity.ts'
import { outbox } from '../../../src/offline/outbox.ts'
import { findDateField, typeDate } from '../../support/dates.ts'
import { fakeExpenseBackend, fakeSettingsBackend, fakeVehicle, healthHandler, renderWithApollo, sessionHandler, stubViewport, type FakeTimeline } from '../../support/mocks.tsx'
import { answered, server } from '../../support/server.ts'
import { graphql } from '../../support/graphql.ts'
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

it('the same set on both sides of a gap, filled whole, becomes one period; a later period whose line holds a reading is kept and met', async () => {
  const twice: FakeTyreSet = { ...tyreWinter, installations: [{ id: 'r4', setId: 's2', from: '2026-02-01', until: '2026-03-28', pairOneFront: true, distance: 900 }, { ...tyreWinter.installations[0], until: '2026-01-10' }] }
  const filled = async (lines: Parameters<typeof fakeTyres>[2]) => {
    const { ui, state } = setup([tyreSummer, twice], { lines })
    await ui.click(within(await card()).getAllByRole('button', { name: /^No set recorded from Jan 10, 2026 to Feb 1, 2026\. Fill in$/ })[0])
    const dialog = await screen.findByRole('dialog', { name: 'Which set was on the car?' })
    await ui.click(within(dialog).getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(state.calls.UpdateTyreInstallation).toHaveLength(1))
    return state
  }

  const joined = await filled({})
  expect(joined.calls.DeleteTyreInstallation).toEqual([{ id: 'r4' }])
  expect(joined.calls.UpdateTyreInstallation[0].input).toMatchObject({ id: 'r2', from: '2025-11-15', until: '2026-03-28' })
})

it('a later period whose line holds a reading is not trashed: the earlier one stretches to meet it', async () => {
  const twice: FakeTyreSet = { ...tyreWinter, installations: [{ id: 'r4', setId: 's2', from: '2026-02-01', until: '2026-03-28', pairOneFront: true, distance: 900 }, { ...tyreWinter.installations[0], until: '2026-01-10' }] }
  const { ui, state } = setup([tyreSummer, twice], { lines: { r4: { odometer: 139_000 } } })

  await ui.click(within(await card()).getAllByRole('button', { name: /^No set recorded from Jan 10, 2026 to Feb 1, 2026\. Fill in$/ })[0])
  await ui.click(within(await screen.findByRole('dialog', { name: 'Which set was on the car?' })).getByRole('button', { name: 'Save' }))

  await waitFor(() => expect(state.calls.UpdateTyreInstallation).toHaveLength(1))
  expect(state.calls.UpdateTyreInstallation[0].input).toMatchObject({ id: 'r2', until: '2026-02-01' })
  expect(state.calls.DeleteTyreInstallation).toBeUndefined()
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

it('choices this build cannot draw (a newer server’s layer) are left alone: the browser’s own stay', async () => {
  setup([tyreSummer, tyreWinter], { timeline: { range: 'TWO_YEARS', layers: ['GAPS', 'USUAL_MONTHS' as never], hiddenSets: [] } })

  const tl = within(await card())
  await answered() // the account's answer is in: it changed nothing
  expect(tl.getByText('All · 4 of 5 shown')).toBeInTheDocument()
})

// A drag in jsdom: nothing is laid out, so a day is 0.55 px wide (the minimum scale); 11 px to the right is 20 days later.
function dragHandle(handle: HTMLElement, dx: number) {
  const pointer = (type: string, clientX: number) => fireEvent(handle, new MouseEvent(type, { bubbles: true, clientX, button: 0 }))
  Object.defineProperty(handle, 'setPointerCapture', { value: () => undefined, configurable: true })
  // jsdom's pointer events carry no pointerType: React's synthetic one reads it from the native event.
  const down = new MouseEvent('pointerdown', { bubbles: true, clientX: 100, button: 0 })
  Object.defineProperty(down, 'pointerType', { value: 'mouse' })
  fireEvent(handle, down)
  pointer('pointermove', 100 + dx / 2)
  pointer('pointermove', 100 + dx)
  return () => {
    pointer('pointerup', 100 + dx)
    fireEvent.click(handle) // the click a release makes: the drag's, not a request for the dialog
  }
}

it('with a mouse a swap is dragged to another day: the day shown while it moves, saved on release with Undo, and no dialog', async () => {
  const { ui, state } = setup()
  const handle = within(await card()).getAllByRole('button', { name: SWAP })[0]

  const release = dragHandle(handle, 11)
  expect(within(await card()).getByRole('status')).toHaveTextContent('Apr 17, 2026')
  release()

  await waitFor(() => expect(state.calls.UpdateTyreEntry).toEqual([{ input: { id: 'r1', kind: 'SWAP', setId: 's1', day: '2026-04-17', odometer: null, note: null } }]))
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  expect(await screen.findByText('Swap moved to Apr 17, 2026.')).toBeInTheDocument()
  await ui.click(screen.getByRole('button', { name: 'Undo' }))
  await waitFor(() => expect(state.calls.UpdateTyreEntry).toHaveLength(2))
})

it('a refused drag says why in a toast and the handle goes back; less than a few pixels is a click, which opens the dialog', async () => {
  const { state } = setup()
  state.failNext.push({ key: 'tyres.swapBeforePrevious', args: { set: 'Continental WinterContact', from: '2025-11-15' } })
  const handle = within(await card()).getAllByRole('button', { name: SWAP })[0]

  dragHandle(handle, -90)()
  expect(await screen.findByText(/^The swap cannot move to or before .*, the day Continental WinterContact went on the car before it\.$/)).toBeInTheDocument()
  await waitFor(() => expect(within(screen.getByRole('heading', { name: 'Timeline' }).closest('section')!).queryByRole('status')).not.toBeInTheDocument())
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

  const pointer = (type: string, clientX: number) => fireEvent(handle, new MouseEvent(type, { bubbles: true, clientX, button: 0 }))
  const down = new MouseEvent('pointerdown', { bubbles: true, clientX: 100, button: 0 })
  Object.defineProperty(down, 'pointerType', { value: 'mouse' })
  fireEvent(handle, down)
  pointer('pointermove', 102)
  pointer('pointerup', 102)
  fireEvent.click(handle)
  expect(await screen.findByRole('dialog', { name: 'Move the swap' })).toBeInTheDocument()
  expect(state.calls.UpdateTyreEntry).toHaveLength(1)
})

it('out of reach, the device refuses a move the server would refuse, in the dialog, and keeps nothing', async () => {
  const { ui } = setup()
  const tl = within(await card())
  await answered()
  act(() => connectivity.failed())

  await ui.click(tl.getAllByRole('button', { name: SWAP })[0])
  const dialog = await screen.findByRole('dialog', { name: 'Move the swap' })
  await typeDate(ui, await findDateField(/Day of the swap/, dialog), '2025-11-01') // before the winter set went on
  await ui.click(within(dialog).getByRole('button', { name: 'Save' }))

  // Said at the field it is about and under the form, as every refusal is.
  expect((await within(dialog).findAllByText(/^The swap cannot move to or before .*, the day Continental WinterContact went on the car before it\.$/)).length).toBeGreaterThan(0)
  expect(outbox.changes).toHaveLength(0)
})

it('with a set’s month come, the page reads: the reminder’s note, the timeline, the sets', async () => {
  setup([tyreSummer, { ...tyreWinter, reminder: '2026-11-01' }])
  const timeline = await screen.findByRole('heading', { name: 'Timeline' })
  const note = screen.getByText('Time to fit the winter tyres')
  const sets = screen.getByRole('article', { name: 'Michelin Primacy 4' })

  expect(note.compareDocumentPosition(timeline) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  expect(timeline.compareDocumentPosition(sets) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
})

it('a filled gap leaves with its button, and the focus it held goes to the picture', async () => {
  const { ui, state } = setup([tyreSummer, winterEarly])
  // The server's answer to the save has no gap any more (the fake answers its sets as they are: this moves them on first).
  server.use(graphql.mutation('UpdateTyreInstallation', () => void (state.sets = [tyreSummer, tyreWinter])))
  const tl = within(await card())

  await ui.click(tl.getAllByRole('button', { name: /^No set recorded .* Fill in$/ })[0])
  await ui.click(within(await screen.findByRole('dialog', { name: 'Which set was on the car?' })).getByRole('button', { name: 'Save' }))

  await waitFor(() => expect(tl.queryByRole('button', { name: /^No set recorded/ })).not.toBeInTheDocument())
  await waitFor(() => expect(tl.getByRole('group', { name: 'Timeline picture, scrolls sideways' })).toHaveFocus())
})
