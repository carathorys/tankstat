import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import { RestoreCarsPage } from '../../src/admin/RestoreCars.tsx'
import { ReplaceConfirmations } from '../../src/pages/import/BackupKinds.tsx'
import { RestoreMode } from '../../src/pages/import/importMode.ts'
import { check } from '../support/accessibility.tsx'
import { graphql } from '../support/graphql.ts'
import { renderWithApollo, sessionHandler, stubViewport, user } from '../support/mocks.tsx'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const file = (state: string, over: Record<string, unknown> = {}) => ({
  __typename: 'BackupImportInfo', token: 't1', state, triesLeft: null, wrongKey: false, refusal: null,
  file: state === 'OPENED' ? { __typename: 'BackupFileInfo', createdAt: '2026-11-03T08:15:00Z', version: '0.21.0', isExport: false, trashedCars: 0, cars: [] } : null,
  ...over,
})
const car = (id: string, name: string, over: Record<string, unknown> = {}) => ({
  __typename: 'BackupCarInfo', id, name, licensePlate: null, ownerId: 'anna', ownerName: 'Anna', ownerEmail: 'anna@example.com', distanceUnit: 'KILOMETERS', volumeUnit: 'LITERS',
  refuelings: 12, expenses: 3, schedules: 1, ...over,
})
const offer = (state: string, c: ReturnType<typeof car>, over: Record<string, unknown> = {}) => ({
  __typename: 'RestoreCarOfferInfo', state, ownerIdHere: null, ownerNameHere: null, missingRefuelings: 0, missingExpenses: 0, missingSchedules: 0, missingTyreSets: 0,
  grants: 0, goneGrantees: [], sameEmailUserId: null, car: c, ...over,
})
const OFFERS = [
  offer('HERE', car('golf', 'Golf', { licensePlate: 'ABC-123' }), { ownerIdHere: 'anna', ownerNameHere: 'Anna', missingRefuelings: 2, missingTyreSets: 1, grants: 1 }),
  offer('GONE', car('fiat', 'Fiat'), { grants: 2 }),
  offer('OWNER_GONE', car('polo', 'Polo', { ownerId: 'dave', ownerName: 'Dave', ownerEmail: 'dave@example.com' }), { sameEmailUserId: 'dave2' }),
  offer('IN_TRASH', car('seat', 'Seat')),
]
const kindOf = (kind: string) => ({
  __typename: 'ImportKindReviewInfo', kind, mode: 'ONLY_MISSING', backup: 0, new: 0, differ: 0, same: 0, inTrash: 0, adds: 0, takes: 0, targetLive: 0, targetTrashed: 0, withPhotos: 0, onTyreLines: 0, photosLost: 0,
})
const review = (carId: string, name: string, restore: Record<string, unknown>, over: Record<string, unknown> = {}) => ({
  __typename: 'ImportCarReviewInfo', carId, name, ownerName: 'Anna', ownedByMe: false, targetVehicleId: null, targetName: null,
  newRefuelings: 2, newExpenses: 0, newSchedules: 0, existingRefuelings: 0, existingExpenses: 0, existingSchedules: 0, differing: 0, inTrash: 0,
  links: true, linksLost: 0, kinds: [kindOf('refueling'), kindOf('expense'), kindOf('recurring')], tyres: { __typename: 'ImportTyreReviewInfo', sets: [], noSetLines: 0, noSetExpenses: 0, expenses: 0 }, media: null,
  restore: { __typename: 'RestoreCarReviewInfo', access: [], goneAccess: [], ...restore },
  ...over,
})
const run = (state: string, over: Record<string, unknown> = {}) => ({
  __typename: 'ImportRunInfo', id: 'run1', kind: 'RESTORE', backupOf: '2026-11-03T08:15:00Z', startedByName: 'Ada', mine: true, state, failure: null, queuedAt: '2026-11-16T10:00:00Z', startedAt: null, ahead: 0,
  fileToken: 't1', limitMinutes: 10, progress: { __typename: 'ImportRunProgressInfo', car: 1, cars: 1, done: 0, total: 3 },
  cars: [{ __typename: 'ImportRunCarInfo', carId: 'polo', name: 'Polo', targetName: null }], report: null, ...over,
})

