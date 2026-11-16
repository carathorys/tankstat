import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import { BackupFileStep } from '../../src/pages/import/BackupFileStep.tsx'
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

const cars = (n: number) => Array.from({ length: n }, (_, i) => ({ __typename: 'BackupCarInfo', id: `c${i}`, name: `Car ${i}`, licensePlate: null, ownerName: 'Anna', refuelings: 12, expenses: 3, schedules: 1 }))
const status = (state: string, over: Record<string, unknown> = {}) => ({ __typename: 'BackupImportInfo', token: 't1', state, triesLeft: null, wrongKey: false, refusal: null, file: null, ...over })
const opened = (n = 2) =>
  status('OPENED', { file: { __typename: 'BackupFileInfo', createdAt: '2026-11-03T08:15:00Z', version: '0.21.0', isExport: false, trashedCars: 0, cars: cars(n) } })

/**
 * The server's side of the step: the upload's answer, then what the file's status says each time it is asked (the last one again once
 * they run out), and what each passphrase tried is answered.
 */
function setup(opts: { upload?: { status: number; body: unknown }; statuses?: unknown[]; tries?: unknown[] } = {}) {
  stubViewport('desktop')
  const calls = { uploads: 0, asked: 0, passphrases: [] as string[], ended: [] as string[], onOpened: vi.fn() }
  const statuses = opts.statuses ?? [opened()]
  server.use(
    http.post('/imports/tankstat', () => {
      calls.uploads++
      return HttpResponse.json(opts.upload?.body ?? { token: 't1', format: 'tankstat' }, { status: opts.upload?.status ?? 200 })
    }),
    graphql.query('BackupImport', () => HttpResponse.json({ data: { backupImport: statuses[Math.min(calls.asked++, statuses.length - 1)] } })),
    graphql.mutation('OpenBackupImport', ({ variables }) => {
      calls.passphrases.push((variables.input as { passphrase: string }).passphrase)
      return HttpResponse.json({ data: { openBackupImport: (opts.tries ?? [])[calls.passphrases.length - 1] } })
    }),
    graphql.mutation('EndBackupImport', ({ variables }) => {
      calls.ended.push(variables.token as string)
      return HttpResponse.json({ data: { endBackupImport: true } })
    }),
  )
  const view = renderWithApollo(<BackupFileStep onOpened={calls.onOpened} pollMs={5} />)
  return { calls, view, ui: userEvent.setup() }
}

async function choose(ui: ReturnType<typeof userEvent.setup>, container: HTMLElement) {
  await ui.upload(container.querySelector<HTMLInputElement>('input[type="file"]')!, new File(['zip'], 'tankstat-backup.zip', { type: 'application/zip' }))
  expect(screen.getByText('Selected file: tankstat-backup.zip')).toBeInTheDocument()
}

