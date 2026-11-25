import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import { navigation } from '../../src/navigation.ts'
import { ImportPage } from '../../src/pages/ImportPage.tsx'
import { ExportCarButton } from '../../src/pages/vehicle/ExportCarDialog.tsx'
import { check } from '../support/accessibility.tsx'
import { graphql } from '../support/graphql.ts'
import { gqlError, renderWithApollo, stubViewport } from '../support/mocks.tsx'
import { answered, server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.restoreAllMocks()
})
afterAll(() => server.close())

const MB = 1_000_000
const offer = (id: string, name: string, over: Record<string, unknown> = {}) => ({
  __typename: 'ExportCarOfferInfo', id, name, licensePlate: null, refuelings: 412, expenses: 57, schedules: 3, tyreSets: 2, charts: 1, photos: 38, photoBytes: 61 * MB, pictureBytes: 0.4 * MB, rowBytes: 0.2 * MB, ...over,
})
const file = (state: string, over: Record<string, unknown> = {}) => ({
  __typename: 'ExportInfo', id: 'e1', state, carNames: ['Golf'], encrypted: true, startedAt: '2026-11-17T10:00:00Z', readyAt: null, keptUntil: null, deletedAt: null, bytes: null,
  failureKey: null, largerThanImport: false, importMaxMb: 200, downloadUrl: null, progress: null, kept: null, ...over,
})
const ready = (over: Record<string, unknown> = {}) =>
  file('READY', { readyAt: '2026-11-17T10:01:00Z', keptUntil: '2026-11-18T10:01:00Z', bytes: 62 * MB, downloadUrl: '/media/exports/e1', ...over })

/** The server's side of the Export tab, each start and delete recorded; `mine` answers myExport in turn (the last one stays). */
function setup(opts: { cars?: unknown[]; mine?: unknown[]; can?: boolean; startError?: unknown; keptOnStart?: unknown } = {}) {
  stubViewport('desktop')
  const calls = { starts: [] as Record<string, unknown>[], deleted: 0, cancelled: 0, heads: 0 }
  const answers = [...(opts.mine ?? [null])]
  server.use(
    graphql.query('CanExport', () => HttpResponse.json({ data: { canExport: opts.can ?? true } })),
    graphql.query('ExportCars', () => HttpResponse.json({ data: { exportCars: opts.cars ?? [offer('v1', 'Golf', { licensePlate: 'ABC-123' }), offer('v2', 'Polo', { photos: 0, photoBytes: 0, pictureBytes: 0 })], exportPassphraseMinLength: 12 } })),
    graphql.query('MyExport', () => HttpResponse.json({ data: { myExport: answers.length > 1 ? answers.shift() : answers[0] } })),
    graphql.mutation('StartExport', ({ variables }) => {
      if (opts.startError) return HttpResponse.json(opts.startError)
      calls.starts.push(variables.input as Record<string, unknown>)
      answers.splice(0, answers.length, file('BUILDING', { id: 'e2', progress: { __typename: 'ExportProgressInfo', car: 1, cars: 1, done: 21, total: 38 }, kept: opts.keptOnStart ?? null }))
      return HttpResponse.json({ data: { startExport: answers[0] } })
    }),
    graphql.mutation('CancelExport', () => {
      calls.cancelled++
      answers.splice(0, answers.length, file('CANCELLED'))
      return HttpResponse.json({ data: { cancelExport: true } })
    }),
    graphql.mutation('DeleteExport', () => {
      calls.deleted++
      answers.splice(0, answers.length, null)
      return HttpResponse.json({ data: { deleteExport: true } })
    }),
    http.head('/media/exports/:id', ({ params }) => {
      calls.heads++
      return params.id === 'e1' ? new HttpResponse(null, { status: 200 }) : new HttpResponse(null, { status: 404 })
    }),
  )
  return calls
}

