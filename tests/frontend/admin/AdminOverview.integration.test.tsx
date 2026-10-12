import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { server } from '../support/server.ts'
import { fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, user } from '../support/mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

/** The server's answer to the overview: by default nothing to act on, three users (one administrator, one disabled) and two vehicles. */
function overviewServer({ restartPending = false, ignored = false, databaseReachable = true, exists = true } = {}) {
  const calls: string[] = []
  server.use(
    sessionHandler('STANDALONE', () => user({ isAdmin: true })),
    healthHandler,
    ...fakeVehicleBackend().handlers,
    graphql.query('AdminOverview', () => {
      calls.push('AdminOverview')
      return HttpResponse.json({
        data: {
          systemSettings: { __typename: 'SystemSettingsInfo', restartPending, file: { __typename: 'SettingsFileInfo', path: '/data/settings.json', exists, ignored } },
          users: [
            { __typename: 'UserAccount', id: 'u1', isAdmin: true, isDisabled: false },
            { __typename: 'UserAccount', id: 'u2', isAdmin: false, isDisabled: true },
            { __typename: 'UserAccount', id: 'u3', isAdmin: false, isDisabled: false },
          ],
          vehicleCount: 2,
          health: { __typename: 'HealthReport', version: '1.2.3', databaseReachable },
        },
      })
    }),
  )
  return calls
}

const overview = async () => within(await screen.findByRole('region', { name: 'Administration' }))

it('says in one line that nothing needs attention, and gives the facts with links to the pages behind them', async () => {
  overviewServer()
  renderWithApollo(<App />, '/admin')

  const view = await overview()
  expect(await view.findByText('Nothing needs your attention.')).toBeInTheDocument()
  expect(view.getByText('1.2.3')).toBeInTheDocument()
  expect(view.getByText('Reachable')).toBeInTheDocument()
  expect(view.getByRole('link', { name: '3 users (administrators: 1, disabled: 1)' })).toHaveAttribute('href', '/admin/people') // reads right whatever each count is
  expect(view.getByRole('link', { name: '2 vehicles' })).toHaveAttribute('href', '/admin/vehicles')
  expect(view.getByText('/data/settings.json')).toBeInTheDocument()
})

it('lists what needs attention, each with where to act on it', async () => {
  const ui = userEvent.setup()
  overviewServer({ restartPending: true, ignored: true })
  renderWithApollo(<App />, '/admin')

  const view = await overview()
  const items = within(await view.findByRole('list')).getAllByRole('listitem')
  expect(items.map((li) => li.textContent)).toEqual([
    'The settings file was left out at the last start: the server runs without it. Go to Server settings',
    'Saved settings wait for a restart of the server. Go to Server settings',
  ])
  expect(view.queryByText('Nothing needs your attention.')).not.toBeInTheDocument()

  await ui.click(within(items[1]).getByRole('link', { name: 'Go to Server settings' }))
  expect(await screen.findByRole('heading', { level: 1, name: 'Server settings' })).toBeInTheDocument()
})

it('asks the server every time it is opened, never answering from what it kept', async () => {
  const calls = overviewServer()
  const first = renderWithApollo(<App />, '/admin')
  await (await overview()).findByText('Nothing needs your attention.')
  first.unmount()

  renderWithApollo(<App />, '/admin')
  await (await overview()).findByText('Nothing needs your attention.')
  expect(calls).toHaveLength(2)
})

it("the old administration page's tabs lead to where they went, and the old vehicle list to its new address with its query", async () => {
  overviewServer()
  server.use(graphql.query('Admin', () => HttpResponse.json({ data: { users: [], canSetUserPasswords: false, accessSettings: { defaultLevelForOthers: 'NONE' }, accessGrants: [] } })))

  const access = renderWithApollo(<App />, '/admin?tab=access')
  expect(await screen.findByRole('heading', { level: 1, name: 'People' })).toBeInTheDocument()
  expect(screen.getByRole('tab', { name: 'Access' })).toHaveAttribute('aria-selected', 'true')
  access.unmount()

  const users = renderWithApollo(<App />, '/admin?tab=users')
  expect(await screen.findByRole('heading', { level: 1, name: 'People' })).toBeInTheDocument()
  expect(screen.getByRole('tab', { name: 'Users' })).toHaveAttribute('aria-selected', 'true')
  users.unmount()

  renderWithApollo(<App />, '/vehicles')
  expect(await screen.findByRole('heading', { level: 1, name: 'Vehicles' })).toBeInTheDocument()
})

it('is for administrators only', async () => {
  server.use(sessionHandler('STANDALONE', () => user()), healthHandler, ...fakeVehicleBackend().handlers)
  renderWithApollo(<App />, '/admin')

  await screen.findByRole('heading', { name: 'Your vehicles' }) // bounced back to the home page
  expect(screen.queryByRole('region', { name: 'Administration' })).not.toBeInTheDocument()
})
