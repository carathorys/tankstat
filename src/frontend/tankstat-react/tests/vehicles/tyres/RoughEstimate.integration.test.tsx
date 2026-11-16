import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { HttpResponse } from 'msw'
import { todayIso } from '../../../src/forms/dates.ts'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../src/App.tsx'
import { connectivity } from '../../../src/offline/connectivity.ts'
import { findDateField, typeDate } from '../../support/dates.ts'
import { graphql } from '../../support/graphql.ts'
import { fakeExpenseBackend, fakeVehicle, healthHandler, renderWithApollo, sessionHandler, stubViewport } from '../../support/mocks.tsx'
import { answered, server } from '../../support/server.ts'
import { fakeTyres, tyreSummer, tyreWinter, type FakeTyreSet } from '../../support/tyres.ts'

// The automatic estimate of a used set's rough figure (#291, the owner's decisions 47 to 57).

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

type Estimate = { distance: number; from: string | null; notCovered: { from: string; until: string }[]; leftOutDays: number; readings: boolean }
const worked: Estimate = { distance: 24_100, from: '2024-06-01', notCovered: [{ from: '2023-04-01', until: '2023-10-31' }, { from: '2024-04-01', until: '2024-05-31' }], leftOutDays: 14, readings: true }

function setup(sets: FakeTyreSet[], answer: (months: number[]) => Estimate = () => worked) {
  stubViewport('desktop')
  const tyres = fakeTyres(sets)
  const asked: { vehicleId: string; since: string; until: string; months: number[] }[] = []
  server.use(
    sessionHandler('NONE', () => null),
    healthHandler,
    ...fakeExpenseBackend(fakeVehicle(), []).handlers,
    ...tyres.handlers,
    graphql.query('TyreRoughEstimate', ({ variables }) => {
      asked.push(variables as (typeof asked)[number])
      const estimate = answer(variables.months as number[])
      return HttpResponse.json({ data: { tyreRoughEstimate: { __typename: 'TyreRoughEstimate', ...estimate, notCovered: estimate.notCovered.map((r) => ({ __typename: 'DayRange', ...r })) } } })
    }),
  )
  renderWithApollo(<App />, '/vehicles/v1?tab=tyres')
  return { ...tyres, asked, ui: userEvent.setup() }
}

async function addForm(ui: ReturnType<typeof userEvent.setup>) {
  await ui.click(await screen.findByRole('button', { name: 'Add a tyre set' }))
  return screen.findByRole('dialog', { name: 'Add a tyre set' })
}
const month = (scope: HTMLElement, name: string) => within(within(scope).getByRole('group', { name: 'Usual months' })).getByRole('checkbox', { name })

