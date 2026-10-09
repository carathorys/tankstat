import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { server } from '../support/server.ts'
import { fakeVehicle, fakeVehicleBackend, gqlError, healthHandler, person, renderWithApollo, sessionHandler, silenceConsoleError, stubViewport } from '../support/mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

const octavia = { id: 'v1', name: 'Octavia', units: { distance: 'KILOMETERS', volume: 'LITERS' } }

/**
 * Trashed logs as the server can send them: one whose vehicle is gone for good (`vehicle: null`) and one waiting for its photos (no
 * amounts yet), neither with a known creator. The restore mutations are recorded.
 */
function setup(route: string) {
  stubViewport('desktop')
  const calls: Record<string, unknown[]> = {}
  const record = (name: string, vars: unknown) => (calls[name] ??= []).push(vars)
  const deletedAt = '2026-10-01T08:00:00Z'
  const vehicles = fakeVehicleBackend([fakeVehicle()], [fakeVehicle({ id: 't1', name: 'Old Fiat', ownerName: null })])
  server.use(
    sessionHandler('NONE', () => null),
    healthHandler,
    graphql.query('RefuelingTrash', () =>
      HttpResponse.json({
        data: {
          refuelingTrash: [
            { id: 'r1', version: 2, date: '2026-08-01', vehicle: null, volume: 30, totalCost: 45, currency: null, odometer: 1000, deletedAt, createdBy: null },
            { id: 'r2', version: 1, date: '2026-09-01', vehicle: octavia, volume: null, totalCost: null, currency: null, odometer: null, deletedAt, createdBy: person('Alice') },
          ],
          refuelingTrashCount: 2,
          refuelingTrashDeletableCount: 2,
        },
      }),
    ),
    graphql.query('ExpenseTrash', () =>
      HttpResponse.json({
        data: {
          expenseTrash: [
            { id: 'e1', version: 3, date: '2026-08-01', title: 'Parking', vehicle: null, category: null, amount: 1500, currency: null, deletedAt, createdBy: null },
            { id: 'e2', version: 1, date: '2026-09-01', title: 'Car wash', vehicle: { id: 'v1', name: 'Octavia' }, category: 'Care', amount: null, currency: null, deletedAt, createdBy: person('Alice') },
          ],
          expenseTrashCount: 2,
          expenseTrashDeletableCount: 2,
        },
      }),
    ),
    graphql.mutation('RestoreRefueling', ({ variables }) => (record('RestoreRefueling', variables), HttpResponse.json({ data: { restoreRefueling: { id: variables.id } } }))),
    graphql.mutation('RestoreExpense', ({ variables }) => (record('RestoreExpense', variables), HttpResponse.json({ data: { restoreExpense: { id: variables.id } } }))),
    ...vehicles.handlers,
  )
  renderWithApollo(<App />, route)
  return { calls, vehicles, ui: userEvent.setup() }
}

const rowOf = async (text: RegExp | string) => (await screen.findByText(text)).closest<HTMLElement>('[role="row"]')!
const cells = (row: HTMLElement) => within(row).getAllByRole('gridcell').map((c) => c.textContent)

it('a trashed refuelling shows a dash for what is not known: its vehicle, the amounts it still waits for, who logged it', async () => {
  setup('/trash?tab=refuelings')

  const gone = await rowOf(/Aug 1, 2026/)
  // Without its vehicle there are no units to show the volume and the odometer in; a total without a currency is not shown either.
  expect(cells(gone).slice(0, 5)).toEqual(['–', '–', '–', '–', '–'])
  const waiting = await rowOf(/Sep 1, 2026/)
  expect(within(waiting).getByText('Octavia')).toBeInTheDocument()
  expect(cells(waiting).slice(0, 4)).toEqual(['Octavia', '–', '–', '–'])
})

it('a refuelling whose vehicle is gone is restored straight away: there is no vehicle to keep the change for', async () => {
  const { ui, calls } = setup('/trash?tab=refuelings')
  await screen.findByText(/Aug 1, 2026/)

  await ui.click(screen.getByRole('button', { name: 'Restore Aug 1, 2026' }))

  await waitFor(() => expect(calls.RestoreRefueling).toEqual([{ id: 'r1' }]))
})

it('a trashed expense shows a dash for what is not known: its vehicle, a category, an amount without a currency, who logged it', async () => {
  setup('/trash?tab=expenses')

  const gone = await rowOf('Parking')
  expect(cells(gone).slice(0, 5)).toEqual(['Parking', '–', '–', '–', '–'])
  const waiting = await rowOf('Car wash')
  expect(cells(waiting).slice(0, 3)).toEqual(['Car wash', 'Octavia', '–'])
  expect(within(waiting).getByText('Care')).toBeInTheDocument()
})

it('an expense is restored from its own tab; one whose vehicle is gone straight away', async () => {
  const { ui, calls } = setup('/trash?tab=expenses')
  await screen.findByText('Parking')

  await ui.click(screen.getByRole('button', { name: 'Restore Parking' }))
  await waitFor(() => expect(calls.RestoreExpense).toEqual([{ id: 'e1' }]))

  await ui.click(screen.getByRole('button', { name: 'Restore Car wash' }))
  await waitFor(() => expect(calls.RestoreExpense).toHaveLength(2))
  expect(calls.RestoreExpense[1]).toEqual({ id: 'e2' })
})

it('a trashed vehicle without an owner shows a dash in the owner column', async () => {
  setup('/trash')

  const row = await rowOf('Old Fiat')
  expect(within(row).getByText('–')).toBeInTheDocument()
})

it('going back to the vehicles tab takes the tab out of the address and lists the vehicles again', async () => {
  const { ui } = setup('/trash?tab=expenses')
  await screen.findByText('Parking')

  await ui.click(screen.getByRole('tab', { name: /Vehicles/ }))

  expect(await screen.findByText('Old Fiat')).toBeInTheDocument()
  expect(screen.getByRole('tab', { name: /Vehicles/, selected: true })).toBeInTheDocument()
  expect(screen.queryByText('Parking')).not.toBeInTheDocument()
})

it('a restore the server refuses says why, and the vehicle stays in the trash', async () => {
  silenceConsoleError()
  const { ui } = setup('/trash')
  server.use(graphql.mutation('RestoreVehicle', () => HttpResponse.json(gqlError('Not found', 'NOT_FOUND', 'vehicle.notFound'))))
  await screen.findByText('Old Fiat')

  await ui.click(screen.getByRole('button', { name: 'Restore Old Fiat' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('This vehicle does not exist.')
  expect(screen.getByText('Old Fiat')).toBeInTheDocument()
})
