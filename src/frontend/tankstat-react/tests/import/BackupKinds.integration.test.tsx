import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { HttpResponse } from 'msw'
import { useState } from 'react'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import { BackupImport, type BackupStep } from '../../src/pages/import/BackupImport.tsx'
import { check } from '../support/accessibility.tsx'
import { graphql } from '../support/graphql.ts'
import { renderWithApollo, stubViewport } from '../support/mocks.tsx'
import { server } from '../support/server.ts'

// The details of a merge (#338): what comes of each kind of a car, the Choose dialog, the review's lines, the typed confirmations
// and the report's lines. The server's answers are made here; what was asked is kept.

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())
afterEach(() => {
  TYRES = {}
  MEDIA = {}
})

type Mode = 'ONLY_MISSING' | 'DONT_IMPORT' | 'REPLACE_ALL' | 'CHOOSE'
type Asked = { carId: string; targetVehicleId: string | null; refuelings?: { mode: Mode } | null; expenses?: { mode: Mode } | null; schedules?: { mode: Mode } | null; confirmedName?: string | null }

const kindOf = (kind: string, over: Record<string, unknown> = {}) => ({
  __typename: 'ImportKindReviewInfo', kind, mode: 'ONLY_MISSING', backup: 0, new: 0, differ: 0, same: 0, inTrash: 0, adds: 0, takes: 0, targetLive: 0, targetTrashed: 0, withPhotos: 0, onTyreLines: 0, photosLost: 0, ...over,
})
/** What the backup's Octavia is against My Octavia; the Fabia against Old Golf has expenses with photos and tyre log lines, and no recurring expenses. */
const AGAINST: Record<string, Record<string, Record<string, number>>> = {
  c1: {
    refueling: { backup: 60, new: 55, differ: 3, same: 2, targetLive: 40, targetTrashed: 2 },
    expense: { backup: 5, new: 2, differ: 1, same: 2, targetLive: 9 },
    recurring: { backup: 3, new: 1, differ: 0, same: 2, targetLive: 4 },
  },
  c2: {
    refueling: { backup: 10, new: 10, targetLive: 3, withPhotos: 1 },
    expense: { backup: 2, new: 2, targetLive: 6, withPhotos: 2, onTyreLines: 1 },
    recurring: { backup: 0, targetLive: 1 },
  },
}
const NO_TYRES = { __typename: 'ImportTyreReviewInfo', sets: [], noSetLines: 0, noSetExpenses: 0, expenses: 0 }
const offer = (id: string, name: string, state: string, over: Record<string, unknown> = {}) => ({
  __typename: 'ImportSetOfferInfo', id, name, season: 'WINTER', state, installations: 3, lines: 6, tyres: 5, expenses: 3, refusalKey: null, refusalArgs: null, ...over,
})
/** The tyres each car's review answers with (#339); none unless a test sets them. */
let TYRES: Record<string, unknown> = {}
/** The media each car's review answers with (#339); none unless a test sets them. */
let MEDIA: Record<string, unknown> = {}
const mediaOf = (over: Record<string, unknown> = {}) => ({
  __typename: 'ImportMediaReviewInfo', photosNew: 3, photosHere: 1, photosLeftOut: false, picture: true, targetPicture: true, chartsNew: 2, chartsHere: 0, ownChartsNew: 1,
  dashboard: true, targetDashboard: true, ownDashboard: true, ...over,
})
const review = (car: Asked) => {
  const modes = { refueling: car.refuelings?.mode, expense: car.expenses?.mode, recurring: car.schedules?.mode } as Record<string, Mode | undefined>
  const names = { c1: ['Octavia', 'My Octavia'], c2: ['Fabia', 'Old Golf'] } as Record<string, string[]>
  return {
    __typename: 'ImportCarReviewInfo', carId: car.carId, name: names[car.carId][0], ownerName: 'Anna', ownedByMe: false, targetVehicleId: car.targetVehicleId, targetName: car.targetVehicleId ? names[car.carId][1] : null,
    newRefuelings: 0, newExpenses: 0, newSchedules: 0, existingRefuelings: 0, existingExpenses: 0, existingSchedules: 0, differing: 0, inTrash: 0,
    links: modes.expense !== 'DONT_IMPORT' && modes.recurring !== 'DONT_IMPORT', linksLost: modes.recurring === 'REPLACE_ALL' ? 2 : 0,
    tyres: TYRES[car.carId] ?? NO_TYRES,
    media: MEDIA[car.carId] ?? null,
    kinds: ['refueling', 'expense', 'recurring'].map((kind) => kindOf(kind, { ...(car.targetVehicleId ? AGAINST[car.carId][kind] : {}), mode: modes[kind] ?? 'ONLY_MISSING', adds: 4, takes: 1 })),
  }
}
const offers = {
  __typename: 'ImportCarOffersInfo',
  file: { __typename: 'BackupFileInfo', createdAt: '2026-11-03T08:15:00Z', version: '0.21.0', trashedCars: 0 },
  cars: [
    { __typename: 'ImportCarOfferInfo', ownedByMe: false, suggestedTargetId: 'v1', car: { __typename: 'BackupCarInfo', id: 'c1', name: 'Octavia', licensePlate: null, ownerName: 'Anna', distanceUnit: 'KILOMETERS', volumeUnit: 'LITERS', refuelings: 60, expenses: 5, schedules: 3 } },
    { __typename: 'ImportCarOfferInfo', ownedByMe: false, suggestedTargetId: 'v2', car: { __typename: 'BackupCarInfo', id: 'c2', name: 'Fabia', licensePlate: null, ownerName: 'Anna', distanceUnit: 'KILOMETERS', volumeUnit: 'LITERS', refuelings: 10, expenses: 2, schedules: 0 } },
  ],
  targets: [
    { __typename: 'ImportOwnCarInfo', id: 'v1', name: 'My Octavia', licensePlate: null, distanceUnit: 'KILOMETERS', volumeUnit: 'LITERS' },
    { __typename: 'ImportOwnCarInfo', id: 'v2', name: 'Old Golf', licensePlate: null, distanceUnit: 'KILOMETERS', volumeUnit: 'LITERS' },
  ],
}
const entry = (id: string, over: Record<string, unknown> = {}) => ({ __typename: 'ImportEntryInfo', id, date: '2026-03-01', odometer: 1000, title: null, amount: 60, currency: 'EUR', volume: 40, differences: [], ...over })
const run = (state: string, over: Record<string, unknown> = {}) => ({
  __typename: 'ImportRunInfo', id: 'run1', kind: 'IMPORT', startedByName: 'Me', mine: true, state, failure: null, queuedAt: '2026-11-16T10:00:00Z', startedAt: null, endedAt: null, ahead: 0,
  fileToken: 't1', limitMinutes: 10, progress: { __typename: 'ImportRunProgressInfo', car: 1, cars: 1, done: 0, total: 1 }, cars: [], report: null, ...over,
})
const reportCar = (over: Record<string, unknown> = {}) => ({
  __typename: 'ImportCarReportInfo', carId: 'c1', name: 'Octavia', vehicleId: 'v1', targetName: 'My Octavia', asNew: false, leftOut: false,
  refuelings: 60, expenses: 2, schedules: 0, existing: 4, differing: 4, inTrash: 0, refusedMore: 0, refused: [], replacing: false, links: 1, kinds: [], tyres: null, media: null, ...over,
})
const kindReport = (kind: string, mode: Mode, imported: number, changed: number, deleted: number) => ({ __typename: 'ImportKindReportInfo', kind, mode, imported, changed, deleted })

