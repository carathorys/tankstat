import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { axe } from 'vitest-axe'
import { afterAll, afterEach, beforeAll, expect, it, onTestFinished, vi } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { ErrorBoundary } from '../../../src/frontend/ErrorBoundary.tsx'
import { SIGNED_OUT_KEY } from '../../../src/frontend/auth/oidc.ts'
import { navigation } from '../../../src/frontend/navigation.ts'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { platform } from '../../../src/frontend/pwa/platform.ts'
import { server } from '../support/server.ts'
import { fakeExpense, fakeExpenseBackend, fakeLogBackend, fakeNotification, fakeNotificationBackend, fakePhotoStore, fakeRecognition, fakeRecurring, fakeRecurringBackend, fakeRefueling, fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, silenceConsoleError, stubViewport, user } from '../support/mocks.tsx'

// jsdom cannot decode pictures (the resize is covered in Media.unit.test.ts) and has no object URLs (previews of queued photos).
vi.mock('../../../src/frontend/pictures/resizeImage.ts', async (original) => ({
  ...(await original<typeof import('../../../src/frontend/pictures/resizeImage.ts')>()),
  resizeImage: vi.fn(async (file: Blob) => file),
}))

beforeAll(() => {
  server.listen({ onUnhandledRequest: 'error' })
  let n = 0
  URL.createObjectURL = () => `blob:preview-${n++}`
  URL.revokeObjectURL = () => undefined
})
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

