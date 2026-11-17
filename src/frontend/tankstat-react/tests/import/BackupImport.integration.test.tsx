import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { HttpResponse } from 'msw'
import { useState } from 'react'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import { ImportPage } from '../../src/pages/ImportPage.tsx'
import { BackupImport, type BackupStep } from '../../src/pages/import/BackupImport.tsx'
import { check } from '../support/accessibility.tsx'
import { graphql } from '../support/graphql.ts'
import { renderWithApollo, stubViewport } from '../support/mocks.tsx'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

const car = (id: string, name: string, over: Record<string, unknown> = {}) => ({
  __typename: 'BackupCarInfo', id, name, licensePlate: null, ownerName: 'Anna', distanceUnit: 'KILOMETERS', volumeUnit: 'LITERS', refuelings: 412, expenses: 57, schedules: 3, ...over,
})
const own = (id: string, name: string, over: Record<string, unknown> = {}) => ({ __typename: 'ImportOwnCarInfo', id, name, licensePlate: null, distanceUnit: 'KILOMETERS', volumeUnit: 'LITERS', ...over })
const offers = (over: Record<string, unknown> = {}) => ({
  __typename: 'ImportCarOffersInfo',
  file: { __typename: 'BackupFileInfo', createdAt: '2026-11-03T08:15:00Z', version: '0.21.0', trashedCars: 1 },
  cars: [
    { __typename: 'ImportCarOfferInfo', ownedByMe: false, suggestedTargetId: 'v1', car: car('c1', 'Octavia', { licensePlate: 'ABC-123' }) },
    { __typename: 'ImportCarOfferInfo', ownedByMe: true, suggestedTargetId: null, car: car('c2', 'Fabia', { ownerName: 'Me', refuelings: 10, expenses: 2, schedules: 0 }) },
    { __typename: 'ImportCarOfferInfo', ownedByMe: false, suggestedTargetId: null, car: car('c3', 'Mustang', { distanceUnit: 'MILES', volumeUnit: 'US_GALLONS' }) },
  ],
  targets: [own('v1', 'My Octavia', { licensePlate: 'ABC 123' }), own('v2', 'Old Golf')],
  ...over,
})
/** One kind of a reviewed car: only the missing unless said otherwise. */
const kindOf = (kind: string, over: Record<string, unknown> = {}) => ({
  __typename: 'ImportKindReviewInfo', kind, mode: 'ONLY_MISSING', backup: 0, new: 0, differ: 0, same: 0, inTrash: 0, adds: 0, takes: 0, targetLive: 0, targetTrashed: 0, withPhotos: 0, onTyreLines: 0, photosLost: 0, ...over,
})
const reviewOf = (carId: string, name: string, over: Record<string, unknown> = {}) => ({
  __typename: 'ImportCarReviewInfo', carId, name, ownerName: 'Anna', ownedByMe: false, targetVehicleId: null, targetName: null,
  newRefuelings: 10, newExpenses: 2, newSchedules: 0, existingRefuelings: 0, existingExpenses: 0, existingSchedules: 0, differing: 0, inTrash: 0,
  links: true, linksLost: 0, kinds: [kindOf('refueling'), kindOf('expense'), kindOf('recurring')], tyres: { __typename: 'ImportTyreReviewInfo', sets: [], noSetLines: 0, noSetExpenses: 0, expenses: 0 }, media: null, ...over,
})
const run = (state: string, over: Record<string, unknown> = {}) => ({
  __typename: 'ImportRunInfo', id: 'run1', kind: 'IMPORT', startedByName: 'Me', mine: true, state, failure: null, queuedAt: '2026-11-16T10:00:00Z', startedAt: null, ahead: 0,
  fileToken: 't1', limitMinutes: 10, progress: { __typename: 'ImportRunProgressInfo', car: 1, cars: 2, done: 310, total: 470 },
  cars: [{ __typename: 'ImportRunCarInfo', carId: 'c1', name: 'Octavia', targetName: 'My Octavia' }], report: null, ...over,
})
const reportCar = (over: Record<string, unknown> = {}) => ({
  __typename: 'ImportCarReportInfo', carId: 'c1', name: 'Octavia', vehicleId: 'v1', targetName: 'My Octavia', asNew: false, leftOut: false,
  refuelings: 300, expenses: 50, schedules: 2, existing: 112, differing: 4, inTrash: 1, refusedMore: 0, refused: [], replacing: false, links: 0, kinds: [], tyres: null, media: null, ...over,
})

