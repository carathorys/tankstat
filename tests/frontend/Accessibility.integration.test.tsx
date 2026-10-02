import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { axe } from 'vitest-axe'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../src/frontend/App.tsx'
import { server } from './server.ts'
import { fakeExpense, fakeExpenseBackend, fakeLogBackend, fakeRefueling, fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport, user } from './mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

// Colour contrast cannot be computed without a real renderer; everything else axe knows is checked.
const check = async (container: HTMLElement) => {
  const results = await axe(container, { rules: { 'color-contrast': { enabled: false } } })
  expect(results.violations.map((v) => `${v.id}: ${v.help} (${v.nodes.map((n) => n.target.join(' ')).join(', ')})`)).toEqual([])
}

function setup(route: string, viewport: 'desktop' | 'phone' = 'desktop') {
  stubViewport(viewport)
  const logs = fakeLogBackend(fakeVehicle(), [fakeRefueling({ id: 'r1' }), fakeRefueling({ id: 'r2', date: '2026-08-01', note: 'Trip' })])
  const expenseBackend = fakeExpenseBackend(fakeVehicle(), [fakeExpense({ id: 'e1' }), fakeExpense({ id: 'e2', title: 'Parking', category: null, odometer: null })])
  expenseBackend.state.trash = [fakeExpense({ id: 'x1', title: 'Old fee', deletedAt: '2026-10-01T08:00:00Z' })]
  const vehicles = fakeVehicleBackend([fakeVehicle()], [fakeVehicle({ id: 't1', name: 'Old Fiat' })])
  server.use(
    sessionHandler('STANDALONE', () => user({ isAdmin: true })),
    healthHandler,
    ...logs.handlers,
    ...expenseBackend.handlers, // shares VehicleDetails and LogDefaults with the logs backend: the first handler wins, they answer alike
    ...vehicles.handlers,
    graphql.query('Admin', () =>
      HttpResponse.json({
        data: {
          users: [
            { id: 'u1', provider: 'LOCAL', email: 'alice@example.com', displayName: 'Alice', isAdmin: true, isDisabled: false, avatarUrl: null },
            { id: 'u2', provider: 'LOCAL', email: 'bob@example.com', displayName: 'Bob', isAdmin: false, isDisabled: false, avatarUrl: null },
          ],
          canSetUserPasswords: true,
          accessSettings: { defaultLevelForOthers: 'NONE' },
          accessGrants: [],
        },
      }),
    ),
  )
  const view = renderWithApollo(<App />, route)
  return { view, ui: userEvent.setup() }
}

it('the vehicle list has no accessibility violations (desktop)', async () => {
  const { view } = setup('/vehicles')
  await screen.findByText('Octavia')

  await check(view.container)
})

it('the vehicle list has no accessibility violations (phone, menu drawer open)', async () => {
  const { view, ui } = setup('/vehicles', 'phone')
  await screen.findByText('Octavia')
  await ui.click(screen.getByRole('button', { name: 'Show menu' }))

  await check(await screen.findByRole('dialog').then((d) => d.ownerDocument.body)).catch((e) => {
    throw e
  })
  expect(view.container).toBeTruthy()
})

it('the vehicle page and its tabs have no violations', async () => {
  const { view, ui } = setup('/vehicles/v1?tab=refuelings')
  await screen.findByText(/Sep 1, 2026/)
  await check(view.container)

  await ui.click(screen.getByRole('tab', { name: /Expenses/ }))
  await screen.findByText('Oil change')
  await check(view.container)

  await ui.click(screen.getByRole('tab', { name: /Details/ }))
  await screen.findByRole('button', { name: 'Choose a picture' })
  await check(view.container)

  await ui.click(screen.getByRole('tab', { name: /Sharing/ }))
  await screen.findByText('Nobody has been given access to the logs yet.')
  await check(view.container)
})

it('the add refuelling dialog is labelled, described and free of violations', async () => {
  const { ui } = setup('/vehicles/v1?tab=refuelings')
  await screen.findByText(/Sep 1, 2026/)

  await ui.click(screen.getByRole('button', { name: 'Add refuelling' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add refuelling' })
  await within(dialog).findByText(/Last reading/)

  expect(dialog).toHaveAccessibleDescription(/Enter what you filled up/)
  await check(document.body)
})

it('the trash, account and administration pages have no violations', async () => {
  for (const route of ['/trash', '/trash?tab=refuelings', '/trash?tab=expenses', '/account', '/admin']) {
    const { view } = setup(route)
    await screen.findByRole('main')
    await screen.findByRole('heading', { level: 1 })
    await check(view.container)
    view.unmount()
  }
})

it('sortable columns announce their state and every grid is a labelled table', async () => {
  setup('/vehicles/v1?tab=refuelings')
  await screen.findByText(/Sep 1, 2026/)

  expect(screen.getByRole('table', { name: 'Refuelings' })).toBeInTheDocument()
  expect(screen.getByRole('columnheader', { name: /Date/ })).toHaveAttribute('aria-sort', 'descending')
  expect(screen.getByRole('columnheader', { name: /Odometer/ })).toHaveAttribute('aria-sort', 'none')
})

it('the add expense dialog is labelled, described and free of violations', async () => {
  const { ui } = setup('/vehicles/v1?tab=expenses')
  await screen.findByText('Oil change')

  await ui.click(screen.getByRole('button', { name: 'Add expense' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add expense' })
  await within(dialog).findByText(/Used before/)

  expect(dialog).toHaveAccessibleDescription(/Money spent on the vehicle/)
  await check(document.body)
})

it('the user administration dialogs are labelled, described and free of violations', async () => {
  const { ui } = setup('/admin')
  const table = await screen.findByRole('table')
  await within(table).findByText('bob@example.com')

  for (const [button, dialogName] of [
    ['Edit Bob', 'Edit Bob'],
    ['Set password for Bob', 'Set password for Bob'],
    ['Delete Bob', 'Delete Bob?'],
  ] as const) {
    await ui.click(within(table).getByRole('button', { name: button }))
    const dialog = await screen.findByRole('dialog', { name: dialogName })
    expect(dialog).toHaveAccessibleDescription()
    await check(document.body)
    await ui.click(within(dialog).getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  }
})
