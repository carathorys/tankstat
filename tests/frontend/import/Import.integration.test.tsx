import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, http, HttpResponse } from 'msw'
import { axe } from 'vitest-axe'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { server } from '../support/server.ts'
import { fakeVehicle, healthHandler, renderWithApollo, sessionHandler, stubViewport } from '../support/mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

const preview = (over: Record<string, unknown> = {}) => ({
  fuelRows: 24,
  expenseRows: 5,
  recurringRows: 2,
  duplicateFuelRows: 0,
  duplicateExpenseRows: 0,
  duplicateRecurringRows: 0,
  firstDate: '2024-11-03',
  lastDate: '2026-09-17',
  categories: ['Maintenance', 'Service'],
  sourceVehicle: { name: 'Polo', licensePlate: 'ABC-123', fuelType: 'PETROL', distanceUnit: 'KILOMETERS', volumeUnit: 'LITERS' },
  issues: [] as { section: string; row: number; key: string; args: Record<string, unknown> }[],
  ...over,
})

function setup(opts: { vehicles?: ReturnType<typeof fakeVehicle>[]; uploadStatus?: number; uploadBody?: unknown; previewFor?: (vehicleId: string | null) => ReturnType<typeof preview>; result?: Record<string, unknown> } = {}) {
  stubViewport('desktop')
  const calls = { uploads: [] as { format: string; size: number }[], previews: [] as unknown[], confirms: [] as unknown[] }
  server.use(
    sessionHandler('NONE', () => null),
    healthHandler,
    graphql.query('ImportTargets', () => HttpResponse.json({ data: { myVehicles: opts.vehicles ?? [fakeVehicle()] } })),
    graphql.query('VehicleDefaults', () => HttpResponse.json({ data: { vehicleDefaults: { distanceUnit: 'MILES', volumeUnit: 'US_GALLONS', currency: 'USD', recurringWarnDays: 30, recurringWarnDistance: 500 } } })),
    graphql.query('ImportPreview', ({ variables }) => {
      calls.previews.push(variables)
      return HttpResponse.json({ data: { importPreview: (opts.previewFor ?? (() => preview()))(variables.vehicleId as string | null) } })
    }),
    graphql.mutation('ConfirmImport', ({ variables }) => {
      calls.confirms.push(variables.input)
      return HttpResponse.json({
        data: { confirmImport: { vehicleId: 'v9', fuelImported: 24, expensesImported: 5, recurringImported: 2, fuelSkippedDuplicates: 0, expensesSkippedDuplicates: 0, recurringSkippedDuplicates: 0, errors: [], ...opts.result } },
      })
    }),
    http.post('/imports/:format', async ({ params, request }) => {
      calls.uploads.push({ format: String(params.format), size: (await request.arrayBuffer()).byteLength })
      return HttpResponse.json(opts.uploadBody ?? { token: 'tok123', format: 'fuelio' }, { status: opts.uploadStatus ?? 200 })
    }),
  )
  renderWithApollo(<App />, '/import')
  return { calls, ui: userEvent.setup() }
}

const csv = () => new File(['"## Log"\n'], 'vehicle-4-sync.csv', { type: 'text/csv' })
const fileInput = () => document.querySelector('input[type=file]') as HTMLInputElement

const choose = async (ui: ReturnType<typeof userEvent.setup>, control: string, option: string) => {
  await ui.click(screen.getByRole('combobox', { name: control }))
  await ui.click(await screen.findByRole('option', { name: option }))
}

async function toTarget(ui: ReturnType<typeof userEvent.setup>) {
  await screen.findByRole('heading', { name: 'Import', level: 1 })
  await ui.upload(fileInput(), csv())
  expect(screen.getByText('Selected file: vehicle-4-sync.csv')).toBeInTheDocument()
  await ui.click(screen.getByRole('button', { name: 'Read the file' }))
  await screen.findByRole('heading', { name: 'Where should the data go?' })
}

