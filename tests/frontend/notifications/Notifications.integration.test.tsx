import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { server } from '../support/server.ts'
import {
  fakeExpenseBackend,
  fakeNotification,
  fakeNotificationBackend,
  fakeRecurring,
  fakeRecurringBackend,
  fakeVehicle,
  healthHandler,
  renderWithApollo,
  sessionHandler,
  stubViewport,
  type FakeNotification,
} from '../support/mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

const shared = fakeNotification()
const overdue = fakeNotification({
  id: 'n2',
  kind: 'RECURRING_OVERDUE',
  updatedAt: '2026-10-01T08:00:00Z',
  subject: { type: 'RECURRING_EXPENSE', id: 'rc1' },
  args: [
    { name: 'title', value: 'Oil change' },
    { name: 'vehicleName', value: 'Family car' },
  ],
})
const revoked = fakeNotification({
  id: 'n3',
  read: true,
  count: 3,
  updatedAt: '2026-09-20T08:00:00Z',
  args: [
    { name: 'actorName', value: 'Bob' },
    { name: 'level', value: 'NONE' },
    { name: 'vehicleName', value: 'Old van' },
  ],
})

function setup(items: FakeNotification[] = [shared, overdue, revoked], route = '/') {
  stubViewport('desktop')
  const inbox = fakeNotificationBackend(items)
  const expenses = fakeExpenseBackend(fakeVehicle(), [])
  const recurring = fakeRecurringBackend([fakeRecurring({ id: 'rc1' })])
  server.use(sessionHandler('NONE', () => null), healthHandler, ...inbox.handlers, ...expenses.handlers, ...recurring.handlers)
  renderWithApollo(<App />, route)
  return { ...inbox, ui: userEvent.setup() }
}

it('the bell says how many notifications are unread, in words too', async () => {
  setup()

  expect(await screen.findByRole('button', { name: 'Notifications, 2 unread' })).toBeInTheDocument()
  expect(screen.getByText('You have 2 unread notifications.')).toBeInTheDocument()
})

it('the bell lists the latest notifications and marks them all read', async () => {
  const { ui, state } = setup()

  await ui.click(await screen.findByRole('button', { name: 'Notifications, 2 unread' }))
  const latest = await screen.findByRole('list', { name: 'Latest notifications' })

  const items = within(latest).getAllByRole('listitem').map((li) => li.textContent)
  expect(items[0]).toContain('NewOil change on Family car is overdue.')
  expect(items[1]).toContain('NewBob gave you access to the logs of Family car: Add and change logs.')
  expect(items[2]).toContain('Bob removed your access to the logs of Old van. (3 changes)')
  expect(within(latest).queryByRole('link', { name: /Old van/ })).not.toBeInTheDocument() // nothing to look at any more

  await ui.click(screen.getByRole('button', { name: 'Mark all read' }))

  await waitFor(() => expect(state.items.every((n) => n.read)).toBe(true))
  expect(state.calls.MarkNotificationsRead).toEqual([{ ids: null }])
  // The bell no longer counts any (the list is modal, so the page behind it is hidden from assistive technology while it is open).
  expect(await screen.findByRole('button', { name: 'Notifications', hidden: true })).toBeInTheDocument()
})

it('following a notification marks it read and leads to what it is about', async () => {
  const { ui, state } = setup()

  await ui.click(await screen.findByRole('button', { name: 'Notifications, 2 unread' }))
  await ui.click(await screen.findByRole('link', { name: 'Oil change on Family car is overdue.' }))

  expect(await screen.findByRole('rowheader', { name: /Oil change/ })).toBeInTheDocument() // the vehicle's recurring tab
  expect(screen.queryByRole('list', { name: 'Latest notifications' })).not.toBeInTheDocument()
  await waitFor(() => expect(state.calls.MarkNotificationsRead).toEqual([{ ids: ['n2'] }]))
})

it('the inbox page shows everything or only what is unread', async () => {
  const { ui } = setup(undefined, '/notifications')

  const list = await screen.findByRole('list', { name: 'Notifications' })
  expect(within(list).getAllByRole('listitem')).toHaveLength(3)

  await ui.click(screen.getByRole('radio', { name: 'Unread' }))

  await waitFor(() => expect(within(screen.getByRole('list', { name: 'Notifications' })).getAllByRole('listitem')).toHaveLength(2))
})

it('notifications are marked read one by one or all at once, and cannot be deleted', async () => {
  const { ui, state } = setup(undefined, '/notifications')
  const list = await screen.findByRole('list', { name: 'Notifications' })
  expect(screen.getByText('Read notifications are removed automatically after a while.')).toBeInTheDocument()

  await ui.click(within(list).getAllByRole('button', { name: 'Mark read' })[0])
  await waitFor(() => expect(within(list).getAllByRole('button', { name: 'Mark read' })).toHaveLength(1))
  expect(state.calls.MarkNotificationsRead).toEqual([{ ids: ['n2'] }])

  await ui.click(screen.getByRole('button', { name: 'Mark all read' }))
  await waitFor(() => expect(within(list).queryAllByRole('button', { name: 'Mark read' })).toHaveLength(0))
  expect(within(list).getAllByRole('listitem')).toHaveLength(3) // read, but still listed
  expect(screen.queryByRole('button', { name: /Delete/ })).not.toBeInTheDocument()
})

it('marking read keeps the pages already loaded', async () => {
  const many = Array.from({ length: 25 }, (_, i) => fakeNotification({ id: `m${i}`, updatedAt: `2026-09-${String(30 - i).padStart(2, '0')}T08:00:00Z` }))
  const { ui, state } = setup(many, '/notifications')
  await screen.findByRole('list', { name: 'Notifications' })
  await ui.click(screen.getByRole('button', { name: 'Show more' }))
  await waitFor(() => expect(within(screen.getByRole('list', { name: 'Notifications' })).getAllByRole('listitem')).toHaveLength(25))

  await ui.click(within(screen.getByRole('list', { name: 'Notifications' })).getAllByRole('button', { name: 'Mark read' })[24])

  await waitFor(() => expect(state.items.find((n) => n.id === 'm24')?.read).toBe(true))
  await waitFor(() => expect(within(screen.getByRole('list', { name: 'Notifications' })).getAllByRole('button', { name: 'Mark read' })).toHaveLength(24))
  expect(within(screen.getByRole('list', { name: 'Notifications' })).getAllByRole('listitem')).toHaveLength(25)
})

it('more notifications are loaded on request', async () => {
  const many = Array.from({ length: 25 }, (_, i) => fakeNotification({ id: `m${i}`, updatedAt: `2026-09-${String(30 - i).padStart(2, '0')}T08:00:00Z` }))
  const { ui } = setup(many, '/notifications')
  const list = await screen.findByRole('list', { name: 'Notifications' })
  expect(within(list).getAllByRole('listitem')).toHaveLength(20)

  await ui.click(screen.getByRole('button', { name: 'Show more' }))

  await waitFor(() => expect(within(screen.getByRole('list', { name: 'Notifications' })).getAllByRole('listitem')).toHaveLength(25))
  expect(screen.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument()
})

it('explains an empty inbox', async () => {
  setup([], '/notifications')

  expect(await screen.findByText(/Nothing here yet/)).toBeInTheDocument()
})