/** The page from the Cars step on, with an opened file (the token of a run that brought nothing in stands in for the file step's). */
async function cars(found?: unknown) {
  stubViewport('desktop')
  const calls = { reviews: [] as Asked[][], entries: [] as Record<string, unknown>[], starts: [] as { token: string; cars: Asked[] }[] }
  server.use(
    graphql.query('VehicleDefaults', () => HttpResponse.json({ data: { vehicleDefaults: { distanceUnit: 'KILOMETERS', volumeUnit: 'LITERS', currency: 'EUR', recurringWarnDays: 30, recurringWarnDistance: 500 } } })),
    graphql.query('BackupImportCars', () => HttpResponse.json({ data: { backupImportCars: offers } })),
    graphql.query('BackupImportReview', ({ variables }) => {
      calls.reviews.push(variables.cars as Asked[])
      return HttpResponse.json({ data: { backupImportReview: (variables.cars as Asked[]).map(review) } })
    }),
    graphql.query('BackupImportEntries', ({ variables }) => {
      calls.entries.push(variables)
      const { differing, skip, take } = variables as { differing: boolean; skip: number; take: number }
      const total = differing ? 3 : 55
      const items = Array.from({ length: Math.min(take, total - skip) }, (_, i) => {
        const n = skip + i + 1
        return differing
          ? entry(`d${n}`, { date: `2026-04-0${n}`, odometer: 2000 + n, differences: [{ __typename: 'ImportFieldDifferenceInfo', field: 'note', backup: null, target: 'mine' }, { __typename: 'ImportFieldDifferenceInfo', field: 'isFullTank', backup: 'true', target: 'false' }, { __typename: 'ImportFieldDifferenceInfo', field: 'volume', backup: '40.5', target: '41' }] })
          : entry(`n${n}`, { odometer: 1000 + n })
      })
      return HttpResponse.json({ data: { backupImportEntries: { __typename: 'ImportEntryPageInfo', total, items } } })
    }),
    graphql.mutation('StartBackupImport', ({ variables }) => {
      calls.starts.push(variables.input as { token: string; cars: Asked[] })
      return HttpResponse.json({ data: { startBackupImport: run('RUNNING') } })
    }),
    graphql.query('ImportRun', () => HttpResponse.json({ data: { importRun: run('RUNNING') } })),
  )
  function Harness() {
    const [step, setStep] = useState<BackupStep>('done')
    return <BackupImport step={step} onStep={setStep} run={(found ?? run('NOT_FINISHED', { failure: 'CANCELLED' })) as never} onAnother={() => undefined} pollMs={5} />
  }
  const view = renderWithApollo(<Harness />)
  const ui = userEvent.setup()
  if (!found) await ui.click(await screen.findByRole('button', { name: 'Back to the review' })) // no choices here yet: the cars
  return { calls, view, ui }
}

