import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { expect, it, onTestFinished, vi } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { ErrorBoundary } from '../../../src/frontend/ErrorBoundary.tsx'
import { SIGNED_OUT_KEY } from '../../../src/frontend/auth/oidc.ts'
import { navigation } from '../../../src/frontend/navigation.ts'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { platform } from '../../../src/frontend/pwa/platform.ts'
import { check, setup, setupAccessibilityTests } from '../support/accessibility.tsx'
import { server } from '../support/server.ts'
import { fakeLogBackend, fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, silenceConsoleError, stubViewport } from '../support/mocks.tsx'

// The pages, the shell and the sign-in screens. The log dialogs and photos are in AccessibilityLogs, the recurring expenses and the home
// cards in AccessibilityRecurring (split so the files run side by side).
setupAccessibilityTests()

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

it('the trash, account and administration pages have no violations', async () => {
  for (const route of ['/trash', '/trash?tab=refuelings', '/trash?tab=expenses', '/account', '/admin', '/sync']) {
    const { view } = setup(route)
    await screen.findByRole('main')
    await screen.findByRole('heading', { level: 1 })
    await check(view.container)
    view.unmount()
  }
})

it('the changes the server could not apply, and the confirmations to apply or discard one, are labelled and free of violations', async () => {
  const { ui } = setup('/sync')
  server.use(
    graphql.query('ParkedChanges', () =>
      HttpResponse.json({
        data: {
          parkedChanges: [{
            __typename: 'SyncChangeInfo', id: 'c1', kind: 'DELETE_REFUELING', status: 'PARKED', vehicleId: 'v1', targetId: 'r1', change: '{"id":"c1","expectedVersion":1,"deleteRefueling":"r1"}',
            receivedAt: '2026-10-06T09:00:00Z', reason: { __typename: 'SyncReasonInfo', key: 'sync.versionMismatch', args: [] },
            vehicle: { __typename: 'SyncVehicleRef', id: 'v1', name: 'Octavia', distanceUnit: 'KILOMETERS', volumeUnit: 'LITERS' },
            submittedBy: { __typename: 'UserRef', id: 'u2', displayName: 'Bob' }, canResolve: true,
          }],
        },
      }),
    ),
  )
  const region = await screen.findByRole('region', { name: 'Not applied' })
  await check(document.body)

  for (const [button, title] of [[/^Apply anyway: Refuelling to the trash/, 'Apply this change anyway?'], [/^Discard: Refuelling to the trash/, 'Discard this change?']] as const) {
    await ui.click(await within(region).findByRole('button', { name: button }))
    const dialog = await screen.findByRole('alertdialog', { name: title })
    expect(dialog).toHaveAccessibleDescription(/./)
    await check(document.body)
    await ui.click(within(dialog).getByRole('button', { name: 'Cancel' }))
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

it("the Offline data section, a vehicle's window dialog and the timespan fields are free of violations", async () => {
  const { view, ui } = setup('/account')
  server.use(
    graphql.query('OfflineVehicles', () => HttpResponse.json({ data: { myVehicles: [{ __typename: 'Vehicle', id: 'v1', name: 'Octavia' }] } })),
    graphql.query('OfflineEstimates', () =>
      HttpResponse.json({ data: { myVehicles: [{ __typename: 'Vehicle', id: 'v1', logCountSince: { __typename: 'LogCountSince', refuelings: 10, expenses: 2 } }] } }),
    ),
  )
  const offline = await screen.findByRole('region', { name: 'Offline data' })
  await within(offline).findByText('≈ 12')
  await check(view.container)

  await ui.click(within(offline).getByRole('button', { name: 'Change the window of Octavia' }))
  const dialog = await screen.findByRole('dialog', { name: 'Window of Octavia' })
  await check(document.body)

  await ui.click(within(dialog).getByRole('combobox', { name: 'What to download' }))
  await ui.click(screen.getByRole('option', { name: 'Since a date' }))
  await within(dialog).findByRole('group', { name: 'Since' }) // the date field (its picker loads lazily)
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

it('the appearance and language menus are labelled radio menus, free of violations', async () => {
  const { ui } = setup('/')
  await screen.findByText('Octavia')

  await ui.click(screen.getByRole('button', { name: 'Appearance' }))
  const modes = await screen.findByRole('menu', { name: 'Appearance' })
  // Two sets of radios, split by a separator; each item's name says its set.
  expect(within(modes).getAllByRole('menuitemradio').map((item) => [item.getAttribute('aria-label'), item.getAttribute('aria-checked')])).toEqual([
    ['Colour mode: Light', 'false'],
    ['Colour mode: Dark', 'true'],
    ['Colour mode: System', 'false'],
    ['Surfaces: Glossy', 'true'],
    ['Surfaces: Transparent', 'false'],
    ['Surfaces: Opaque', 'false'],
  ])
  expect(within(modes).getByRole('separator')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Appearance', hidden: true })).toHaveAttribute('aria-controls', modes.id)
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