it('is a tab of the Import and export page, and the page stays Import while exporting is off for the person', async () => {
  setup({ can: false })
  const { unmount } = renderWithApollo(<ImportPage />, '/import?tab=export')
  expect(await screen.findByRole('heading', { level: 1, name: 'Import' })).toBeInTheDocument()
  expect(screen.queryByRole('tab')).not.toBeInTheDocument()
  unmount()

  setup()
  renderWithApollo(<ImportPage />, '/import?tab=export')
  expect(await screen.findByRole('heading', { level: 1, name: 'Import and export' })).toBeInTheDocument()
  expect(await screen.findByRole('tab', { name: 'Export', selected: true })).toBeInTheDocument()
  expect(await screen.findByText(/Export your own cars as a file/)).toBeInTheDocument()
  await userEvent.click(screen.getByRole('tab', { name: 'Import' }))
  expect(await screen.findByText('Source')).toBeInTheDocument()
  await answered() // the Import tab's own questions: one answered after this test's handlers are gone would leave the next test offline
})

it("a report's link opens the Import tab whatever the tab says", async () => {
  setup()
  server.use(graphql.query('ImportRun', () => HttpResponse.json({ data: { importRun: null } })))
  renderWithApollo(<ImportPage />, '/import?tab=export&run=run1')
  expect(await screen.findByRole('tab', { name: 'Import', selected: true })).toBeInTheDocument()
  await answered() // its ImportRun, still under way: unanswered once the handlers are reset, it would leave the next test offline (#380's CI)
})