it('a plain backup is read, asked about while the server opens it, and says what it holds; Next hands it on', async () => {
  const { calls, view, ui } = setup({ statuses: [status('OPENING'), status('OPENING'), opened(2)] })
  expect(screen.getByText('A backup file of this app, or an export made on its Import and export page (.zip).')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Read the file' })).toBeDisabled()

  await choose(ui, view.container)
  await ui.click(screen.getByRole('button', { name: 'Read the file' }))

  expect(await screen.findByText(/^A backup of .*2026.*, written by Tankstat 0\.21\.0: 2 cars\.$/)).toBeInTheDocument()
  expect(calls.asked).toBe(3)
  expect(screen.queryByText('Reading the file…')).not.toBeInTheDocument()

  await ui.click(screen.getByRole('button', { name: 'Next' }))
  expect(calls.onOpened).toHaveBeenCalledWith('t1', expect.objectContaining({ version: '0.21.0' }))
  view.unmount()
  expect(calls.ended).toEqual([]) // handed on: the next step has it now
})

it('one car is one car', async () => {
  const { view, ui } = setup({ statuses: [opened(1)] })
  await choose(ui, view.container)
  await ui.click(screen.getByRole('button', { name: 'Read the file' }))
  expect(await screen.findByText(/: 1 car\.$/)).toBeInTheDocument()
})

it('an encrypted backup asks for its passphrase; a wrong one is said at the field with the tries left, the right one opens it', async () => {
  const { calls, view, ui } = setup({
    statuses: [status('NEEDS_KEY', { triesLeft: 3 })],
    tries: [status('NEEDS_KEY', { triesLeft: 2, wrongKey: true }), status('OPENING')],
  })
  await choose(ui, view.container)
  await ui.click(screen.getByRole('button', { name: 'Read the file' }))

  expect(await screen.findByText('This file is encrypted. Its passphrase is used for this import only and kept nowhere.')).toBeInTheDocument()
  const field = screen.getByLabelText(/^Passphrase/)
  expect(field).toHaveAttribute('type', 'password')
  expect(field).toHaveAttribute('autocomplete', 'off')
  expect(field).toHaveAccessibleDescription(expect.stringContaining("The passphrase the backup's key was made from."))
  await waitFor(() => expect(field).toHaveFocus())
  await check(view.container)

  await ui.type(field, 'wrong one')
  await ui.click(screen.getByRole('button', { name: 'Open the file' }))
  await waitFor(() => expect(screen.getByLabelText(/^Passphrase/)).toHaveAccessibleDescription(expect.stringContaining('The passphrase (or identity) does not open this backup. 2 tries left.')))
  await waitFor(() => expect(screen.getByLabelText(/^Passphrase/)).toHaveFocus())

  // The second answer is OPENING, then the status query says it is open.
  calls.asked = 0
  server.use(graphql.query('BackupImport', () => HttpResponse.json({ data: { backupImport: opened(3) } })))
  await ui.clear(screen.getByLabelText(/^Passphrase/))
  await ui.type(screen.getByLabelText(/^Passphrase/), 'right one')
  await ui.click(screen.getByRole('button', { name: 'Open the file' }))

  expect(await screen.findByText(/: 3 cars\.$/)).toBeInTheDocument()
  expect(calls.passphrases).toEqual(['wrong one', 'right one'])
  expect(screen.queryByLabelText(/^Passphrase/)).not.toBeInTheDocument()
})

it('after the last wrong try the file is gone: an alert says so and the file field is empty again', async () => {
  const { view, ui } = setup({
    statuses: [status('NEEDS_KEY', { triesLeft: 1, wrongKey: true })],
    tries: [status('ENDED', { refusal: { __typename: 'BackupImportRefusalInfo', key: 'import.noTriesLeft', args: {} } })],
  })
  await choose(ui, view.container)
  await ui.click(screen.getByRole('button', { name: 'Read the file' }))
  expect(await screen.findByLabelText(/^Passphrase/)).toHaveAccessibleDescription(expect.stringContaining('1 try left.'))

  await ui.type(screen.getByLabelText(/^Passphrase/), 'wrong again')
  await ui.click(screen.getByRole('button', { name: 'Open the file' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('Three wrong tries: the file was removed from the server. Choose it again to start over.')
  expect(screen.getByText('No file chosen')).toBeInTheDocument()
  expect(screen.queryByLabelText(/^Passphrase/)).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Read the file' })).toBeDisabled()
})

it('a file that is no backup is refused with the reason, and the file field stays to choose another', async () => {
  const { view, ui } = setup({ upload: { status: 400, body: { key: 'import.notABackup', args: {}, message: 'No.' } } })
  await choose(ui, view.container)
  await ui.click(screen.getByRole('button', { name: 'Read the file' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('This file is not a Tankstat backup or export.')
  expect(screen.getByText('Selected file: tankstat-backup.zip')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Read the file' })).toBeEnabled()
})

it('a file that breaks while the server opens it ends the same way', async () => {
  const { view, ui } = setup({ statuses: [status('OPENING'), status('ENDED', { refusal: { __typename: 'BackupImportRefusalInfo', key: 'import.notABackup', args: {} } })] })
  await choose(ui, view.container)
  await ui.click(screen.getByRole('button', { name: 'Read the file' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('This file is not a Tankstat backup or export.')
})

it('a file past the limit says the limit and that a larger one needs an administrator; one of a newer release says its version', async () => {
  const large = setup({ upload: { status: 413, body: { key: 'import.tooLarge', args: { maxMb: 200 }, message: 'Too large.' } } })
  await choose(large.ui, large.view.container)
  await large.ui.click(screen.getByRole('button', { name: 'Read the file' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('The file can be at most 200 MB. A larger one needs an administrator.')
  large.view.unmount()

  const newer = setup({ upload: { status: 400, body: { key: 'backup.newerFormat', args: { appVersion: '0.30.0' }, message: 'Newer.' } } })
  await choose(newer.ui, newer.view.container)
  await newer.ui.click(screen.getByRole('button', { name: 'Read the file' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('The backup was written by a newer release (Tankstat 0.30.0); upgrade first.')
})

it('leaving the step with a file still on the server gives it up there', async () => {
  const { calls, view, ui } = setup({ statuses: [status('NEEDS_KEY', { triesLeft: 3 })] })
  await choose(ui, view.container)
  await ui.click(screen.getByRole('button', { name: 'Read the file' }))
  await screen.findByLabelText(/^Passphrase/)

  view.unmount()

  await waitFor(() => expect(calls.ended).toEqual(['t1']))
})