function setup(opts: { opened?: unknown; keyed?: (input: Record<string, unknown>) => unknown; runs?: unknown[]; total?: number; route?: string } = {}) {
  stubViewport('desktop')
  const calls = { cars: [] as Record<string, unknown>[], reviews: [] as unknown[], starts: [] as unknown[], keys: [] as Record<string, unknown>[] }
  server.use(
    sessionHandler('STANDALONE', () => user({ id: 'ada', displayName: 'Ada', isAdmin: true })),
    graphql.query('MyRestoreRun', () => HttpResponse.json({ data: { myRestoreRun: null } })),
    graphql.mutation('OpenCarRestore', () => HttpResponse.json({ data: { openCarRestore: opts.opened ?? file('OPENED') } })),
    graphql.mutation('SubmitCarRestoreKey', ({ variables }) => {
      calls.keys.push(variables.input as Record<string, unknown>)
      return HttpResponse.json({ data: { submitCarRestoreKey: (opts.keyed ?? (() => file('OPENED')))(variables.input as Record<string, unknown>) } })
    }),
    graphql.mutation('EndCarRestore', () => HttpResponse.json({ data: { endCarRestore: true } })),
    graphql.query('RestoreCars', ({ variables }) => {
      calls.cars.push(variables)
      const search = String(variables.search ?? '').toLowerCase()
      const cars = OFFERS.filter((o) => !search || o.car.name.toLowerCase().includes(search))
      return HttpResponse.json({ data: { restoreCars: { __typename: 'RestoreCarPageInfo', total: opts.total ?? cars.length, cars, owners: [{ __typename: 'RestoreOwnerInfo', id: 'anna', name: 'Anna' }, { __typename: 'RestoreOwnerInfo', id: 'dave', name: 'Dave' }] } } })
    }),
    graphql.query('RestorePeople', () =>
      HttpResponse.json({
        data: {
          restorePeople: [
            { __typename: 'RestorePersonInfo', id: 'ada', name: 'Ada', email: 'ada@example.com' },
            { __typename: 'RestorePersonInfo', id: 'bob', name: 'Bob', email: 'bob@example.com' },
            { __typename: 'RestorePersonInfo', id: 'dave2', name: 'Dave New', email: 'dave@example.com' },
          ],
        },
      }),
    ),
    graphql.query('RestoreReview', ({ variables }) => {
      const cars = variables.cars as { carId: string; ownerId: string | null }[]
      calls.reviews.push(cars)
      return HttpResponse.json({
        data: {
          restoreReview: cars.map((c) =>
            c.carId === 'golf'
              ? review('golf', 'Golf', { state: 'HERE', ownerId: 'anna', ownerName: 'Anna' }, { differing: 1 })
              : review('polo', 'Polo', { state: 'OWNER_GONE', ownerId: c.ownerId ?? 'ada', ownerName: c.ownerId === 'dave2' ? 'Dave New' : 'Ada', access: ['Bob'], goneAccess: ['Carol'] }),
          ),
        },
      })
    }),
    graphql.mutation('StartCarRestore', ({ variables }) => {
      calls.starts.push(variables.input)
      return HttpResponse.json({ data: { startCarRestore: run('RUNNING') } })
    }),
    graphql.query('ImportRun', () =>
      opts.runs ? HttpResponse.json({ data: { importRun: opts.runs.length > 1 ? opts.runs.shift() : opts.runs[0] } }) : HttpResponse.json({
        data: {
          importRun: run('DONE', {
            report: [
              {
                __typename: 'ImportCarReportInfo', carId: 'polo', name: 'Polo', vehicleId: 'polo', targetName: null, asNew: true, leftOut: false, refuelings: 2, expenses: 0, schedules: 0,
                existing: 0, differing: 0, inTrash: 0, refusedMore: 0, refused: [], replacing: false, links: 0, kinds: [], tyres: null, media: null,
                restore: { __typename: 'ImportRestoreReportInfo', ownerId: 'dave2', ownerName: 'Dave New', cameBack: true, grants: 1, goneGrantees: ['Carol'], peopleLeftOut: 2 },
              },
            ],
          }),
        },
      }),
    ),
  )
  const view = renderWithApollo(<RestoreCarsPage pollMs={5} openingPollMs={5} />, opts.route ?? '/admin/backups/restore?file=tankstat-backup-20261103T081500Z.zip')
  return { calls, view, ui: userEvent.setup() }
}