it('imports a file into a new vehicle that starts from what the file says', async () => {
  const { ui, calls } = setup()
  await toTarget(ui)
  expect(calls.uploads).toEqual([{ format: 'fuelio', size: 9 }])

  await ui.click(screen.getByRole('radio', { name: 'A new vehicle' }))
  expect(screen.getByLabelText('Name')).toHaveValue('Polo')
  expect(screen.getByLabelText(/^License plate/)).toHaveValue('ABC-123')
  expect(screen.getByText(/The file uses Kilometers and Liters/)).toBeInTheDocument()
  await ui.clear(screen.getByLabelText('Currency of all amounts'))
  await ui.type(screen.getByLabelText('Currency of all amounts'), 'huf')
  await ui.click(screen.getByRole('button', { name: 'What will be imported' }))

  await screen.findByText(/24 fuel logs, 5 expenses and 2 recurring expenses, from Nov 3, 2024 to Sep 17, 2026\./)
  expect(screen.getByText('Categories in the file: Maintenance, Service')).toBeInTheDocument()
  await ui.click(screen.getByRole('button', { name: 'Import' }))

  await screen.findByRole('heading', { name: 'Import finished' })
  expect(screen.getByText('Imported 24 fuel logs, 5 expenses and 2 recurring expenses.')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Open the vehicle' })).toHaveAttribute('href', '/vehicles/v9')
  expect(calls.confirms).toEqual([
    { token: 'tok123', currency: 'HUF', importDuplicates: false, newVehicle: { name: 'Polo', licensePlate: 'ABC-123', fuelType: 'PETROL', units: { distance: 'KILOMETERS', volume: 'LITERS' } } },
  ])
})

it('uses the instance defaults for what the file does not say', async () => {
  const { ui, calls } = setup({ previewFor: () => preview({ sourceVehicle: null }) })
  await toTarget(ui)

  await ui.click(screen.getByRole('radio', { name: 'A new vehicle' }))
  await ui.type(screen.getByLabelText('Name'), 'Mine')
  expect(screen.getByLabelText('Currency of all amounts')).toHaveValue('USD')
  await ui.click(screen.getByRole('button', { name: 'What will be imported' }))
  await ui.click(await screen.findByRole('button', { name: 'Import' }))

  await screen.findByRole('heading', { name: 'Import finished' })
  expect(calls.confirms[0]).toMatchObject({ currency: 'USD', newVehicle: { name: 'Mine', units: { distance: 'MILES', volume: 'US_GALLONS' } } })
})

it('imports into an existing vehicle, and lets the user decide about rows that already exist', async () => {
  const { ui, calls } = setup({
    previewFor: (vehicleId) => (vehicleId ? preview({ duplicateFuelRows: 20, duplicateExpenseRows: 3, duplicateRecurringRows: 1 }) : preview()),
    result: { fuelImported: 4, expensesImported: 2, recurringImported: 1, fuelSkippedDuplicates: 20, expensesSkippedDuplicates: 3, recurringSkippedDuplicates: 1 },
  })
  await toTarget(ui)

  await choose(ui, 'Vehicle', 'Octavia (ABC-123)')
  await ui.click(screen.getByRole('button', { name: 'What will be imported' }))
  await screen.findByText('20 fuel logs, 3 expenses and 1 recurring expenses already exist in this vehicle.')
  expect(screen.getByRole('radio', { name: 'Skip them (recommended)' })).toBeChecked()
  await ui.click(screen.getByRole('button', { name: 'Import' }))

  await screen.findByText('Skipped 20 fuel logs, 3 expenses and 1 recurring expenses that already existed.')
  expect(calls.previews.at(-1)).toEqual({ token: 'tok123', vehicleId: 'v1' })
  expect(calls.confirms).toEqual([{ token: 'tok123', vehicleId: 'v1', currency: 'USD', importDuplicates: false }])
})

it('imports the duplicates anyway when asked to', async () => {
  const { ui, calls } = setup({ previewFor: (vehicleId) => preview(vehicleId ? { duplicateFuelRows: 1 } : {}) })
  await toTarget(ui)
  await choose(ui, 'Vehicle', 'Octavia (ABC-123)')
  await ui.click(screen.getByRole('button', { name: 'What will be imported' }))

  await ui.click(await screen.findByRole('radio', { name: 'Import them again anyway' }))
  await ui.click(screen.getByRole('button', { name: 'Import' }))

  await screen.findByRole('heading', { name: 'Import finished' })
  expect(calls.confirms[0]).toMatchObject({ importDuplicates: true })
})

it('lists the rows left out while reading, with translated reasons and where they were', async () => {
  const issues = [
    { section: 'costs', row: 3, key: 'import.templateSkipped', args: { title: 'Brake pads' } },
    { section: 'log', row: 7, key: 'import.badNumber', args: { field: 'volume', value: 'x' } },
    { section: 'vehicle', row: 0, key: 'import.unitsDiffer', args: {} },
  ]
  const { ui } = setup({ previewFor: () => preview({ issues }) })
  await toTarget(ui)
  await ui.click(screen.getByRole('radio', { name: 'A new vehicle' }))
  await ui.click(screen.getByRole('button', { name: 'What will be imported' }))

  await screen.findByText('Left out while reading the file (3)')
  expect(screen.getByText(/Costs, row 3/)).toBeInTheDocument()
  expect(screen.getByText(/“Brake pads” is a one-off reminder, which is not imported\./)).toBeInTheDocument()
  expect(screen.getByText(/The volume “x” is not a valid number\./)).toBeInTheDocument()
  expect(screen.getByText(/The file uses other units than this vehicle/)).toBeInTheDocument()
})

it('reports the rows that could not be imported after the import', async () => {
  const { ui } = setup({
    result: { fuelImported: 1, expensesImported: 0, errors: [{ section: 'log', row: 5, key: 'odometer.belowPrevious', args: { previous: '2,000 km', date: '2026-07-01' } }] },
  })
  await toTarget(ui)
  await ui.click(screen.getByRole('radio', { name: 'A new vehicle' }))
  await ui.click(screen.getByRole('button', { name: 'What will be imported' }))
  await ui.click(await screen.findByRole('button', { name: 'Import' }))

  await screen.findByText('These rows could not be imported (1)')
  expect(screen.getByText(/The odometer cannot be lower than 2,000 km, the reading on 2026-07-01\./)).toBeInTheDocument()
})

it('can start over with another file', async () => {
  const { ui } = setup()
  await toTarget(ui)
  await ui.click(screen.getByRole('radio', { name: 'A new vehicle' }))
  await ui.click(screen.getByRole('button', { name: 'What will be imported' }))
  await ui.click(await screen.findByRole('button', { name: 'Import' }))
  await ui.click(await screen.findByRole('button', { name: 'Import another file' }))

  expect(await screen.findByRole('button', { name: 'Choose file' })).toBeInTheDocument()
})

it('shows the server’s reason when the file cannot be read, and stays on the first step', async () => {
  const { ui } = setup({ uploadStatus: 400, uploadBody: { key: 'import.unreadable', args: {}, message: 'bad' } })
  await screen.findByRole('heading', { name: 'Import', level: 1 })

  await ui.upload(fileInput(), csv())
  await ui.click(screen.getByRole('button', { name: 'Read the file' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('This file does not look like an export of the chosen app.')
  expect(screen.getByRole('button', { name: 'Read the file' })).toBeInTheDocument()
})

it('cannot read before a file is chosen', async () => {
  setup()

  await screen.findByRole('heading', { name: 'Import', level: 1 })

  expect(screen.getByRole('button', { name: 'Read the file' })).toBeDisabled()
})

it('only offers vehicles whose logs the user may add to, and says when there are none', async () => {
  const { ui } = setup({ vehicles: [fakeVehicle({ canEdit: false, logAccess: 'VIEW' })] })
  await toTarget(ui)

  expect(await screen.findByText(/no vehicle you can add logs to/)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'What will be imported' })).toBeDisabled()
})

it('lists the steps in order and marks the current one for screen readers', async () => {
  const { ui } = setup()
  const steps = within(await screen.findByRole('list', { name: 'Import' })).getAllByRole('listitem')
  const current = () => steps.filter((s) => s.getAttribute('aria-current') === 'step').map((s) => s.textContent)

  expect(steps.map((s) => s.textContent)).toEqual(['1File', '2Target', '3Review', '4Done'])
  expect(current()).toEqual(['1File'])

  await toTarget(ui)
  expect(current()).toEqual(['2Target'])
})

it('has an entry in the navigation', async () => {
  setup()

  await screen.findByRole('heading', { name: 'Import', level: 1 })

  expect(within(screen.getByRole('navigation', { name: 'Main navigation' })).getByRole('link', { name: 'Import' })).toHaveAttribute('aria-current', 'page')
})

it('has no accessibility violations on any step', async () => {
  const issues = [{ section: 'costs', row: 3, key: 'import.templateSkipped', args: { title: 'Brake pads' } }]
  const { ui } = setup({ previewFor: () => preview({ issues }) })
  const check = async () => {
    const results = await axe(document.body, { rules: { 'color-contrast': { enabled: false } } })
    expect(results.violations.map((v) => `${v.id}: ${v.help} (${v.nodes.map((n) => n.target.join(' ')).join(', ')})`)).toEqual([])
  }
  await screen.findByRole('heading', { name: 'Import', level: 1 })
  await check()

  await toTarget(ui)
  await check()
  await ui.click(screen.getByRole('radio', { name: 'A new vehicle' }))
  await check()
  await ui.click(screen.getByRole('button', { name: 'What will be imported' }))
  await screen.findByText(/Left out while reading/)
  await check()
  await ui.click(screen.getByRole('button', { name: 'Import' }))
  await screen.findByRole('heading', { name: 'Import finished' })
  await check()
})