/** The flow from the Cars step on (the file is #336's step, tested there), with the server's answers; what was asked is kept. */
function setup(opts: { step?: BackupStep; offers?: unknown; review?: (cars: { carId: string; targetVehicleId: string | null }[]) => unknown[]; started?: unknown; statuses?: unknown[]; found?: unknown; afterAnother?: BackupStep } = {}) {
  stubViewport('desktop')
  const calls = { reviews: [] as unknown[], starts: [] as { token: string; cars: unknown[] }[], cancelled: [] as string[], asked: 0, another: vi.fn() }
  server.use(
    graphql.query('VehicleDefaults', () => HttpResponse.json({ data: { vehicleDefaults: { distanceUnit: 'KILOMETERS', volumeUnit: 'LITERS', currency: 'EUR', recurringWarnDays: 30, recurringWarnDistance: 500 } } })),
    graphql.query('BackupImportCars', () => HttpResponse.json({ data: { backupImportCars: opts.offers ?? offers() } })),
    graphql.query('BackupImportReview', ({ variables }) => {
      const cars = variables.cars as { carId: string; targetVehicleId: string | null }[]
      calls.reviews.push(cars)
      return HttpResponse.json({ data: { backupImportReview: (opts.review ?? ((c) => c.map((x) => reviewOf(x.carId, x.carId === 'c1' ? 'Octavia' : 'Fabia'))))(cars) } })
    }),
    graphql.mutation('StartBackupImport', ({ variables }) => {
      calls.starts.push(variables.input as { token: string; cars: unknown[] })
      return HttpResponse.json({ data: { startBackupImport: opts.started ?? run('RUNNING') } })
    }),
    graphql.query('ImportRun', () => {
      const statuses = opts.statuses ?? [run('RUNNING')]
      return HttpResponse.json({ data: { importRun: statuses[Math.min(calls.asked++, statuses.length - 1)] } })
    }),
    graphql.mutation('CancelImportRun', ({ variables }) => {
      calls.cancelled.push(variables.id as string)
      return HttpResponse.json({ data: { cancelImportRun: true } })
    }),
  )
  function Harness() {
    const [step, setStep] = useState<BackupStep>(opts.step ?? 'cars')
    return (
      <>
        <p data-testid="step">{step}</p>
        <BackupImport
          step={step}
          onStep={setStep}
          run={opts.found as never}
          onAnother={() => {
            calls.another()
            if (opts.afterAnother) setStep(opts.afterAnother)
          }}
          pollMs={5}
        />
      </>
    )
  }
  // The file step is skipped: a session token as the file step would have handed it on.
  const view = renderWithApollo(<Harness />)
  return { calls, view, ui: userEvent.setup() }
}

/** The flow with an opened file: the found run's token stands in for the file step's. */
const withFile = (opts: Parameters<typeof setup>[0] = {}) => setup({ ...opts, found: opts.found ?? undefined })