/** Ticks a car and opens its Choose what to import. */
async function open(ui: ReturnType<typeof userEvent.setup>, name: string) {
  const car = await screen.findByRole('region', { name })
  await ui.click(within(car).getByRole('checkbox', { name }))
  await ui.click(within(car).getByRole('button', { name: 'Choose what to import' }))
  return within(car)
}

it('what to import is closed at first; open, each kind says what the backup has against the car and what its choice does', async () => {
  const { view, ui } = await cars()
  const car = await screen.findByRole('region', { name: 'Octavia' })
  await ui.click(within(car).getByRole('checkbox', { name: 'Octavia' }))

  const toggle = within(car).getByRole('button', { name: 'Choose what to import' })
  expect(toggle).toHaveAttribute('aria-expanded', 'false')
  expect(within(car).getByText('Only what is missing is imported.')).toBeInTheDocument()
  await ui.click(toggle)
  expect(toggle).toHaveAttribute('aria-expanded', 'true')

  expect(await within(car).findByRole('heading', { level: 3, name: 'Refuellings' })).toBeInTheDocument()
  expect(within(car).getByText('In the backup: 55 only there · 3 differ · 2 the same')).toBeInTheDocument()
  const fuel = within(car).getByRole('radiogroup', { name: 'Refuellings of Octavia' })
  expect(within(fuel).getAllByRole('radio').map((r) => r.parentElement?.textContent)).toEqual(["Don't import", 'Only the missing', 'Replace all', 'Choose…'])
  expect(within(fuel).getByRole('radio', { name: 'Only the missing' })).toBeChecked()
  expect(within(car).getByText('55 new are added; 3 that differ stay as they are; 2 are the same.')).toBeInTheDocument()
  await check(view.container)

  await ui.click(within(fuel).getByRole('radio', { name: "Don't import" }))
  expect(within(car).getByText("None of the backup's refuellings are imported.")).toBeInTheDocument()
  await ui.click(within(fuel).getByRole('radio', { name: 'Replace all' }))
  expect(within(car).getByText("Your My Octavia's 40 refuellings and the 2 in its trash are deleted for good, then the backup's 60 come in.")).toBeInTheDocument()
  // Nothing in the trash: not said.
  await ui.click(within(within(car).getByRole('radiogroup', { name: 'Expenses of Octavia' })).getByRole('radio', { name: 'Replace all' }))
  expect(within(car).getByText("Your My Octavia's 9 expenses are deleted for good, then the backup's 5 come in.")).toBeInTheDocument()
  await ui.click(within(fuel).getByRole('radio', { name: 'Choose…' }))
  expect(within(car).getByText("55 of 55 new are added; 0 of 3 that differ take the backup's values.")).toBeInTheDocument()
  expect(within(car).getByRole('button', { name: 'Choose refuellings…' })).toBeInTheDocument()
  expect(within(car).queryByText('Only what is missing is imported.')).not.toBeInTheDocument()
})