it('exports the ticked car with its kinds and a passphrase typed twice, then follows the build', async () => {
  const calls = setup()
  renderWithApollo(<ImportPage />, '/import?tab=export')
  const golf = await screen.findByRole('checkbox', { name: 'Golf (ABC-123)' })
  expect(screen.getByText('412 refuellings · 57 expenses · 3 recurring expenses · 2 tyre sets · 38 photos')).toBeInTheDocument()
  // Nothing ticked: the reason is said and Export is off.
  expect(screen.getByText('Tick at least one car.')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Export' })).toBeDisabled()

  await userEvent.click(golf)
  expect(screen.getByText('Everything of this car is exported.')).toBeInTheDocument()
  expect(screen.getByText('1 car · about 61.6 MB')).toBeInTheDocument()
  const disclosure = screen.getByRole('button', { name: 'Choose what to export' })
  expect(disclosure).toHaveAttribute('aria-expanded', 'false')
  await userEvent.click(disclosure)
  expect(disclosure).toHaveAttribute('aria-expanded', 'true')
  const kinds = screen.getByRole('group', { name: 'Choose what to export' })
  expect(within(kinds).getByRole('checkbox', { name: 'Photos of entries · 38 photos · adds 61 MB' })).toBeChecked()
  await userEvent.click(within(kinds).getByRole('checkbox', { name: /Photos of entries/ }))
  expect(screen.getByText('1 car · about 600 kB')).toBeInTheDocument()

  // Every kind left out: the car takes nothing.
  for (const box of within(kinds).getAllByRole('checkbox', { checked: true })) await userEvent.click(box)
  expect(screen.getByText('Tick at least one kind for Golf.')).toBeInTheDocument()
  await userEvent.click(within(kinds).getByRole('checkbox', { name: /Refuellings/ }))

  await userEvent.type(screen.getByLabelText('Passphrase (optional)', { selector: 'input' }), 'a long enough passphrase')
  await userEvent.type(screen.getByLabelText('Repeat the passphrase', { selector: 'input' }), 'a long enough passphrase')
  await userEvent.click(screen.getByRole('button', { name: 'Export' }))

  await waitFor(() => expect(calls.starts).toHaveLength(1))
  expect(calls.starts[0]).toEqual({
    cars: [{ vehicleId: 'v1', refuelings: true, expenses: false, schedules: false, tyres: false, charts: false, photos: false, picture: false }],
    passphrase: 'a long enough passphrase',
    passphraseConfirmation: 'a long enough passphrase',
  })
  expect(await screen.findByText('Building your export… car 1 of 1 · 21 of 38 photos')).toHaveAttribute('role', 'status')
  expect(screen.queryByRole('button', { name: 'Export' })).not.toBeInTheDocument()

  // Cancelled: nothing kept, and the form is back.
  await userEvent.click(screen.getByRole('button', { name: 'Cancel the export' }))
  await userEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Cancel the export' }))
  expect(await screen.findByText('The export was cancelled.')).toBeInTheDocument()
  expect(calls.cancelled).toBe(1)
  expect(await screen.findByRole('button', { name: 'Export' })).toBeInTheDocument()
})

it('a passphrase typed differently is said at its field and nothing is sent; only spaces is a plain file', async () => {
  const calls = setup()
  renderWithApollo(<ImportPage />, '/import?tab=export')
  await userEvent.click(await screen.findByRole('checkbox', { name: 'Golf (ABC-123)' }))
  await userEvent.type(screen.getByLabelText('Passphrase (optional)', { selector: 'input' }), 'a long enough passphrase')
  await userEvent.type(screen.getByLabelText('Repeat the passphrase', { selector: 'input' }), 'another one entirely')
  await userEvent.click(screen.getByRole('button', { name: 'Export' }))
  expect(await screen.findByText('The two entries are not the same.')).toBeInTheDocument()
  expect(calls.starts).toHaveLength(0)

  await userEvent.clear(screen.getByLabelText('Passphrase (optional)', { selector: 'input' }))
  await userEvent.clear(screen.getByLabelText('Repeat the passphrase', { selector: 'input' }))
  await userEvent.click(screen.getByRole('button', { name: 'Export' }))
  await waitFor(() => expect(calls.starts).toHaveLength(1))
  expect(calls.starts[0]).toMatchObject({ passphrase: null, passphraseConfirmation: null })
})

it('a ready file is downloaded once the server says it is there, deleted after a confirmation, and replaced by Export again', async () => {
  const calls = setup({ mine: [ready({ largerThanImport: true })] })
  const download = vi.spyOn(navigation, 'download').mockImplementation(() => undefined)
  renderWithApollo(<ImportPage />, '/import?tab=export')
  expect(await screen.findByText('Your export is ready.')).toBeInTheDocument()
  expect(screen.getByText(/^1 car · 62 MB · encrypted · kept until /)).toBeInTheDocument()
  expect(screen.getByText('tankstat-export-2026-11-17.zip')).toBeInTheDocument()
  expect(screen.getByText(/larger than this server accepts as an import \(200 MB\)/)).toBeInTheDocument()
  // The form waits behind Export again, which says it replaces the file.
  expect(screen.queryByRole('checkbox', { name: 'Golf (ABC-123)' })).not.toBeInTheDocument()

  await userEvent.click(screen.getByRole('button', { name: 'Download' }))
  await waitFor(() => expect(download).toHaveBeenCalledWith('/media/exports/e1'))
  expect(calls.heads).toBe(1)

  await userEvent.click(screen.getByRole('button', { name: 'Delete it' }))
  await userEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Delete it' }))
  await waitFor(() => expect(calls.deleted).toBe(1))
  await waitFor(() => expect(screen.queryByText('Your export is ready.')).not.toBeInTheDocument())
})

it('Export again opens the form, which says it replaces the file; while the new one is built the kept file stays and nothing offers a third', async () => {
  const kept = ready()
  const calls = setup({ mine: [kept], keptOnStart: kept })
  renderWithApollo(<ImportPage />, '/import?tab=export')
  await userEvent.click(await screen.findByRole('button', { name: 'Export again' }))
  await userEvent.click(await screen.findByRole('checkbox', { name: 'Golf (ABC-123)' }))
  expect(screen.getByText(/An export of yours is kept until .+\. This one replaces it\./)).toBeInTheDocument()
  await userEvent.click(screen.getByRole('button', { name: 'Export' }))
  await waitFor(() => expect(calls.starts).toHaveLength(1))
  expect(await screen.findByText('Building your export… car 1 of 1 · 21 of 38 photos')).toBeInTheDocument()
  expect(screen.getByText('Your earlier export is still kept.')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Export again' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Export' })).not.toBeInTheDocument()
})

it('a download of a file gone since is said on the page', async () => {
  setup({ mine: [ready({ downloadUrl: '/media/exports/gone' })] })
  const download = vi.spyOn(navigation, 'download').mockImplementation(() => undefined)
  renderWithApollo(<ImportPage />, '/import?tab=export')
  await userEvent.click(await screen.findByRole('button', { name: 'Download' }))
  expect(await screen.findByText('This export is no longer kept.')).toBeInTheDocument()
  expect(download).not.toHaveBeenCalled()
})