it('estimates a used set from the fuel log once In use since is filled, says what is not covered and left out, and only offers the figure', async () => {
  const { ui, asked, state } = setup([tyreSummer, tyreWinter])
  const dialog = await addForm(ui)
  const button = within(dialog).getByRole('button', { name: 'Estimate from the fuel log' })
  expect(button).toBeDisabled()
  expect(button).toHaveAccessibleDescription('Fill in In use since first.')

  await ui.type(within(dialog).getByLabelText(/^Name/), 'Nokian')
  await typeDate(ui, await findDateField(/In use since/, dialog), '2023-03-25')
  await ui.type(within(dialog).getByLabelText(/^Driven so far, roughly/), '5000') // typed first: never replaced by itself
  await ui.click(within(dialog).getByRole('button', { name: 'Estimate from the fuel log' }))

  expect(await within(dialog).findByText(/^≈ 24,100 km in the season's, from June 2024 to today, as the fuel log measured them\.$/)).toBeInTheDocument()
  expect(within(dialog).getByText('Not covered, before the fuel log: April – October 2023 and April – May 2024. Add those by hand if you know them.')).toBeInTheDocument()
  expect(within(dialog).getByText('Left out: 14 days another set was on the car.')).toBeInTheDocument()
  expect(asked.at(-1)).toMatchObject({ vehicleId: 'v1', since: '2023-03-25', months: [4, 5, 6, 7, 8, 9, 10] })
  expect(month(dialog, 'April')).toBeChecked()
  expect(month(dialog, 'March')).not.toBeChecked()
  expect(within(dialog).getByLabelText(/^Driven so far, roughly/)).toHaveValue('5000')

  // A month changed asks again; the hint says the months are the set's own, and Season's months goes back.
  await ui.click(month(dialog, 'March'))
  await waitFor(() => expect(asked.at(-1)!.months).toEqual([3, 4, 5, 6, 7, 8, 9, 10]))
  expect(within(dialog).getByText("Changed from its season's.")).toBeInTheDocument()
  expect(await within(dialog).findByText(/^≈ 24,100 km in Mar, Apr, May, Jun, Jul, Aug, Sep, and Oct, from June 2024 to today/)).toBeInTheDocument()

  await ui.click(within(dialog).getByRole('button', { name: 'Use the estimate, 24,100 km, as the rough figure' }))
  expect(within(dialog).getByLabelText(/^Driven so far, roughly/)).toHaveValue('24100')
  expect(within(dialog).getByLabelText(/^Driven so far, roughly/)).toHaveFocus()
  await ui.click(within(dialog).getByRole('button', { name: 'Save set' }))

  await waitFor(() => expect(state.calls.AddTyreSet).toHaveLength(1))
  expect(state.calls.AddTyreSet[0].input).toMatchObject({ name: 'Nokian', roughDistance: 24_100, usualMonths: [3, 4, 5, 6, 7, 8, 9, 10] }) // the months shown are kept (decision 51)
})

it("goes back to the season's months, and a set added on them sends none of its own", async () => {
  const { ui, asked, state } = setup([tyreSummer, tyreWinter])
  const dialog = await addForm(ui)
  await ui.type(within(dialog).getByLabelText(/^Name/), 'Nokian')
  await typeDate(ui, await findDateField(/In use since/, dialog), '2023-03-25')
  await ui.click(within(dialog).getByRole('button', { name: 'Estimate from the fuel log' }))
  await within(dialog).findByText(/^≈ 24,100 km/)
  await ui.click(month(dialog, 'October'))
  await ui.click(within(dialog).getByRole('button', { name: "Season's months" }))
  await waitFor(() => expect(asked.at(-1)!.months).toEqual([4, 5, 6, 7, 8, 9, 10]))
  expect(within(dialog).queryByText("Changed from its season's.")).not.toBeInTheDocument()

  // A change of season takes a set on its season's months along.
  await ui.click(within(dialog).getByRole('radio', { name: 'Winter' }))
  await waitFor(() => expect(asked.at(-1)!.months).toEqual([1, 2, 3, 11, 12]))
  await ui.click(within(dialog).getByRole('button', { name: 'Save set' }))
  await waitFor(() => expect(state.calls.AddTyreSet).toHaveLength(1))
  expect(state.calls.AddTyreSet[0].input).not.toHaveProperty('usualMonths')
})

it('says so when the car has no reading yet, and when the fuel log has nothing in the months', async () => {
  const { ui } = setup([tyreSummer, tyreWinter], (months) =>
    months.includes(4) ? { distance: 0, from: null, notCovered: [], leftOutDays: 0, readings: false } : { distance: 0, from: null, notCovered: [{ from: '2023-05-01', until: '2023-10-31' }], leftOutDays: 0, readings: true },
  )
  const dialog = await addForm(ui)
  await typeDate(ui, await findDateField(/In use since/, dialog), '2023-03-25')
  await ui.click(within(dialog).getByRole('button', { name: 'Estimate from the fuel log' }))

  expect(await within(dialog).findByText('The car has no reading yet, so nothing can be estimated. Type the figure instead, or log a refuelling first.')).toBeInTheDocument()
  expect(within(dialog).queryByRole('button', { name: /^Use the estimate/ })).not.toBeInTheDocument()

  await ui.click(month(dialog, 'April'))
  expect(await within(dialog).findByText('The fuel log has no distance in these months yet.')).toBeInTheDocument()
  expect(within(dialog).getByText(/^Not covered, before the fuel log: May – October 2023\./)).toBeInTheDocument()
  expect(within(dialog).queryByRole('button', { name: /^Use the estimate/ })).not.toBeInTheDocument()
})

it('a month the fuel log reaches but measured nothing in is nothing to offer either', async () => {
  // The latest reading is before the set's months: the server counts a run of 0 from its first day.
  const { ui } = setup([tyreSummer, tyreWinter], () => ({ distance: 0, from: '2026-07-01', notCovered: [], leftOutDays: 0, readings: true }))
  const dialog = await addForm(ui)
  await typeDate(ui, await findDateField(/In use since/, dialog), '2026-07-10')
  await ui.click(within(dialog).getByRole('button', { name: 'Estimate from the fuel log' }))

  expect(await within(dialog).findByText('The fuel log has no distance in these months yet.')).toBeInTheDocument()
  expect(within(dialog).queryByText(/^≈ 0 km/)).not.toBeInTheDocument()
  expect(within(dialog).queryByRole('button', { name: /^Use the estimate/ })).not.toBeInTheDocument()
})

it('needs the server: offline the button is off and says why, the figure can still be typed', async () => {
  const { ui, asked } = setup([tyreSummer, tyreWinter])
  const dialog = await addForm(ui)
  await typeDate(ui, await findDateField(/In use since/, dialog), '2023-03-25')
  await answered()
  act(() => connectivity.failed())

  const button = await within(dialog).findByRole('button', { name: 'Estimate from the fuel log' })
  await waitFor(() => expect(button).toBeDisabled())
  expect(button).toHaveAccessibleDescription('Estimating needs the server. It works again once you are back online; the figure can be typed meanwhile.')
  expect(asked).toEqual([])
})

it('offers the estimate beside the suggestion in the rough figure dialog, up to the day the figure stands for, and keeps the months changed there', async () => {
  const { ui, asked, state } = setup([{ ...tyreSummer, suggestion: { distance: 12_900, until: '2025-03-29', measuredBefore: 20_000 } }, tyreWinter])
  const card = await screen.findByRole('article', { name: 'Michelin Primacy 4' })
  await ui.click(within(card).getByRole('button', { name: 'Check the rough figure' }))
  const dialog = await screen.findByRole('dialog', { name: 'The rough figure' })
  expect(within(dialog).getByLabelText(/^Driven before that/)).toHaveValue('12900') // the suggestion stays in the field (decision 54)

  await ui.click(within(dialog).getByRole('button', { name: 'Estimate from the fuel log' }))
  expect(await within(dialog).findByText(/^≈ 24,100 km in the season's, from June 2024 to Mar 29, 2025, as the fuel log measured them\.$/)).toBeInTheDocument()
  expect(asked.at(-1)).toMatchObject({ until: '2025-03-29' }) // the day the save sends, by one rule
  expect(within(dialog).getByLabelText(/^Driven before that/)).toHaveValue('12900')
  await ui.click(month(dialog, 'October'))
  await waitFor(() => expect(asked.at(-1)!.months).toEqual([4, 5, 6, 7, 8, 9]))
  await ui.click(await within(dialog).findByRole('button', { name: /^Use the estimate/ }))
  expect(within(dialog).getByLabelText(/^Driven before that/)).toHaveValue('24100')
  await ui.click(within(dialog).getByRole('button', { name: 'Save' }))

  await waitFor(() => expect(state.calls.SetTyreRoughDistance).toEqual([{ input: { id: 's1', distance: 24_100, until: '2025-03-29', usualMonths: [4, 5, 6, 7, 8, 9] } }]))
})

it("changes a set's usual months in Edit set, starting from its own", async () => {
  const { ui, state } = setup([{ ...tyreSummer, usualMonths: [3, 4, 5, 6, 7, 8, 9, 10] }, tyreWinter])
  const card = await screen.findByRole('article', { name: 'Michelin Primacy 4' })
  await ui.click(within(card).getByRole('button', { name: 'History' }))
  const history = await screen.findByRole('dialog', { name: /Michelin Primacy 4/ })
  await ui.click(within(history).getByRole('button', { name: 'Edit set' }))
  const edit = await screen.findByRole('dialog', { name: 'Edit tyre set' })

  expect(month(edit, 'March')).toBeChecked()
  expect(within(edit).getByText("Changed from its season's.")).toBeInTheDocument()
  await ui.click(month(edit, 'October'))
  await ui.click(within(edit).getByRole('button', { name: 'Save changes' }))

  await waitFor(() => expect(state.calls.UpdateTyreSet).toHaveLength(1))
  expect(state.calls.UpdateTyreSet[0].input).toMatchObject({ id: 's1', usualMonths: [3, 4, 5, 6, 7, 8, 9] })
})

it('says "to today" in the rough figure dialog when nothing ends the span before today', async () => {
  const { ui, asked } = setup([{ ...tyreWinter, onTheCar: false, installations: [], roughUntil: null }])
  const card = await screen.findByRole('article', { name: 'Continental WinterContact' })
  await ui.click(within(card).getByRole('button', { name: 'History' }))
  const history = await screen.findByRole('dialog', { name: /Continental WinterContact/ })
  await ui.click(within(history).getByRole('button', { name: 'Change the rough figure' }))
  const dialog = await screen.findByRole('dialog', { name: 'The rough figure' })

  await ui.click(within(dialog).getByRole('button', { name: 'Estimate from the fuel log' }))
  expect(await within(dialog).findByText(/^≈ 24,100 km in the season's, from June 2024 to today, as the fuel log measured them\.$/)).toBeInTheDocument()
  expect(asked.at(-1)?.until).toBe(todayIso()) // counted up to today, and said so
})

it('estimates from the first day of In use since\'s month, so a figure that stops earlier that month can still be estimated', async () => {
  // In use since is 25 March 2023 (the fake's); the figure stops on 10 March 2023: the span starts on 1 March (decision 48).
  const { ui, asked } = setup([{ ...tyreWinter, onTheCar: false, installations: [], roughUntil: '2023-03-10' }])
  const card = await screen.findByRole('article', { name: 'Continental WinterContact' })
  await ui.click(within(card).getByRole('button', { name: 'History' }))
  const history = await screen.findByRole('dialog', { name: /Continental WinterContact/ })
  await ui.click(within(history).getByRole('button', { name: 'Change the rough figure' }))
  const dialog = await screen.findByRole('dialog', { name: 'The rough figure' })

  const button = within(dialog).getByRole('button', { name: 'Estimate from the fuel log' })
  expect(button).toBeEnabled()
  expect(within(dialog).queryByText('Fill in In use since first.')).not.toBeInTheDocument()
  await ui.click(button)
  await waitFor(() => expect(asked.at(-1)).toMatchObject({ since: '2023-03-25', until: '2023-03-10' }))
})

it('says the span cannot be when the figure stops before In use since\'s month', async () => {
  const { ui } = setup([{ ...tyreWinter, onTheCar: false, installations: [], roughUntil: '2023-02-10' }])
  const card = await screen.findByRole('article', { name: 'Continental WinterContact' })
  await ui.click(within(card).getByRole('button', { name: 'History' }))
  const history = await screen.findByRole('dialog', { name: /Continental WinterContact/ })
  await ui.click(within(history).getByRole('button', { name: 'Change the rough figure' }))
  const dialog = await screen.findByRole('dialog', { name: 'The rough figure' })

  expect(within(dialog).getByRole('button', { name: 'Estimate from the fuel log' })).toBeDisabled()
  expect(within(dialog).getByText('The day the estimate starts must be before the day it ends, and not in the future.')).toBeInTheDocument()
  expect(within(dialog).queryByText('Fill in In use since first.')).not.toBeInTheDocument() // it is filled
})
