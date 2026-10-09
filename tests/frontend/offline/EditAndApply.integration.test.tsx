import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { gqlError, healthHandler, renderWithApollo, sessionHandler, silenceConsoleError, stubViewport } from '../support/mocks.tsx'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

/** A change the server parked, as `ParkedChanges` lists it: on Octavia (v1, kilometres and litres), sent by Bob, resolvable. */
function parkedRow(id: string, kind: string, change: object, reason: string, targetId: string) {
  return {
    __typename: 'SyncChangeInfo', id, kind, status: 'PARKED', vehicleId: 'v1', targetId, change: JSON.stringify(change), receivedAt: '2026-10-06T09:00:00Z',
    reason: { __typename: 'SyncReasonInfo', key: reason, args: [] }, vehicle: { __typename: 'SyncVehicleRef', id: 'v1', name: 'Octavia', distanceUnit: 'KILOMETERS', volumeUnit: 'LITERS' },
    submittedBy: { __typename: 'UserRef', id: 'u2', displayName: 'Bob' }, canResolve: true,
  }
}

/** The Waiting to sync page with `rows` parked on the server, which applies an edited change with `resolve` (its input recorded). */
function setup(rows: object[], resolve: (input: { id: string }) => Response) {
  stubViewport('desktop')
  let parked = rows
  const sent: { id: string; change: Record<string, unknown> }[] = []
  let parkedAnswers = 0
  const state = { failParkedAfter: Infinity, failBellAfter: Infinity }
  let bellAnswers = 0
  server.use(
    sessionHandler('NONE', () => null),
    healthHandler,
    // What the dialogs ask for their choices (the values themselves come from the change).
    graphql.query('VehicleDefaults', () =>
      HttpResponse.json({ data: { vehicleDefaults: { distanceUnit: 'KILOMETERS', volumeUnit: 'LITERS', currency: 'HUF', recurringWarnDays: 30, recurringWarnDistance: 500 } } }),
    ),
    graphql.query('ExpenseCategories', () => HttpResponse.json({ data: { expenseCategories: ['Fees'] } })),
    graphql.query('LogDefaults', () => HttpResponse.json({ data: { logDefaults: { lastOdometer: 1500, lastDate: '2026-09-20', currency: 'HUF' } } })),
    graphql.query('ParkedChanges', () =>
      ++parkedAnswers > state.failParkedAfter ? HttpResponse.json(gqlError('Busy', 'INTERNAL_ERROR')) : HttpResponse.json({ data: { parkedChanges: parked } }),
    ),
    graphql.query('UnreadNotificationCount', () =>
      ++bellAnswers > state.failBellAfter ? HttpResponse.json(gqlError('Busy', 'INTERNAL_ERROR')) : HttpResponse.json({ data: { notificationCount: 0 } }),
    ),
    graphql.mutation('ResolveSyncChange', ({ variables }) => {
      sent.push(variables.input)
      const answer = resolve(variables.input)
      if (answer.status === 200 && !answer.headers.get('x-refused')) parked = parked.filter((p) => (p as { id: string }).id !== variables.input.id)
      return answer
    }),
  )
  renderWithApollo(<App />, '/sync')
  return { sent, state, ui: userEvent.setup(), counts: () => ({ parkedAnswers, bellAnswers }) }
}

const applied = (row: object) => HttpResponse.json({ data: { resolveSyncChange: { ...row, status: 'APPLIED' } } })

it('Edit and apply, on a vehicle change the server parked, opens the vehicle dialog with what it carries and applies it as edited', async () => {
  const row = parkedRow('c2', 'UPDATE_VEHICLE', { id: 'c2', expectedVersion: 2, updateVehicle: { id: 'v1', name: 'Octavia RS', licensePlate: 'ABC-123', fuelType: 'DIESEL', units: { distance: 'KILOMETERS', volume: 'LITERS' } } }, 'sync.versionMismatch', 'v1')
  const { sent, state, ui, counts } = setup([row], () => applied(row))

  await ui.click(await screen.findByRole('button', { name: /^Edit and apply: Changed vehicle/ }, { timeout: 10_000 }))
  const dialog = await screen.findByRole('dialog', { name: 'Edit and apply the change' })
  expect(within(dialog).getByLabelText('Name')).toHaveValue('Octavia RS')
  await ui.clear(within(dialog).getByLabelText('Name'))
  await ui.type(within(dialog).getByLabelText('Name'), 'Octavia vRS')
  // Once applied, the page refreshes what it shows; the notification count failing to come back must not undo the success.
  state.failBellAfter = counts().bellAnswers
  await ui.click(within(dialog).getByRole('button', { name: 'Apply' }))

  expect(await screen.findByText('Applied.')).toBeInTheDocument()
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  // Applied against the version shown of what is on the server now (`current`; none shown here: over whatever is there), never the
  // version the device made it from, which is why it was parked.
  expect(sent).toEqual([{ id: 'c2', action: 'APPLY', change: expect.objectContaining({ id: 'c2', expectedVersion: null, updateVehicle: expect.objectContaining({ id: 'v1', name: 'Octavia vRS' }) }) }])
  await waitFor(() => expect(counts().bellAnswers).toBeGreaterThan(state.failBellAfter))
})

it('a refusal of an edited change stays in the dialog with its reason, even when the list of parked changes cannot be refreshed', async () => {
  silenceConsoleError()
  const row = parkedRow('c1', 'UPDATE_EXPENSE', { id: 'c1', expectedVersion: 1, updateExpense: { id: 'e1', date: '2026-09-20', title: 'Parking', category: 'Fees', amount: 1500, currency: 'HUF', odometer: null, note: null } }, 'sync.versionMismatch', 'e1')
  const { sent, state, ui, counts } = setup([row], () => HttpResponse.json(gqlError('Gone', 'NOT_FOUND', 'vehicle.notFound'), { headers: { 'x-refused': '1' } }))

  await ui.click(await screen.findByRole('button', { name: /^Edit and apply: Changed expense/ }, { timeout: 10_000 }))
  const dialog = await screen.findByRole('dialog', { name: 'Edit and apply the change' })
  expect(await within(dialog).findByLabelText('Title')).toHaveValue('Parking')
  await ui.clear(within(dialog).getByLabelText('Title'))
  await ui.type(within(dialog).getByLabelText('Title'), 'Parking garage')
  state.failParkedAfter = counts().parkedAnswers
  await ui.click(within(dialog).getByRole('button', { name: 'Apply' }))

  expect(await within(dialog).findByText('This vehicle does not exist.')).toBeInTheDocument()
  await waitFor(() => expect(counts().parkedAnswers).toBeGreaterThan(state.failParkedAfter)) // asked again for its new reason, which failed
  expect(sent).toHaveLength(1)
  expect(sent[0].change).toMatchObject({ updateExpense: { id: 'e1', title: 'Parking garage' } })
})