function setup(route: string, viewport: 'desktop' | 'phone' = 'desktop', photos = fakePhotoStore()) {
  stubViewport(viewport)
  const logs = fakeLogBackend(fakeVehicle(), [fakeRefueling({ id: 'r1' }), fakeRefueling({ id: 'r2', date: '2026-08-01', note: 'Trip' })], photos)
  const expenseBackend = fakeExpenseBackend(fakeVehicle(), [fakeExpense({ id: 'e1' }), fakeExpense({ id: 'e2', title: 'Parking', category: null, odometer: null })], photos)
  expenseBackend.state.trash = [fakeExpense({ id: 'x1', title: 'Old fee', deletedAt: '2026-10-01T08:00:00Z' })]
  const recurring = fakeRecurringBackend([
    fakeRecurring({ id: 'rc2', title: 'Tyres', kind: 'ODOMETER', intervalMonths: null, status: { state: 'OVERDUE', limit: 'ODOMETER', dueDate: null, dueOdometer: 60000, daysLeft: null, distanceLeft: -300 } }),
    fakeRecurring(),
  ])
  const attention = [fakeRecurring({ id: 'rc2', title: 'Tyres', kind: 'ODOMETER', intervalMonths: null, status: { state: 'OVERDUE', limit: 'ODOMETER', dueDate: null, dueOdometer: 60000, daysLeft: null, distanceLeft: -300 } })]
  const vehicles = fakeVehicleBackend([fakeVehicle({ recurring: attention })], [fakeVehicle({ id: 't1', name: 'Old Fiat' })])
  const inbox = fakeNotificationBackend([
    fakeNotification(),
    fakeNotification({ id: 'n2', kind: 'RECURRING_OVERDUE', read: true, args: [{ name: 'title', value: 'Tyres' }, { name: 'vehicleName', value: 'Octavia' }] }),
    fakeNotification({ id: 'n3', kind: 'MORE_ACTIVITY', count: 4, context: null, args: [] }),
  ])
  server.use(
    ...inbox.handlers,
    sessionHandler('STANDALONE', () => user({ isAdmin: true })),
    healthHandler,
    ...logs.handlers,
    ...recurring.handlers,
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

it('the message shown when a page crashes is an alert with a reachable reload button and has no violations', async () => {
  silenceConsoleError() // React prints the component that throws
  const Boom = () => {
    throw new Error('boom')
  }
  const view = renderWithApollo(
    <ErrorBoundary>
      <Boom />
    </ErrorBoundary>,
  )

  expect(await screen.findByRole('alert')).toHaveTextContent('Something went wrong while showing this page.')
  expect(screen.getByRole('button', { name: 'Reload the page' })).toBeVisible()
  await check(view.container)
})

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

it('values read from a photo, and a photo value offered next to a typed one, are linked to their fields and free of violations', async () => {
  const { ui } = setup('/vehicles/v1?tab=refuelings')
  server.use(...fakeRecognition({ results: [[{ name: 'TOTAL', value: '24687' }, { name: 'VOLUME', value: '38.52' }]], queuedPolls: 0 }).handlers)
  await screen.findByText(/Sep 1, 2026/)
  await ui.click(screen.getByRole('button', { name: 'Add refuelling' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add refuelling' })
  await within(dialog).findByText(/Last reading/)
  await ui.type(within(dialog).getByLabelText(/^Volume/), '40')

  await ui.upload(within(dialog).getByTestId('photo-camera'), new File([new Uint8Array([1, 2, 3])], 'receipt.png', { type: 'image/png' }))

  expect(await within(dialog).findByText('The photo shows 38.52', {}, { timeout: 5000 })).toBeInTheDocument()
  expect(within(dialog).getByLabelText('Total cost')).toHaveAccessibleDescription(/Read from the photo; check it\./)
  expect(within(dialog).getByLabelText(/^Volume/)).toHaveAccessibleDescription(/The photo shows 38\.52/)
  await check(document.body)
})

it('the reasons a photo gave less than it might have are announced in the dialog and free of violations', async () => {
  const { ui } = setup('/vehicles/v1?tab=refuelings')
  server.use(...fakeRecognition({ results: [[{ name: 'TOTAL', value: '24687' }]], issues: [[{ code: 'UNSURE', field: 'VOLUME' }, { code: 'ODOMETER_BELOW_LATEST', field: 'ODOMETER' }]], queuedPolls: 0 }).handlers)
  await screen.findByText(/Sep 1, 2026/)
  await ui.click(screen.getByRole('button', { name: 'Add refuelling' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add refuelling' })
  await within(dialog).findByText(/Last reading/)

  await ui.upload(within(dialog).getByTestId('photo-camera'), new File([new Uint8Array([1, 2, 3])], 'receipt.png', { type: 'image/png' }))

  const status = await within(dialog).findByRole('status', { name: 'Photo reading status' }, { timeout: 5000 })
  await waitFor(() => expect(status).toHaveTextContent(/lower than the last one logged/), { timeout: 5000 })
  expect(status).toHaveTextContent(/not reliably enough to fill it in/) // inside the live region, so a screen reader hears it
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

it('the signed-in devices and the confirmation to sign out the others are free of violations', async () => {
  const { view, ui } = setup('/account')
  const at = (id: string, client: string | null, current: boolean) => ({
    id, client, current, createdAt: '2026-09-01T08:00:00Z', lastUsedAt: '2026-10-01T08:00:00Z', expiresAt: '2026-12-30T08:00:00Z',
  })
  server.use(graphql.query('MySessions', () => HttpResponse.json({ data: { mySessions: [at('s1', 'Firefox on Linux', true), at('s2', null, false)] } })))
  const devices = await screen.findByRole('region', { name: 'Signed-in devices' })
  await within(devices).findByText('Unknown device')
  await check(view.container)

  await ui.click(within(devices).getByRole('button', { name: 'Sign out everywhere else' }))
  await screen.findByRole('alertdialog', { name: 'Sign out everywhere else?' })
  await check(document.body)
})

it('offline, the top bar, the footer and a page that needs the server are free of violations', async () => {
  const { view, ui } = setup('/')
  await screen.findByRole('heading', { level: 1 })
  connectivity.failed()

  await screen.findByText('Offline')
  await ui.click(screen.getByRole('link', { name: 'Trash' }))
  await screen.findAllByText(/This needs a connection to the server/)
  await check(view.container)
})

it('the notifications page and the open bell are labelled and free of violations', async () => {
  const { view, ui } = setup('/notifications')
  await screen.findByRole('list', { name: 'Notifications' })
  await check(view.container)

  await ui.click(screen.getByRole('button', { name: 'Notifications, 2 unread' }))
  await screen.findByRole('list', { name: 'Latest notifications' })
  await check(document.body)
})

it('the colour mode and language menus are labelled radio menus, free of violations', async () => {
  const { ui } = setup('/')
  await screen.findByText('Octavia')

  await ui.click(screen.getByRole('button', { name: 'Colour mode' }))
  const modes = await screen.findByRole('menu', { name: 'Colour mode' })
  expect(within(modes).getAllByRole('menuitemradio').map((item) => [item.textContent, item.getAttribute('aria-checked')])).toEqual([
    ['Light', 'false'],
    ['Dark', 'true'],
    ['System', 'false'],
  ])
  expect(screen.getByRole('button', { name: 'Colour mode', hidden: true })).toHaveAttribute('aria-controls', modes.id)
  await check(modes) // the menu itself: a transient popup outside the page's landmarks, as every popup menu is
  await ui.keyboard('{Escape}')

  await ui.click(screen.getByRole('button', { name: 'Language' }))
  const languages = await screen.findByRole('menu', { name: 'Language' })
  expect(within(languages).getByRole('menuitemradio', { name: 'Magyar' })).toHaveAttribute('lang', 'hu')
  await check(languages)
})

it('the floating add button of a phone and its menu are labelled and free of violations', async () => {
  const { view, ui } = setup('/vehicles/v1?tab=refuelings', 'phone')
  await screen.findByText(/Sep 1, 2026/)
  const fab = screen.getByRole('button', { name: 'Add a refuelling or an expense' })
  await check(view.container)

  await ui.click(fab)
  const menu = await screen.findByRole('menu', { name: 'Add a refuelling or an expense' })
  expect(fab).toHaveAttribute('aria-controls', menu.id)
  await check(menu)
})

it('the message that offers Undo after trashing is a status and free of violations', async () => {
  const { ui } = setup('/vehicles/v1?tab=refuelings')
  await screen.findByText(/Sep 1, 2026/)

  await ui.click(screen.getByRole('button', { name: 'Delete the refuelling of Sep 1, 2026' }))
  await ui.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Move to trash' }))

  const undo = await screen.findByRole('button', { name: 'Undo' })
  const message = undo.closest('[role="status"]') as HTMLElement
  expect(message).toHaveTextContent('is in the trash.')
  await check(message)
})

it('sortable columns announce their state and every grid is labelled, its rows named by their first cell', async () => {
  setup('/vehicles/v1?tab=refuelings')
  await screen.findByText(/Sep 1, 2026/)

  const grid = screen.getByRole('grid', { name: 'Refuelings' })
  expect(within(grid).getAllByRole('rowheader').map((cell) => cell.textContent)).toEqual(['Sep 1, 2026', 'Aug 1, 2026'])
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

it('the photo gallery of the expense dialogs is a labelled group and free of violations', async () => {
  const { ui } = setup('/vehicles/v1?tab=expenses')
  await screen.findByText('Oil change')

  await ui.click(screen.getByRole('button', { name: 'Add expense' }))
  const adding = await screen.findByRole('dialog', { name: 'Add expense' })
  await within(adding).findByText(/Used before/)
  expect(within(adding).getByRole('group', { name: 'Photos' })).toBeInTheDocument()
  expect(within(adding).getByRole('button', { name: 'Take photo' })).toBeInTheDocument()
  await check(document.body)
  await ui.click(within(adding).getByRole('button', { name: 'Cancel' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

  await ui.click(screen.getAllByRole('button', { name: /^Edit the expense/ })[0])
  const editing = await screen.findByRole('dialog', { name: 'Edit expense' })
  await within(editing).findByRole('group', { name: 'Photos' })
  await check(document.body)
})

it('a refuelling waiting for a review is marked in its row, and its edit dialog says what to check, free of violations', async () => {
  stubViewport('desktop')
  const backend = fakeLogBackend(fakeVehicle(), [
    fakeRefueling({ id: 'r1', odometer: 12480, reviewState: 'NEEDS_REVIEW', filledFromPhoto: ['ODOMETER'] }),
    fakeRefueling({ id: 'r2', date: '2026-09-10', odometer: null, reviewState: 'AWAITING_PHOTOS' }),
    fakeRefueling({ id: 'r3', date: '2026-09-20', volume: null, reviewState: 'INCOMPLETE' }),
  ])
  server.use(sessionHandler('NONE', () => null), healthHandler, ...backend.handlers)
  renderWithApollo(<App />, '/vehicles/v1?tab=refuelings')
  const ui = userEvent.setup()
  await screen.findByText('Check values')
  expect(screen.getByText('Reading photo…')).toBeInTheDocument()
  expect(screen.getByText('Values missing')).toBeInTheDocument()
  await check(document.body)

  await ui.click(screen.getAllByRole('button', { name: /^Edit the refuelling/ }).at(-1)!)
  const dialog = await screen.findByRole('dialog', { name: 'Edit refuelling' })
  await within(dialog).findByText(/Check them and save/)

  expect(within(dialog).getByLabelText(/^Odometer/)).toHaveAccessibleDescription(/Read from the photo; check it\./)
  await check(document.body)
})

it('the edit refuelling dialog with its photos is free of violations', async () => {
  const { ui } = setup('/vehicles/v1?tab=refuelings')
  await screen.findByText(/Sep 1, 2026/)

  await ui.click(screen.getAllByRole('button', { name: /^Edit the refuelling/ })[0])
  const dialog = await screen.findByRole('dialog', { name: 'Edit refuelling' })
  await within(dialog).findByRole('group', { name: 'Photos' })
  expect(within(dialog).getByRole('button', { name: 'Take photo' })).toBeInTheDocument()

  await check(document.body)
})

it('a gallery that holds photos is free of violations and its thumbnails and buttons are labelled', async () => {
  const photos = fakePhotoStore({
    e1: [{ id: 'p1', url: '/media/p1' }, { id: 'p2', url: '/media/p2' }],
    e2: [{ id: 'p3', url: '/media/p3' }, { id: 'p4', url: '/media/p4' }],
  })
  const { ui } = setup('/vehicles/v1?tab=expenses', 'desktop', photos)
  await screen.findByText('Oil change')

  await ui.click(screen.getAllByRole('button', { name: /^Edit the expense/ })[0])
  const dialog = await screen.findByRole('dialog', { name: 'Edit expense' })
  expect(await within(dialog).findAllByRole('link', { name: /^Open photo/ })).toHaveLength(2)
  expect(within(dialog).getAllByRole('button', { name: /^Remove photo/ })).toHaveLength(2)

  await check(document.body)
})

it('a photo that could not be uploaded, and the screen after a photo could not be attached, are free of violations', async () => {
  const photos = fakePhotoStore()
  photos.state.failWith = { key: 'photo.tooManyDrafts', args: { max: 20 } }
  const { ui } = setup('/vehicles/v1?tab=expenses', 'desktop', photos)
  await screen.findByText('Oil change')

  await ui.click(screen.getByRole('button', { name: 'Add expense' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add expense' })
  await within(dialog).findByText(/Used before/)
  await ui.type(within(dialog).getByLabelText('Title'), 'Tyres')
  await ui.type(within(dialog).getByLabelText('Amount'), '120000')
  const file = new File([new Uint8Array([1, 2, 3])], 'a.png', { type: 'image/png' })
  await ui.upload(within(dialog).getByTestId('photo-library'), file)
  await within(dialog).findByRole('alert')
  expect(within(dialog).getByRole('button', { name: 'Upload photo 1 again' })).toBeInTheDocument()
  await check(document.body)

  photos.state.failWith = undefined
  photos.state.unattachable.add('draft1')
  await ui.click(within(dialog).getByRole('button', { name: 'Upload photo 1 again' }))
  await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Add expense' })).toBeEnabled())
  await ui.click(within(dialog).getByRole('button', { name: 'Add expense' }))

  expect(await within(dialog).findByRole('alert')).toHaveTextContent(/could not be attached/)
  expect(within(dialog).getByRole('button', { name: 'Done' })).toBeInTheDocument()
  await check(document.body)
})

it('the recurring expenses table and its dialogs are labelled and free of violations', async () => {
  const { ui } = setup('/vehicles/v1?tab=recurring')
  const table = await screen.findByRole('table', { name: 'Recurring expenses' })
  within(table).getByText('Tyres')
  await waitFor(() => expect(table.querySelectorAll('[data-limit]')).toHaveLength(3)) // the gauges (one schedule has two), once their chunk is in
  await check(document.body)

  await ui.click(screen.getByRole('button', { name: 'Add recurring expense' }))
  const adding = await screen.findByRole('dialog', { name: 'Add recurring expense' })
  expect(adding).toHaveAccessibleDescription(/comes back again and again/)
  await check(document.body)
  await ui.click(within(adding).getByRole('button', { name: 'Cancel' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

  await ui.click(screen.getByRole('button', { name: 'Edit the recurring expense Tyres' }))
  const editing = await screen.findByRole('dialog', { name: 'Edit recurring expense' })
  await within(editing).findByLabelText('Title')
  await check(document.body)
  await ui.click(within(editing).getByRole('button', { name: 'Cancel' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

  await ui.click(screen.getByRole('button', { name: 'Mark Tyres as done' }))
  const done = await screen.findByRole('dialog', { name: 'Mark as done: Tyres' })
  await within(done).findByLabelText('Currency')
  expect(within(done).getByRole('group', { name: 'Done at this visit' })).toBeInTheDocument() // the schedules to tick, as labelled checkboxes
  await check(document.body)
})

// Its own test: each accessibility scan takes a while on CI, and one test has 5 s.
it('the selection column of the recurring expenses and its Mark selected as done dialog are free of violations', async () => {
  const { ui } = setup('/vehicles/v1?tab=recurring')
  await screen.findByRole('table', { name: 'Recurring expenses' })

  await ui.click(screen.getByRole('checkbox', { name: 'Select all recurring expenses' }))
  await check(document.body)
  await ui.click(screen.getByRole('button', { name: /selected as done/ }))
  const many = await screen.findByRole('dialog', { name: 'Mark as done' })
  await within(many).findByLabelText('Currency')
  await check(document.body)
})

it('the home page card lists what needs attention in a labelled list, free of violations', async () => {
  setup('/')
  const list = await screen.findByRole('list', { name: 'Needs attention' })

  expect(within(list).getByText(/Tyres · 300 km over/)).toBeInTheDocument()
  await check(document.body)
})

it('the details tab with its trash button and confirmation is free of violations', async () => {
  const { ui } = setup('/vehicles/v1?tab=details')
  await ui.click(await screen.findByRole('button', { name: 'Move to trash' }))
  const dialog = await screen.findByRole('alertdialog', { name: 'Move Octavia to the trash?' })
  expect(dialog).toHaveAccessibleDescription(/restore it from the trash/)

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

it('the Arrange dialog is labelled, described and free of violations', async () => {
  stubViewport('desktop')
  const vehicles = fakeVehicleBackend([fakeVehicle({ id: 'a', name: 'Alpha' }), fakeVehicle({ id: 'b', name: 'Beta' })])
  server.use(sessionHandler('NONE', () => null), healthHandler, ...vehicles.handlers, ...fakeLogBackend(fakeVehicle({ id: 'a', name: 'Alpha' }), []).handlers)
  const ui = userEvent.setup()
  renderWithApollo(<App />, '/')
  await screen.findByRole('link', { name: 'Open Alpha' })

  await ui.click(screen.getByRole('button', { name: 'Arrange' }))
  const dialog = await screen.findByRole('dialog', { name: 'Arrange vehicles' })
  await within(dialog).findByRole('button', { name: 'Move Beta up' })

  expect(dialog).toHaveAccessibleDescription(/order/)
  await check(document.body)
})

it('the sign-in screens of every mode have no violations', async () => {
  const replace = vi.spyOn(navigation, 'replace').mockImplementation(() => undefined)
  onTestFinished(() => replace.mockRestore())

  // OIDC: on the way to the provider.
  server.use(sessionHandler('OIDC', () => null), healthHandler)
  let view = renderWithApollo(<App />, '/')
  await screen.findByText('Taking you to your identity provider…')
  await check(view.container)
  view.unmount()

  // OIDC: signed out on purpose.
  window.sessionStorage.setItem(SIGNED_OUT_KEY, '1')
  view = renderWithApollo(<App />, '/')
  await screen.findByRole('heading', { name: 'You have signed out.' })
  await check(view.container)
  view.unmount()
  window.sessionStorage.clear()

  // OIDC: the sign-in failed.
  view = renderWithApollo(<App />, '/?signIn=failed&reason=access_denied')
  await screen.findByRole('alert')
  await check(view.container)
  view.unmount()

  // Standalone: the password form.
  server.use(sessionHandler('STANDALONE', () => null), healthHandler)
  view = renderWithApollo(<App />, '/')
  await screen.findByLabelText(/Password/)
  await check(view.container)
  view.unmount()

  // Behind a proxy: only an explanation.
  server.use(sessionHandler('PROXY_HEADER', () => null), healthHandler)
  view = renderWithApollo(<App />, '/')
  await screen.findByText(/reverse proxy/i)
  await check(view.container)
})

it('the install how-to for iPhones is a labelled dialog without violations', async () => {
  vi.spyOn(platform, 'isIos').mockReturnValue(true)
  onTestFinished(() => {
    vi.restoreAllMocks()
  })
  const { ui } = setup('/', 'phone')
  await screen.findByText('Octavia')

  await ui.click(screen.getByRole('button', { name: 'Show menu' }))
  await ui.click(await screen.findByRole('button', { name: 'Install app' }))

  const dialog = await screen.findByRole('dialog', { name: 'Install Tankstat' })
  expect(dialog).toHaveAccessibleDescription(/Home Screen/)
  await check(document.body)
})

it('the quick actions on a home card have names with the vehicle in them, on a desktop and a phone, free of violations', async () => {
  const { view } = setup('/')
  const card = within((await screen.findByRole('link', { name: 'Open Octavia' })).closest('li')!)
  expect(card.getByRole('button', { name: 'Refuel Octavia' })).toBeInTheDocument()
  expect(card.getByRole('button', { name: 'Expense for Octavia' })).toBeInTheDocument()
  expect(card.getByRole('button', { name: 'Mark Tyres of Octavia as done' })).toBeInTheDocument()
  await check(view.container)
  view.unmount()

  const phone = setup('/', 'phone')
  await screen.findByRole('link', { name: 'Open Octavia' })
  await check(phone.view.container)
})

it('the dialogs opened from a home card are labelled, described and free of violations', async () => {
  const { ui } = setup('/')
  const card = within((await screen.findByRole('link', { name: 'Open Octavia' })).closest('li')!)

  await ui.click(card.getByRole('button', { name: 'Refuel Octavia' }))
  let dialog = await screen.findByRole('dialog', { name: 'Add refuelling' })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))
  expect(dialog).toHaveAccessibleDescription(/Enter what you filled up/)
  await check(document.body)
  await ui.keyboard('{Escape}')
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

  await ui.click(card.getByRole('button', { name: 'Expense for Octavia' }))
  dialog = await screen.findByRole('dialog', { name: 'Add expense' })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))
  await check(document.body)
  await ui.keyboard('{Escape}')
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

  await ui.click(card.getByRole('button', { name: 'Mark Tyres of Octavia as done' }))
  dialog = await screen.findByRole('dialog', { name: 'Mark as done: Tyres' })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))
  await check(document.body)
})