it("the cars step lists the file's cars, with whose they were, their counts and units, and Next stays off until one is ticked", async () => {
  const { view, ui } = withFile({ found: run('NOT_FINISHED', { failure: 'CANCELLED' }), step: 'done' })
  await ui.click(await screen.findByRole('button', { name: 'Back to the review' })) // the file is still open: its token comes back, and with no choices here, the cars
  expect(screen.getByTestId('step')).toHaveTextContent('cars')

  expect(await screen.findByText(/^A backup of .*2026.*, written by Tankstat 0\.21\.0: 3 cars\. Pick the ones to import; what you import becomes yours\.$/)).toBeInTheDocument()
  expect(screen.getByText('1 car of this file was in the trash and is left out.')).toBeInTheDocument()
  const octavia = screen.getByRole('region', { name: 'Octavia · ABC-123' })
  expect(within(octavia).getByText('412 refuellings · 57 expenses · 3 recurring expenses')).toBeInTheDocument()
  expect(within(octavia).getByText("was Anna's")).toBeInTheDocument()
  expect(within(screen.getByRole('region', { name: 'Fabia' })).queryByText(/was .*'s/)).not.toBeInTheDocument() // the importer's own in the backup
  expect(within(screen.getByRole('region', { name: 'Mustang' })).getByText('Units: mi, gal')).toBeInTheDocument() // not the instance's usual ones
  expect(within(octavia).queryByText(/Units:/)).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
  expect(screen.getByText('Tick at least one car.')).toBeInTheDocument()
  await check(view.container)
})

it("a ticked car comes as a new car or into one of the person's own cars, the app's match preselected and said; other units and a car taken twice hold Next back", async () => {
  const { view, ui } = withFile({ found: run('NOT_FINISHED', { failure: 'CANCELLED' }), step: 'done' })
  await ui.click(await screen.findByRole('button', { name: 'Back to the review' }))
  const octavia = await screen.findByRole('region', { name: 'Octavia · ABC-123' })

  await ui.click(within(octavia).getByRole('checkbox', { name: 'Octavia · ABC-123' }))
  expect(within(octavia).getByRole('checkbox', { name: 'As a new car' })).not.toBeChecked() // a match was found
  const targets = within(octavia).getByRole('radiogroup', { name: 'Add to one of your cars:' })
  expect(within(targets).getByRole('radio', { name: 'My Octavia · ABC 123 · suggested' })).toBeChecked()
  expect(within(targets).getByRole('radio', { name: 'Old Golf' })).not.toBeChecked()
  expect(screen.getByRole('button', { name: 'Next' })).toBeEnabled()

  // Nothing matches the Fabia: a new car, unless one is chosen.
  const fabia = screen.getByRole('region', { name: 'Fabia' })
  await ui.click(within(fabia).getByRole('checkbox', { name: 'Fabia' }))
  expect(within(fabia).getByRole('checkbox', { name: 'As a new car' })).toBeChecked()
  await ui.click(within(fabia).getByRole('checkbox', { name: 'As a new car' }))
  expect(within(fabia).getByText('Choose a car, or tick As a new car.')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
  await ui.click(within(fabia).getByRole('radio', { name: /^My Octavia/ }))
  expect(within(fabia).getByText('My Octavia already takes another car of this file.')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
  await ui.click(within(fabia).getByRole('radio', { name: 'Old Golf' }))
  expect(screen.getByRole('button', { name: 'Next' })).toBeEnabled()

  // Other units: only as a new car.
  const mustang = screen.getByRole('region', { name: 'Mustang' })
  await ui.click(within(mustang).getByRole('checkbox', { name: 'Mustang' }))
  await ui.click(within(mustang).getByRole('checkbox', { name: 'As a new car' }))
  await ui.click(within(mustang).getByRole('radio', { name: 'Old Golf' }))
  expect(within(mustang).getByText('Mustang is in mi, gal, Old Golf in km, L: it can only come as a new car.')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
  await check(view.container)
})

it('the review says per car what is new, exists, differs and is in the trash; the rest is imported by default; Import starts the run with the choices', async () => {
  const { calls, view, ui } = withFile({
    found: run('NOT_FINISHED', { failure: 'CANCELLED' }),
    step: 'done',
    review: (cars) => cars.map((c) => (c.carId === 'c1'
      ? reviewOf('c1', 'Octavia', { targetVehicleId: 'v1', targetName: 'My Octavia', newRefuelings: 300, newExpenses: 50, newSchedules: 2, existingRefuelings: 112, existingExpenses: 7, existingSchedules: 1, differing: 4, inTrash: 1 })
      : reviewOf('c2', 'Fabia', { ownedByMe: true }))),
    statuses: [run('RUNNING'), run('DONE', { report: [reportCar()] })],
  })
  await ui.click(await screen.findByRole('button', { name: 'Back to the review' }))
  await ui.click(within(await screen.findByRole('region', { name: 'Octavia · ABC-123' })).getByRole('checkbox', { name: 'Octavia · ABC-123' }))
  await ui.click(within(screen.getByRole('region', { name: 'Fabia' })).getByRole('checkbox', { name: 'Fabia' }))
  await ui.click(screen.getByRole('button', { name: 'Next' }))

  const added = await screen.findByRole('region', { name: 'Octavia · added to your car My Octavia' })
  expect(within(added).getByText("was Anna's")).toBeInTheDocument()
  expect(within(added).getByText('300 refuellings, 50 expenses and 2 recurring expenses are new.')).toBeInTheDocument()
  expect(within(added).getByText('112 refuellings, 7 expenses and 1 recurring expenses already exist and are skipped.')).toBeInTheDocument()
  expect(within(added).getByText('4 entries differ from yours and stay as they are.')).toBeInTheDocument()
  expect(within(added).getByText("1 entry is in My Octavia's trash and is not imported; restore it there if you want it.")).toBeInTheDocument()
  expect(within(added).getByText('Adding entries to My Octavia changes its figures and charts for those months.')).toBeInTheDocument()
  const fresh = screen.getByRole('region', { name: 'Fabia · a new car' })
  expect(within(fresh).queryByText(/changes its figures/)).not.toBeInTheDocument()
  expect(within(fresh).queryByText(/was .*'s/)).not.toBeInTheDocument()
  expect(within(added).getByRole('radio', { name: 'Import the rest' })).toBeChecked() // the owner's default
  expect(screen.getByText('Everything imported is yours. Sharing and who logged an entry are not carried over; entries are not marked for review.')).toBeInTheDocument()
  expect(screen.getByText('While it runs, saving may have to wait for a moment, for you and for others.')).toBeInTheDocument()
  await check(view.container)

  const asked = calls.reviews.length
  await ui.click(within(fresh).getByRole('radio', { name: 'Leave this car out' }))
  expect(calls.reviews).toHaveLength(asked) // a policy asks nothing again
  await ui.click(screen.getByRole('button', { name: 'Import' }))

  expect(await screen.findByRole('status')).toHaveTextContent('Importing… car 1 of 2 · 310 of 470 entries')
  expect(calls.starts).toEqual([{ token: 't1', cars: [
    { carId: 'c1', targetVehicleId: 'v1', policy: 'IMPORT_THE_REST', tyres: { except: [], noSetLines: true }, media: { photos: true, picture: null, charts: true, dashboard: null } },
    { carId: 'c2', targetVehicleId: null, policy: 'LEAVE_CAR_OUT', tyres: { except: [], noSetLines: true }, media: { photos: true, picture: null, charts: true, dashboard: null } },
  ] }])
  expect(await screen.findByText('Import finished: 1 car.')).toBeInTheDocument()
  expect(screen.getByTestId('step')).toHaveTextContent('done')
})

it('a run that goes says how far it is and can be left or cancelled behind a confirmation; one that waits says how many are ahead', async () => {
  const { calls, view, ui } = setup({ step: 'review', found: run('WAITING', { ahead: 2 }), statuses: [run('WAITING', { ahead: 1 }), run('RUNNING'), run('RUNNING')] })

  expect(screen.getByRole('status')).toHaveTextContent('Waiting for other imports to finish (2 ahead of you). Yours starts by itself. The file is kept while it waits.')
  await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Importing… car 1 of 2 · 310 of 470 entries'))
  expect(screen.getByText('You can leave this page: a notification tells you when it is done. Nothing is kept unless everything goes through.')).toBeInTheDocument()
  await check(view.container)

  await ui.click(screen.getByRole('button', { name: 'Cancel the import' }))
  const dialog = await screen.findByRole('alertdialog', { name: 'Cancel the import?' })
  expect(within(dialog).getByText('Nothing will be imported.')).toBeInTheDocument()
  await ui.click(within(dialog).getByRole('button', { name: 'Cancel the import' }))
  await waitFor(() => expect(calls.cancelled).toEqual(['run1']))
})

it('one import ahead is one', () => {
  setup({ step: 'review', found: run('WAITING', { ahead: 1 }), statuses: [run('WAITING', { ahead: 1 })] })
  expect(screen.getByRole('status')).toHaveTextContent('Waiting for another import to finish (1 ahead of you).')
})

it('the report says per car what came in and what was skipped, each entry that could not be saved with its reason, a car left out, and opens the car', async () => {
  const { calls, view, ui } = setup({
    step: 'done',
    found: run('DONE', {
      report: [
        reportCar({ refused: [
          { __typename: 'ImportRefusedEntryInfo', kind: 'refueling', date: '2026-03-11', title: null, key: 'vehicle.viewOnly', args: {} },
          { __typename: 'ImportRefusedEntryInfo', kind: 'expense', date: '2026-03-12', title: 'Car wash', key: 'expense.titleRequired', args: {} },
        ], refusedMore: 3 }),
        reportCar({ carId: 'c2', name: 'Fabia', vehicleId: null, targetName: null, asNew: true, leftOut: true, refuelings: 0, expenses: 0, schedules: 0, existing: 0, differing: 0, inTrash: 0,
          refused: [{ __typename: 'ImportRefusedEntryInfo', kind: 'recurring', date: null, title: 'Oil change', key: 'recurring.titleRequired', args: {} }] }),
        // Left out for a refusal of its own: its target went to the trash while the run waited.
        reportCar({ carId: 'c3', name: 'Superb', vehicleId: null, targetName: 'My Superb', asNew: false, leftOut: true, refuelings: 0, expenses: 0, schedules: 0, existing: 0, differing: 0, inTrash: 0,
          refused: [{ __typename: 'ImportRefusedEntryInfo', kind: 'car', date: null, title: null, key: 'vehicle.notFound', args: {} }] }),
      ],
    }),
  })

  expect(screen.getByText('Import finished: 1 car.')).toBeInTheDocument()
  const octavia = screen.getByRole('region', { name: 'Octavia' })
  expect(within(octavia).getByRole('heading', { name: 'Octavia · added to your car My Octavia' })).toBeInTheDocument()
  expect(within(octavia).getByText('Imported 300 refuellings, 50 expenses and 2 recurring expenses.')).toBeInTheDocument()
  expect(within(octavia).getByText("112 entries already existed and were skipped; 4 differ and stay as they are; 1 are in the car's trash.")).toBeInTheDocument()
  expect(within(octavia).getByText('5 entries could not be saved:')).toBeInTheDocument()
  expect(within(octavia).getByText(/^The refuelling of .*2026.*: You may only view this vehicle\.$/)).toBeInTheDocument()
  expect(within(octavia).getByText(/^Car wash \(.*2026.*\): /)).toBeInTheDocument()
  expect(within(octavia).getByText('…and 3 more.')).toBeInTheDocument()
  expect(within(octavia).getByRole('link', { name: 'Open the vehicle: My Octavia' })).toHaveAttribute('href', '/vehicles/v1')
  const fabia = screen.getByRole('region', { name: 'Fabia' })
  expect(within(fabia).getByText('This car was left out: one of its entries could not be saved.')).toBeInTheDocument()
  expect(within(fabia).getByText(/^Oil change \(recurring expense\): /)).toBeInTheDocument()
  expect(within(fabia).queryByRole('link')).not.toBeInTheDocument()
  const superb = screen.getByRole('region', { name: 'Superb' })
  expect(within(superb).getByText('This car was left out: This vehicle does not exist.')).toBeInTheDocument()
  expect(within(superb).queryByText(/could not be saved/)).not.toBeInTheDocument() // the car's own refusal is no entry
  expect(within(superb).queryByRole('listitem')).not.toBeInTheDocument()
  await check(view.container)

  await ui.click(screen.getByRole('button', { name: 'Import another file' }))
  expect(calls.another).toHaveBeenCalled()
})

it("after Import another file the run is let go: a later review never shows the earlier run's report", async () => {
  // The page keeps this component mounted for a run started on it; the next file's steps come back to Review.
  const { ui } = setup({ step: 'done', found: run('DONE', { report: [reportCar()] }), afterAnother: 'review' })
  expect(screen.getByText('Import finished: 1 car.')).toBeInTheDocument()

  await ui.click(screen.getByRole('button', { name: 'Import another file' }))

  expect(await screen.findByRole('button', { name: 'Choose file' })).toBeInTheDocument() // no file yet: the file step, whatever the step says
  expect(screen.queryByText('Import finished: 1 car.')).not.toBeInTheDocument()
})

it.each([
  ['CANCELLED', 'The import was cancelled. Nothing was imported: your cars are as they were.'],
  ['TIMED_OUT', 'The import took longer than the server allows (10 minutes) and was stopped. Nothing was imported: your cars are as they were.'],
  ['INTERRUPTED', 'The import did not finish: the server restarted while it ran. Nothing was imported: your cars are as they were.'],
  ['DID_NOT_START', 'Your import did not start: another one took too long. Nothing was imported. Nothing was imported: your cars are as they were.'],
  ['FAILED', 'The import did not finish: something unexpected happened on the server. Nothing was imported: your cars are as they were.'],
])('a run that ended as %s says so, and goes back to the review only while its file is kept', async (failure, text) => {
  const kept = setup({ step: 'done', found: run('NOT_FINISHED', { failure }) })
  expect(screen.getByRole('alert')).toHaveTextContent(text)
  expect(screen.getByRole('button', { name: 'Back to the review' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Import another file' })).toBeInTheDocument()
  kept.view.unmount()

  setup({ step: 'done', found: run('NOT_FINISHED', { failure, fileToken: null }) })
  expect(screen.queryByRole('button', { name: 'Back to the review' })).not.toBeInTheDocument()
})

it('the page offers the source only to who may import from a backup, with its own steps', async () => {
  stubViewport('desktop')
  server.use(graphql.query('CanImportFromBackup', () => HttpResponse.json({ data: { canImportFromBackup: true } })))
  const ui = userEvent.setup()
  renderWithApollo(<ImportPage />)

  await ui.click(await screen.findByRole('combobox', { name: 'Source' }))
  await ui.click(await screen.findByRole('option', { name: 'Tankstat backup or export' }))

  expect(await screen.findByText('A backup file of this app, or an export made on its Import and export page (.zip).')).toBeInTheDocument()
  expect(within(screen.getByRole('list', { name: 'Import' })).getAllByRole('listitem').map((s) => s.textContent?.replace(/^\d+/, ''))).toEqual(['File', 'Cars', 'Review', 'Done'])
})

it('the page does not offer the source where importing from a backup is turned off', async () => {
  stubViewport('desktop')
  const ui = userEvent.setup()
  renderWithApollo(<ImportPage />)

  await ui.click(await screen.findByRole('combobox', { name: 'Source' }))

  expect(await screen.findByRole('option', { name: 'Fuelio (CSV)' })).toBeInTheDocument()
  expect(screen.queryByRole('option', { name: 'Tankstat backup or export' })).not.toBeInTheDocument()
})

it("coming back to the page while an import of the person's goes shows it, without the source", async () => {
  stubViewport('desktop')
  server.use(
    graphql.query('MyImportRun', () => HttpResponse.json({ data: { myImportRun: run('RUNNING') } })),
    graphql.query('ImportRun', () => HttpResponse.json({ data: { importRun: run('RUNNING') } })),
  )
  renderWithApollo(<ImportPage />)

  expect(await screen.findByText('Importing… car 1 of 2 · 310 of 470 entries')).toBeInTheDocument()
  expect(screen.queryByRole('combobox', { name: 'Source' })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Cancel the import' })).toBeInTheDocument()
})

it("a run of the person's found going when the page opened shows its report there when it ends", async () => {
  stubViewport('desktop')
  server.use(
    graphql.query('MyImportRun', () => HttpResponse.json({ data: { myImportRun: run('RUNNING') } })),
    graphql.query('ImportRun', () => HttpResponse.json({ data: { importRun: run('DONE', { report: [reportCar()] }) } })),
  )
  renderWithApollo(<ImportPage />)
  expect(await screen.findByText('Importing… car 1 of 2 · 310 of 470 entries')).toBeInTheDocument()

  // The answer about the run also reaches the cached MyImportRun (the same object by its id): the page must keep the run it found.
  expect(await screen.findByText('Import finished: 1 car.', undefined, { timeout: 5000 })).toBeInTheDocument()
  await new Promise((resolve) => setTimeout(resolve, 100))
  expect(screen.getByText('Import finished: 1 car.')).toBeInTheDocument()
  expect(screen.queryByRole('combobox', { name: 'Source' })).not.toBeInTheDocument()
}, 15_000)

it('the report opens by its link from the notification, and a report that is no longer kept is said so', async () => {
  stubViewport('desktop')
  server.use(graphql.query('ImportRun', ({ variables }) => HttpResponse.json({ data: { importRun: variables.id === 'run1' ? run('DONE', { report: [reportCar()] }) : null } })))
  const kept = renderWithApollo(<ImportPage />, '/import?run=run1')
  expect(await screen.findByText('Import finished: 1 car.')).toBeInTheDocument()
  kept.unmount()

  renderWithApollo(<ImportPage />, '/import?run=gone')
  expect(await screen.findByRole('alert')).toHaveTextContent("This import's report is no longer kept.")
})