it('says how the last export ended: failed with its reason and the earlier file still kept, cut short, expired', async () => {
  setup({ mine: [file('FAILED', { failureKey: 'export.failed', kept: ready({ id: 'e0' }) })] })
  const { unmount } = renderWithApollo(<ImportPage />, '/import?tab=export')
  expect(await screen.findByText('The export could not be built: Something went wrong while it was written. Nothing was kept.')).toBeInTheDocument()
  expect(screen.getByText('Your earlier export is still kept.')).toBeInTheDocument()
  expect(screen.queryByText('Your export is ready.')).not.toBeInTheDocument()
  unmount()

  setup({ mine: [file('INTERRUPTED')] })
  const second = renderWithApollo(<ImportPage />, '/import?tab=export')
  expect(await screen.findByText('The export did not finish: the server restarted while it was built.')).toBeInTheDocument()
  expect(await screen.findByRole('button', { name: 'Export' })).toBeInTheDocument()
  second.unmount()

  setup({ mine: [file('EXPIRED', { deletedAt: '2026-11-18T10:01:00Z' })] })
  renderWithApollo(<ImportPage />, '/import?tab=export')
  expect(await screen.findByText(/^Your export is no longer kept: it was deleted .+\.$/)).toBeInTheDocument()
})

it('with no car of their own the person is told why there is nothing to export', async () => {
  setup({ cars: [] })
  renderWithApollo(<ImportPage />, '/import?tab=export')
  expect(await screen.findByText(/You have no cars of your own to export/)).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Export' })).not.toBeInTheDocument()
})

it("Export this car exports that car alone, says it replaces the person's file, and leads to the Export tab", async () => {
  const calls = setup({ mine: [file('BUILDING')] })
  renderWithApollo(<ExportCarButton vehicleId="v2" />)
  await userEvent.click(await screen.findByRole('button', { name: 'Export this car' }))
  const dialog = await screen.findByRole('dialog', { name: 'Export Polo' })
  expect(within(dialog).queryByRole('checkbox', { name: 'Polo' })).not.toBeInTheDocument() // the car is the dialog's own
  expect(await within(dialog).findByText('An export of yours is being built. This one replaces it.')).toBeInTheDocument()
  await userEvent.click(within(dialog).getByRole('button', { name: 'Export' }))
  await waitFor(() => expect(calls.starts).toHaveLength(1))
  expect(calls.starts[0]).toMatchObject({ cars: [{ vehicleId: 'v2', refuelings: true, photos: true, picture: false }] })
  expect(await screen.findByText('The export is being built.')).toBeInTheDocument()
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
})

it("a car that is not the person's own, or exporting switched off, has no Export this car", async () => {
  setup()
  const { unmount } = renderWithApollo(<ExportCarButton vehicleId="shared" />)
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Export this car' })).not.toBeInTheDocument())
  unmount()
  setup({ can: false })
  renderWithApollo(<ExportCarButton vehicleId="v1" />)
  await new Promise((r) => setTimeout(r, 50))
  expect(screen.queryByRole('button', { name: 'Export this car' })).not.toBeInTheDocument()
})

it('a refused start is said in the form', async () => {
  setup({ startError: gqlError('Exporting is switched off on this server.', 'FORBIDDEN', 'export.disabled') })
  renderWithApollo(<ImportPage />, '/import?tab=export')
  await userEvent.click(await screen.findByRole('checkbox', { name: 'Golf (ABC-123)' }))
  await userEvent.click(screen.getByRole('button', { name: 'Export' }))
  expect(await screen.findByText('Exporting is switched off on this server.')).toBeInTheDocument()
})

it('has no accessibility violations with a car ticked and its kinds open', async () => {
  setup({ mine: [ready()] })
  const { container } = renderWithApollo(<ImportPage />, '/import?tab=export')
  await userEvent.click(await screen.findByRole('button', { name: 'Export again' }))
  await userEvent.click(await screen.findByRole('checkbox', { name: 'Golf (ABC-123)' }))
  await userEvent.click(screen.getByRole('button', { name: 'Choose what to export' }))
  await check(container)
})