it('a plain restore point opens straight on its cars, each with its state in words, the one in the trash not pickable', async () => {
  const { view } = setup()

  expect(await screen.findByRole('heading', { level: 1, name: /^Restore cars from the backup of .*2026/ })).toBeInTheDocument()
  const golf = await screen.findByRole('region', { name: "Golf · ABC-123 · Anna's" })
  expect(within(golf).getByText(/Still here\./)).toBeInTheDocument()
  expect(golf).toHaveTextContent('Only what is missing is added: 2 refuellings and 1 tyre set. Its sharing stays as it is today.')
  expect(screen.getByRole('region', { name: "Fiat · Anna's" })).toHaveTextContent('Not here any more. It comes back with its entries, owner and sharing (shared with 2 people).')
  const seat = screen.getByRole('region', { name: "Seat · Anna's" })
  expect(seat).toHaveTextContent('In the trash here: restore it from the trash or empty the trash first.')
  expect(within(seat).getByRole('checkbox')).toBeDisabled()
  expect(screen.getByText('4 cars · 0 picked')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled()
  await check(view.container)
})

it("a car whose owner has no account asks who gets it: the administrator chosen, the account with the same e-mail first among the others", async () => {
  const { ui } = setup()
  const polo = await screen.findByRole('region', { name: "Polo · Dave's" })

  expect(polo).toHaveTextContent('Its owner, Dave, no longer has an account.')
  const picker = within(polo).getByRole('radiogroup', { name: 'Who gets this car?' })
  await waitFor(() => expect(within(picker).getAllByRole('radio')).toHaveLength(3))
  const names = within(picker).getAllByRole('radio').map((r) => r.closest('label')?.textContent)
  expect(names).toEqual(['You (Ada)', 'Dave New · dave@example.com · same e-mail', 'Bob · bob@example.com'])
  expect(within(picker).getByRole('radio', { name: 'You (Ada)' })).toBeChecked()

  await ui.type(screen.getByRole('searchbox', { name: 'Search by name or plate' }), 'pol')
  await waitFor(() => expect(screen.queryByRole('region', { name: /^Golf/ })).not.toBeInTheDocument())
  expect(screen.getByText('1 car · 0 picked')).toBeInTheDocument()
})

it('the review says what comes back and to whom, who is told, and the start sends the picked owner; the report says it is theirs again', async () => {
  const { ui, calls } = setup()
  const polo = await screen.findByRole('region', { name: "Polo · Dave's" })
  await ui.click(within(polo).getByRole('checkbox', { name: "Polo · Dave's" }))
  await ui.click(await within(polo).findByRole('radio', { name: 'Dave New · dave@example.com · same e-mail' }))
  await ui.click(within(screen.getByRole('region', { name: "Golf · ABC-123 · Anna's" })).getByRole('checkbox'))
  expect(screen.getByText('4 cars · 2 picked')).toBeInTheDocument()
  await ui.click(screen.getByRole('button', { name: 'Review' }))

  const golf = await screen.findByRole('region', { name: 'Golf' })
  expect(golf).toHaveTextContent('2 refuellings are missing and come back, under their own ids, logged by who logged them.')
  expect(golf).toHaveTextContent("It stays Anna's.")
  expect(golf).toHaveTextContent('Its sharing stays as it is today.')
  const reviewed = screen.getByRole('region', { name: 'Polo' })
  expect(reviewed).toHaveTextContent('It comes back whole to Dave New: 2 refuellings, under their own ids, logged by who logged them.')
  expect(reviewed).toHaveTextContent('Access comes back for Bob.')
  expect(reviewed).toHaveTextContent('Carol had access too but no longer has an account.')
  expect(screen.getByText('Entries today\'s rules refuse are left out and listed in the report.')).toBeInTheDocument()
  expect(screen.getByText(/^(Anna and Dave New|Dave New and Anna) and everyone their cars are shared with are told afterwards\.$/)).toBeInTheDocument()

  await ui.click(screen.getByRole('button', { name: 'Restore 2 cars' }))
  await waitFor(() => expect(calls.starts).toHaveLength(1))
  const sent = (calls.starts[0] as { cars: { carId: string; ownerId: string | null; targetVehicleId: string | null }[] }).cars
  expect(sent.find((c) => c.carId === 'polo')).toMatchObject({ ownerId: 'dave2', targetVehicleId: null })
  expect(sent.find((c) => c.carId === 'golf')).toMatchObject({ ownerId: null })

  expect(await screen.findByText("It is Dave New's again.")).toBeInTheDocument()
  expect(screen.getByText('Restore finished: 1 car.')).toBeInTheDocument()
  expect(screen.getByText('Access came back for 1 person.')).toBeInTheDocument()
  expect(screen.getByText('2 private charts or own dashboards stayed out: their people have no account here or cannot see the car.')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Restore other cars' })).toBeInTheDocument()
})

it('an encrypted restore point takes its passphrase or its identity, as many tries as it takes', async () => {
  let tries = 0
  const { ui, calls } = setup({ opened: file('NEEDS_KEY'), keyed: () => (++tries === 1 ? file('NEEDS_KEY', { wrongKey: true }) : file('OPENED')) })

  expect(await screen.findByText('This backup is encrypted: give its passphrase or its identity.')).toBeInTheDocument()
  await ui.type(screen.getByLabelText('Passphrase', { selector: 'input[name="secret"]' }), 'wrong one')
  await ui.click(screen.getByRole('button', { name: 'Open the backup' }))
  expect(await screen.findByText(/does not open this backup/)).toBeInTheDocument()

  await ui.click(screen.getByRole('radio', { name: 'Identity' }))
  await ui.type(screen.getByLabelText('Identity', { selector: 'input[name="secret"]' }), 'AGE-SECRET-KEY-1ZZZ')
  await ui.click(screen.getByRole('button', { name: 'Open the backup' }))
  expect(await screen.findByRole('region', { name: "Golf · ABC-123 · Anna's" })).toBeInTheDocument()
  expect(calls.keys).toEqual([
    { token: 't1', passphrase: 'wrong one', identity: null },
    { token: 't1', passphrase: null, identity: 'AGE-SECRET-KEY-1ZZZ' },
  ])
})

it("a Replace all on another person's car names its owner in the typed confirmation", async () => {
  renderWithApollo(
    <RestoreMode.Provider value>
      <ReplaceConfirmations
        cars={[{ carId: 'golf', target: 'Golf', owner: 'Anna', deleted: [{ kind: 'refueling', count: 41 }], trashed: 0, linksLost: 0, photosLost: 0 }]}
        onCancel={() => undefined}
        onConfirmed={() => undefined}
      />
    </RestoreMode.Provider>,
  )

  const dialog = await screen.findByRole('alertdialog', { name: "Delete and replace in Anna's Golf?" })
  expect(dialog).toHaveTextContent("Golf belongs to Anna. Its current 41 refuellings are deleted for good and replaced by the backup's.")
  expect(dialog).toHaveTextContent('This cannot be undone, and they will not be in the trash. Anna and everyone the car is shared with are told.')
  expect(within(dialog).getByRole('button', { name: 'Delete and restore' })).toBeDisabled()
})

it("Choose what to restore, on a car still here, compares it by the restore's own review and words its choices for a restore", async () => {
  const { ui, calls } = setup()
  const golf = await screen.findByRole('region', { name: "Golf · ABC-123 · Anna's" })
  await ui.click(within(golf).getByRole('checkbox'))
  await ui.click(within(golf).getByRole('button', { name: 'Choose what to restore' }))

  expect((await within(golf).findAllByText("Don't restore")).length).toBeGreaterThan(0)
  expect(calls.reviews.some((cars) => (cars as { carId: string }[]).some((c) => c.carId === 'golf'))).toBe(true)
})

it('the search, the owner filter and the pages ask the server, a page at a time', async () => {
  const { ui, calls } = setup({ total: 25 })
  await screen.findByRole('region', { name: "Golf · ABC-123 · Anna's" })
  expect(screen.getByText('Page 1 of 3')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Previous page' })).toBeDisabled()

  await ui.click(screen.getByRole('button', { name: 'Next page' }))
  await waitFor(() => expect(calls.cars.at(-1)).toMatchObject({ skip: 10, take: 10 }))
  expect(await screen.findByText('Page 2 of 3')).toBeInTheDocument()

  await ui.click(screen.getByRole('combobox', { name: 'Owner' }))
  await ui.click(await screen.findByRole('option', { name: 'Dave' }))
  await waitFor(() => expect(calls.cars.at(-1)).toMatchObject({ ownerId: 'dave', skip: 0 }))
})

it('a restore point the server refuses says why, and nothing else is offered', async () => {
  setup({ opened: file('ENDED', { refusal: { __typename: 'BackupImportRefusalInfo', key: 'backup.corrupt', args: {} } }) })
  expect(await screen.findByRole('alert')).toHaveTextContent(/damaged|corrupt/i)
  expect(screen.queryByRole('button', { name: 'Review' })).not.toBeInTheDocument()
})

it("a run opened from its notification shows its report; another restore goes back to the Backups page's files", async () => {
  const { ui } = setup({ route: '/admin/backups/restore?run=run1' })
  expect(await screen.findByText("It is Dave New's again.")).toBeInTheDocument()
  expect(screen.getByRole('heading', { level: 1, name: /^Restore cars from the backup of .*2026/ })).toBeInTheDocument()

  await ui.click(screen.getByRole('button', { name: 'Restore other cars' }))
  expect(await screen.findByText('Choose a backup on the Backups page first.')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Go to Backups' })).toHaveAttribute('href', '/admin/backups')
})

it('a restore cancelled while it goes leaves the choices: back to the review, the same cars', async () => {
  const { ui } = setup({ runs: [run('NOT_FINISHED', { failure: 'CANCELLED' })] })
  const polo = await screen.findByRole('region', { name: "Polo · Dave's" })
  await ui.click(within(polo).getByRole('checkbox', { name: "Polo · Dave's" }))
  await ui.click(screen.getByRole('button', { name: 'Review' }))
  await ui.click(await screen.findByRole('button', { name: 'Restore 1 car' }))

  expect(await screen.findByText(/The restore was cancelled\. Nothing was restored: the cars are as they were\./)).toBeInTheDocument()
  await ui.click(screen.getByRole('button', { name: 'Back to the review' }))
  expect(await screen.findByRole('region', { name: 'Polo' })).toBeInTheDocument()
})