it('Replace all is held back with its reason: tyre log lines, or a kind the backup has none of (photos no longer); and a car with every kind left out holds Next back', async () => {
  const { ui } = await cars()
  const car = await open(ui, 'Fabia')

  // A photo holds nothing back since #339: it comes back with its entry, and the confirmation counts those that do not.
  const fuel = within(await car.findByRole('radiogroup', { name: 'Refuellings of Fabia' })).getByRole('radio', { name: 'Replace all' })
  expect(fuel).toBeEnabled()
  const expenses = within(car.getByRole('radiogroup', { name: 'Expenses of Fabia' })).getByRole('radio', { name: 'Replace all' })
  expect(expenses).toBeDisabled()
  expect(expenses).toHaveAccessibleDescription('Replace all is not available yet: 1 of your expenses belong to tyre log lines, whose amounts a backup cannot bring back to a set that stays.')
  const schedules = within(car.getByRole('radiogroup', { name: 'Recurring expenses of Fabia' })).getByRole('radio', { name: 'Replace all' })
  expect(schedules).toBeDisabled()
  expect(schedules).toHaveAccessibleDescription('The backup has no recurring expenses, so there is nothing to replace yours with.')

  expect(screen.getByRole('button', { name: 'Next' })).toBeEnabled()
  for (const group of ['Refuellings of Fabia', 'Expenses of Fabia', 'Recurring expenses of Fabia']) await ui.click(within(car.getByRole('radiogroup', { name: group })).getByRole('radio', { name: "Don't import" }))
  expect(car.getByText('Choose at least one kind to import for Fabia, or leave the car out.')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
})

it('a new car brings each kind whole or not at all, a box per kind with its count', async () => {
  const { calls, view, ui } = await cars()
  const car = await screen.findByRole('region', { name: 'Octavia' })
  await ui.click(within(car).getByRole('checkbox', { name: 'Octavia' }))
  await ui.click(within(car).getByRole('checkbox', { name: 'As a new car' }))
  await ui.click(within(car).getByRole('button', { name: 'Choose what to import' }))

  expect(within(car).getByText('A new car has nothing to compare with: each kind comes whole, or not at all.')).toBeInTheDocument()
  expect(within(car).getByRole('checkbox', { name: 'Refuellings · 60' })).toBeChecked()
  expect(within(car).getByRole('checkbox', { name: 'Expenses · 5' })).toBeChecked()
  await check(view.container)
  await ui.click(within(car).getByRole('checkbox', { name: 'Recurring expenses · 3' }))
  await ui.click(screen.getByRole('button', { name: 'Next' }))

  const card = await screen.findByRole('region', { name: 'Octavia · a new car' })
  expect(within(card).getByText('No recurring expenses are imported.')).toBeInTheDocument()
  expect(within(card).getByText('Visits come without their link: expenses and recurring expenses are not both imported.')).toBeInTheDocument()
  expect(calls.reviews.at(-1)).toEqual([{ carId: 'c1', targetVehicleId: null, schedules: { mode: 'DONT_IMPORT' }, tyres: { except: [], noSetLines: true }, media: { photos: true, picture: true, charts: true, dashboard: true } }])
})

it('the dialog picks entry by entry: tick and untick all, show more, keep mine or take the backup\'s for one or for all; Done says it on the kind', async () => {
  const { calls, ui } = await cars()
  const car = await open(ui, 'Octavia')
  await ui.click(within(await car.findByRole('radiogroup', { name: 'Refuellings of Octavia' })).getByRole('radio', { name: 'Choose…' }))
  await ui.click(car.getByRole('button', { name: 'Choose refuellings…' }))

  const dialog = within(await screen.findByRole('dialog', { name: 'Choose refuellings · Octavia' }))
  const fresh = within(dialog.getByRole('region', { name: 'Only in the backup · 55' }))
  expect(await fresh.findAllByRole('checkbox')).toHaveLength(50)
  expect(fresh.getAllByRole('checkbox')[0]).toBeChecked()
  expect(fresh.getAllByRole('checkbox')[0]).toHaveAccessibleName(/2026.* · 1,001 · 40 · /)
  await ui.click(fresh.getByRole('button', { name: 'Show 5 more' }))
  await waitFor(() => expect(fresh.getAllByRole('checkbox')).toHaveLength(55))
  expect(fresh.queryByRole('button', { name: /^Show/ })).not.toBeInTheDocument()
  expect(calls.entries.filter((e) => !e.differing).map((e) => [e.skip, e.take])).toEqual([[0, 50], [50, 50]])
  // Untick all is about every entry, shown or not; then two are ticked again.
  await ui.click(fresh.getByRole('button', { name: 'Untick all' }))
  expect(fresh.getAllByRole('checkbox').every((box) => !(box as HTMLInputElement).checked)).toBe(true)
  await ui.click(fresh.getAllByRole('checkbox')[0])
  await ui.click(fresh.getAllByRole('checkbox')[54])

  const differ = within(dialog.getByRole('region', { name: 'Differ · 3' }))
  const first = (await differ.findAllByRole('table'))[0]
  expect(within(first).getAllByRole('row').map((row) => [...row.querySelectorAll('th, td')].map((c) => c.textContent))).toEqual([
    ['Field', 'Yours', "The backup's"],
    ['Note', 'mine', 'none'],
    ['Full tank', 'no', 'yes'],
    ['Volume', '41', '40.5'],
  ])
  const choices = differ.getAllByRole('radiogroup')
  expect(within(choices[0]).getByRole('radio', { name: 'Keep mine' })).toBeChecked()
  await ui.click(differ.getByRole('button', { name: "Take the backup's for all" }))
  expect(choices.every((group) => (within(group).getByRole('radio', { name: "Take the backup's" }) as HTMLInputElement).checked)).toBe(true)
  await ui.click(within(choices[1]).getByRole('radio', { name: 'Keep mine' }))
  expect(dialog.getByRole('region', { name: 'The same · 2' })).toHaveTextContent('Nothing to do.')
  await check(document.body.querySelector('[role="dialog"]') as HTMLElement)

  await ui.click(dialog.getByRole('button', { name: 'Done' }))
  expect(await car.findByText("2 of 55 new are added; 2 of 3 that differ take the backup's values.")).toBeInTheDocument()
  // The picks travel as a default and its exceptions.
  await ui.click(screen.getByRole('button', { name: 'Next' }))
  await waitFor(() => expect(calls.reviews.at(-1)).toEqual([{ carId: 'c1', targetVehicleId: 'v1', refuelings: { mode: 'CHOOSE', addNew: false, newExceptions: ['n1', 'n55'], takeBackups: true, differExceptions: ['d2'] }, tyres: { except: [], noSetLines: true }, media: { photos: true, picture: false, charts: true, dashboard: false } }]))
})

it('the review says what each kind\'s choice does; a car with a Replace all is all or nothing and asks for its name, one car after the other', async () => {
  const { calls, view, ui } = await cars()
  const octavia = await open(ui, 'Octavia')
  await ui.click(within(await octavia.findByRole('radiogroup', { name: 'Refuellings of Octavia' })).getByRole('radio', { name: 'Replace all' }))
  await ui.click(within(octavia.getByRole('radiogroup', { name: 'Expenses of Octavia' })).getByRole('radio', { name: 'Choose…' }))
  await ui.click(within(octavia.getByRole('radiogroup', { name: 'Recurring expenses of Octavia' })).getByRole('radio', { name: 'Replace all' }))
  const fabia = await open(ui, 'Fabia')
  await ui.click(within(await fabia.findByRole('radiogroup', { name: 'Refuellings of Fabia' })).getByRole('radio', { name: "Don't import" }))
  await ui.click(screen.getByRole('button', { name: 'Next' }))

  const replaced = within(await screen.findByRole('region', { name: 'Octavia · added to your car My Octavia' }))
  expect(replaced.getByText("Your 40 refuellings and the 2 in the trash are deleted for good and replaced by the backup's 60.")).toBeInTheDocument()
  expect(replaced.getByText("4 expenses are new; 1 of yours take the backup's values.")).toBeInTheDocument()
  expect(replaced.getByText("Your My Octavia's 4 recurring expenses are deleted for good, then the backup's 3 come in.")).toBeInTheDocument()
  expect(replaced.getByText('A visit keeps its link between its expense and the recurring expenses it marked done.')).toBeInTheDocument()
  expect(replaced.getByText('If an entry cannot be saved, My Octavia stays exactly as it is: nothing of it is deleted or imported.')).toBeInTheDocument()
  expect(replaced.queryByRole('radiogroup')).not.toBeInTheDocument() // no choice of its own: all or nothing
  const other = within(screen.getByRole('region', { name: 'Fabia · added to your car Old Golf' }))
  expect(other.getByText('No refuellings are imported.')).toBeInTheDocument()
  expect(other.getByText('Expenses: 2 new are added; 0 that differ stay as they are; 0 are the same.')).toBeInTheDocument()
  expect(other.getByRole('radio', { name: 'Import the rest' })).toBeChecked()
  await check(view.container)

  await ui.click(screen.getByRole('button', { name: 'Import' }))
  const confirm = within(await screen.findByRole('alertdialog', { name: 'Delete and replace?' }))
  expect(confirm.getByText('Importing deletes 42 refuellings and 4 recurring expenses of your car My Octavia and puts the backup\'s in their place.')).toBeInTheDocument()
  expect(confirm.getByText('2 of them are in the trash now.')).toBeInTheDocument()
  expect(confirm.getByText('2 visits made since the backup lose their link to the recurring expenses they marked done.')).toBeInTheDocument()
  expect(confirm.getByText('This cannot be undone, and they will not be in the trash.')).toBeInTheDocument()
  expect(confirm.getByRole('button', { name: 'Cancel' })).toHaveFocus()
  const go = confirm.getByRole('button', { name: 'Delete and import' })
  expect(go).toBeDisabled()
  await check(document.body.querySelector('[role="alertdialog"]') as HTMLElement)
  const field = confirm.getByRole('textbox', { name: "Type the car's name, My Octavia, to go on" })
  await ui.type(field, 'Octavia')
  expect(go).toBeDisabled()
  await ui.clear(field)
  await ui.type(field, '  my octavia ')
  expect(go).toBeEnabled()
  expect(calls.starts).toEqual([])
  await ui.click(go)

  await waitFor(() => expect(calls.starts).toHaveLength(1))
  expect(calls.starts[0].cars).toEqual([
    { carId: 'c1', targetVehicleId: 'v1', policy: 'IMPORT_THE_REST', refuelings: { mode: 'REPLACE_ALL' }, expenses: { mode: 'CHOOSE', addNew: true, newExceptions: [], takeBackups: false, differExceptions: [] }, schedules: { mode: 'REPLACE_ALL' }, confirmedName: '  my octavia ', tyres: { except: [], noSetLines: true }, media: { photos: true, picture: false, charts: true, dashboard: false } },
    { carId: 'c2', targetVehicleId: 'v2', policy: 'IMPORT_THE_REST', refuelings: { mode: 'DONT_IMPORT' }, tyres: { except: [], noSetLines: true }, media: { photos: true, picture: false, charts: true, dashboard: false } },
  ])
})

it('two cars with a Replace all are confirmed in turn, and Cancel on the second starts nothing', async () => {
  const { calls, ui } = await cars()
  for (const [name, group] of [['Octavia', 'Refuellings of Octavia'], ['Fabia', 'Recurring expenses of Fabia']] as const) {
    const car = await open(ui, name)
    // The Fabia's kinds are held back or empty here; its recurring expenses are replaced in this test's answer below.
    if (name === 'Octavia') await ui.click(within(await car.findByRole('radiogroup', { name: group })).getByRole('radio', { name: 'Replace all' }))
  }
  // The Fabia's refuellings without their photo: a second car that can be replaced.
  AGAINST.c2.refueling.withPhotos = 0
  try {
    const fabia = within(screen.getByRole('region', { name: 'Fabia' }))
    await ui.click(fabia.getByRole('button', { name: 'Choose what to import' }))
    await ui.click(fabia.getByRole('button', { name: 'Choose what to import' })) // closed and opened: compared again
    await ui.click(within(await fabia.findByRole('radiogroup', { name: 'Refuellings of Fabia' })).getByRole('radio', { name: 'Replace all' }))
    await ui.click(screen.getByRole('button', { name: 'Next' }))
    await screen.findByRole('region', { name: 'Fabia · added to your car Old Golf' })
    await ui.click(screen.getByRole('button', { name: 'Import' }))

    const first = within(await screen.findByRole('alertdialog', { name: 'Delete and replace? · 1 of 2' }))
    await ui.type(first.getByRole('textbox'), 'My Octavia')
    await ui.click(first.getByRole('button', { name: 'Next: Old Golf' }))
    const second = within(await screen.findByRole('alertdialog', { name: 'Delete and replace? · 2 of 2' }))
    expect(second.getByText("Importing deletes 3 refuellings of your car Old Golf and puts the backup's in their place.")).toBeInTheDocument()
    expect(second.getByRole('textbox', { name: "Type the car's name, Old Golf, to go on" })).toHaveValue('')
    await ui.click(second.getByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
    expect(calls.starts).toEqual([])
    expect(screen.getByRole('button', { name: 'Import' })).toBeInTheDocument() // back on the review
  } finally {
    AGAINST.c2.refueling.withPhotos = 1
  }
})

it('the report says per kind what was replaced, imported and changed, and a car put back is left as it was, with why', async () => {
  const report = [
    reportCar({ replacing: true, kinds: [kindReport('refueling', 'REPLACE_ALL', 60, 0, 43), kindReport('expense', 'CHOOSE', 1, 2, 0), kindReport('recurring', 'DONT_IMPORT', 0, 0, 0)] }),
    reportCar({ carId: 'c2', name: 'Fabia', targetName: 'Old Golf', vehicleId: null, leftOut: true, replacing: true, kinds: [],
      refused: [{ __typename: 'ImportRefusedEntryInfo', kind: 'refueling', date: '2026-03-11', title: null, key: 'odometer.belowPrevious', args: { value: 20000, date: '2026-03-06' } }] }),
    reportCar({ carId: 'c3', name: 'Golf', targetName: 'My Golf', vehicleId: null, leftOut: true, replacing: true, kinds: [],
      refused: [{ __typename: 'ImportRefusedEntryInfo', kind: 'car', date: null, title: null, key: 'import.replaceHeldBack', args: { kind: 'expense', photos: 1, lines: 0 } }] }),
  ]
  const { view } = await cars(run('DONE', { report }))

  expect(await screen.findByText('Import finished: 1 of 3 cars.')).toBeInTheDocument()
  const done = within(screen.getByRole('region', { name: 'Octavia' }))
  expect(done.getByText('Replaced refuellings: 43 deleted, 60 imported.')).toBeInTheDocument()
  expect(done.getByText("Imported 1 expense; changed 2 to the backup's values.")).toBeInTheDocument()
  expect(done.queryByText(/recurring/)).not.toBeInTheDocument() // left out: nothing to say
  expect(done.getByRole('link', { name: 'Open the vehicle: My Octavia' })).toBeInTheDocument()
  const refused = within(screen.getByRole('region', { name: 'Fabia' }))
  expect(refused.getByRole('heading', { name: 'Old Golf · left as it was' })).toBeInTheDocument()
  expect(refused.getByText("Nothing of Old Golf was deleted or imported: one of the backup's refuellings could not be saved.")).toBeInTheDocument()
  expect(refused.getByText(/^The refuelling of .*2026.*: /)).toBeInTheDocument()
  expect(refused.queryByRole('link')).not.toBeInTheDocument()
  const held = within(screen.getByRole('region', { name: 'Golf' }))
  expect(held.getByRole('heading', { name: 'My Golf · left as it was' })).toBeInTheDocument()
  expect(held.getByText('Nothing of My Golf was deleted or imported: since the review, one of its expenses got a tyre log line, which the backup could not bring back.')).toBeInTheDocument()
  await check(view.container)
})

it('the tyres come set by set: a new set ticked, one here or refused shown with why, the lines of no set last; the review and the report say it', async () => {
  TYRES = {
    c1: {
      __typename: 'ImportTyreReviewInfo', noSetLines: 2, noSetExpenses: 1, expenses: 4,
      sets: [
        offer('s1', 'Nokian WR', 'NEW'),
        offer('s2', 'Michelin Primacy', 'HERE', { season: 'SUMMER' }),
        offer('s3', 'Hankook', 'REFUSED', { refusalKey: 'tyres.twoSetsAtOnce', refusalArgs: { set: 'Old', from: '2024-11-01', until: null } }),
      ],
    },
  }
  const { calls, view, ui } = await cars()
  const car = await open(ui, 'Octavia')

  const rows = within(await car.findByRole('group', { name: 'Tyre sets of Octavia' }))
  const nokian = rows.getByRole('checkbox', { name: 'Nokian WR' })
  expect(nokian).toBeChecked()
  expect(nokian).toHaveAccessibleDescription('Winter · 3 times on the car · 6 lines · 5 single tyres')
  const here = rows.getByRole('checkbox', { name: 'Michelin Primacy' })
  expect(here).toBeDisabled()
  expect(here).toHaveAccessibleDescription(/Already here: it stays as it is\.$/)
  expect(rows.getByRole('checkbox', { name: 'Hankook' })).toBeDisabled()
  expect(rows.getByRole('checkbox', { name: 'Hankook' })).toHaveAccessibleDescription(/Old is on the car then, from .*2024/)
  const noSet = rows.getByRole('checkbox', { name: 'Lines not tied to a set' })
  expect(noSet).toHaveAccessibleDescription('2 lines of the tyre log that belong to no set · 1 expenses')
  await check(view.container)

  await ui.click(noSet)
  await ui.click(screen.getByRole('button', { name: 'Next' }))
  const card = within(await screen.findByRole('region', { name: 'Octavia · added to your car My Octavia' }))
  expect(card.getByText('1 set is new: Nokian WR, with its times on the car, lines and single tyres, and the 4 expenses its lines hold.')).toBeInTheDocument()
  expect(card.getByText('1 set is already here and stays as it is: Michelin Primacy.')).toBeInTheDocument()
  expect(card.getByText(/^Hankook cannot come: Old is on the car then, from .*2024/)).toBeInTheDocument()
  expect(card.queryByText(/lines of the tyre log that belong to no set/)).not.toBeInTheDocument() // unticked
  expect(card.getByText('The swap reminders of imported sets start afresh.')).toBeInTheDocument()
  expect(calls.reviews.at(-1)?.[0]).toMatchObject({ tyres: { except: [], noSetLines: false } })

  // Unticking the new set: it goes into the choice's exceptions.
  await ui.click(screen.getByRole('button', { name: 'Back' }))
  const again = within(await screen.findByRole('region', { name: 'Octavia' }))
  await ui.click(again.getByRole('button', { name: 'Choose what to import' })) // still ticked, closed again
  await ui.click(within(await again.findByRole('group', { name: 'Tyre sets of Octavia' })).getByRole('checkbox', { name: 'Nokian WR' }))
  await ui.click(screen.getByRole('button', { name: 'Next' }))
  await waitFor(() => expect(calls.reviews.at(-1)?.[0]).toMatchObject({ tyres: { except: ['s1'] } }))
})

it('a new car names its sets coming with their history, and the report says what came and what amounts were there already', async () => {
  TYRES = { c1: { __typename: 'ImportTyreReviewInfo', noSetLines: 0, noSetExpenses: 0, expenses: 2, sets: [offer('s1', 'Nokian WR', 'NEW'), offer('s2', 'Michelin', 'NEW', { season: 'SUMMER' })] } }
  const { ui } = await cars()
  const car = await screen.findByRole('region', { name: 'Octavia' })
  await ui.click(within(car).getByRole('checkbox', { name: 'Octavia' }))
  await ui.click(within(car).getByRole('checkbox', { name: 'As a new car' }))
  await ui.click(within(car).getByRole('button', { name: 'Choose what to import' }))
  expect(await within(car).findByRole('checkbox', { name: 'Michelin' })).toBeChecked()
  await ui.click(screen.getByRole('button', { name: 'Next' }))
  expect(await screen.findByText('2 tyre sets come with their history: Nokian WR and Michelin, and the 2 expenses their lines hold.')).toBeInTheDocument()
})

it('the report says the tyres imported, the lines of no set, the amounts already among the expenses, and a set that could not come', async () => {
  const report = [
    reportCar({ tyres: { __typename: 'ImportTyreReportInfo', sets: ['Nokian WR', 'Michelin'], lines: 9, tyres: 5, noSetLines: 2, expenses: 4, amountsHere: 3 },
      refused: [{ __typename: 'ImportRefusedEntryInfo', kind: 'tyreSet', date: null, title: 'Hankook', key: 'tyres.twoSetsAtOnce', args: { set: 'Old', from: '2024-11-01' } }] }),
  ]
  const { view } = await cars(run('DONE', { report }))

  const done = within(await screen.findByRole('region', { name: 'Octavia' }))
  expect(done.getByText('Imported 2 tyre sets (Nokian WR, Michelin) with 9 lines and 5 single tyres.')).toBeInTheDocument()
  expect(done.getByText('Imported 2 lines of the tyre log that belong to no set.')).toBeInTheDocument()
  expect(done.getByText('3 amounts of their lines are already among your expenses: they stay there, and the lines come without them.')).toBeInTheDocument()
  expect(done.getByText(/^The tyre set Hankook: Old is on the car then, from .*2024/)).toBeInTheDocument()
  await check(view.container)
})

it('a car can bring only its tyres: every log kind left out goes on while a set comes, and not once none does', async () => {
  TYRES = { c1: { __typename: 'ImportTyreReviewInfo', noSetLines: 0, noSetExpenses: 0, expenses: 3, sets: [offer('s1', 'Nokian WR', 'NEW')] } }
  const { calls, ui } = await cars()
  const car = await open(ui, 'Octavia')
  for (const group of ['Refuellings of Octavia', 'Expenses of Octavia', 'Recurring expenses of Octavia'])
    await ui.click(within(await car.findByRole('radiogroup', { name: group })).getByRole('radio', { name: "Don't import" }))
  expect(screen.getByRole('button', { name: 'Next' })).toBeEnabled()
  // The comparison the rows come from is asked with the tyre choice: the sets' expenses are not the expenses'.
  expect(calls.reviews.at(-1)?.[0]).toMatchObject({ tyres: { except: [], noSetLines: true } })

  await ui.click(within(car.getByRole('group', { name: 'Tyre sets of Octavia' })).getByRole('checkbox', { name: 'Nokian WR' }))
  expect(car.getByText('Choose at least one kind to import for Octavia, or leave the car out.')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
})

it("the media come by their boxes: photos and charts ticked, the picture and the dashboard not over the car's own; the review and the choice say it", async () => {
  MEDIA = { c1: mediaOf() }
  const { calls, view, ui } = await cars()
  const car = await open(ui, 'Octavia')

  const photos = await car.findByRole('checkbox', { name: 'Photos of entries' })
  expect(photos).toBeChecked()
  const picture = car.getByRole('checkbox', { name: "The car's picture" })
  expect(picture).not.toBeChecked() // My Octavia has one
  expect(picture).toHaveAccessibleDescription("Ticked, the backup's replaces it for good.")
  expect(car.getByRole('checkbox', { name: 'Charts' })).toBeChecked()
  const dashboard = car.getByRole('checkbox', { name: 'The dashboard' })
  expect(dashboard).not.toBeChecked()
  expect(dashboard).toHaveAccessibleDescription("Ticked, the backup's replaces it for everyone who sees this car.")
  await check(view.container)

  await ui.click(car.getByRole('checkbox', { name: 'Charts' }))
  await ui.click(screen.getByRole('button', { name: 'Next' }))
  const card = within(await screen.findByRole('region', { name: 'Octavia · added to your car My Octavia' }))
  expect(card.getByText('3 photos of entries that are imported or already here are new; 1 are already here.')).toBeInTheDocument()
  expect(card.getByText("Your My Octavia has one; the backup's is not taken.")).toBeInTheDocument()
  expect(card.queryByText(/shared charts are new/)).not.toBeInTheDocument() // unticked
  expect(card.getByText(/^Your My Octavia has its own; the backup's is not taken unless you tick it/)).toBeInTheDocument()
  expect(calls.reviews.at(-1)?.[0]).toMatchObject({ media: { photos: true, picture: false, charts: false, dashboard: false } })
})

it("a new car takes its picture and dashboard, says the charts and the importer's own coming back, and a backup without photos says so", async () => {
  MEDIA = { c1: mediaOf({ photosLeftOut: true, photosNew: 0, photosHere: 0, targetPicture: false, targetDashboard: false }) }
  const { calls, ui } = await cars()
  const car = await screen.findByRole('region', { name: 'Octavia' })
  await ui.click(within(car).getByRole('checkbox', { name: 'Octavia' }))
  await ui.click(within(car).getByRole('checkbox', { name: 'As a new car' }))
  await ui.click(within(car).getByRole('button', { name: 'Choose what to import' }))
  const photos = await within(car).findByRole('checkbox', { name: 'Photos of entries' })
  expect(photos).toBeDisabled()
  expect(photos).toHaveAccessibleDescription('This backup was made without the photos of entries; none can be imported.')
  expect(within(car).getByRole('checkbox', { name: "The car's picture" })).toBeChecked()
  await ui.click(screen.getByRole('button', { name: 'Next' }))

  const card = within(await screen.findByRole('region', { name: 'Octavia · a new car' }))
  expect(card.getByText('This backup was made without the photos of entries; none can be imported.')).toBeInTheDocument()
  expect(card.getByText("The backup's picture comes with the car.")).toBeInTheDocument()
  expect(card.getByText("2 shared charts are new; 0 are already here. Charts that were someone's own are not imported.")).toBeInTheDocument()
  expect(card.getByText('1 of them is your own, and comes back to you.')).toBeInTheDocument()
  expect(card.getByText("The backup's dashboard comes with the car.")).toBeInTheDocument()
  expect(card.getByText('Your own dashboard of this car comes back to you.')).toBeInTheDocument()
  expect(calls.reviews.at(-1)?.[0]).toMatchObject({ media: { picture: true, dashboard: true } })
})

it('the confirmation of a Replace all counts the photos the backup does not bring back, and the report says the media that came', async () => {
  const replaced = await cars(run('DONE', { report: [reportCar({ media: { __typename: 'ImportMediaReportInfo', photos: 3, photosHere: 1, picture: true, charts: 2, chartsHere: 0, dashboard: true, ownDashboard: true, tilesLeftOut: 1 } })] }))
  const done = within(await screen.findByRole('region', { name: 'Octavia' }))
  expect(done.getByText('Imported 3 photos.')).toBeInTheDocument()
  expect(done.getByText('1 photo was there already.')).toBeInTheDocument()
  expect(done.getByText("Imported the car's picture.")).toBeInTheDocument()
  expect(done.getByText('Imported 2 charts.')).toBeInTheDocument()
  expect(done.getByText('Imported the dashboard.')).toBeInTheDocument()
  expect(done.getByText('Imported your own dashboard.')).toBeInTheDocument()
  expect(done.getByText('1 tile of the dashboard was left out: its chart did not come.')).toBeInTheDocument()
  replaced.view.unmount()

  AGAINST.c1.refueling.photosLost = 3
  try {
    const { ui } = await cars()
    const octavia = await open(ui, 'Octavia')
    await ui.click(within(await octavia.findByRole('radiogroup', { name: 'Refuellings of Octavia' })).getByRole('radio', { name: 'Replace all' }))
    await ui.click(screen.getByRole('button', { name: 'Next' }))
    await screen.findByRole('region', { name: 'Octavia · added to your car My Octavia' })
    await ui.click(screen.getByRole('button', { name: 'Import' }))
    const confirm = within(await screen.findByRole('alertdialog', { name: 'Delete and replace?' }))
    expect(confirm.getByText('3 of their photos are not in the backup and are lost for good.')).toBeInTheDocument()
  } finally {
    AGAINST.c1.refueling.photosLost = 0
  }
})
