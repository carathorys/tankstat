import userEvent from '@testing-library/user-event'
import { HttpResponse } from 'msw'
import { graphql } from './graphql.ts'
import { axe } from 'vitest-axe'
import { afterAll, afterEach, beforeAll, expect, vi } from 'vitest'
import App from '../../src/App.tsx'
import { server } from './server.ts'
import { fakeExpense, fakeExpenseBackend, fakeLogBackend, fakeNotification, fakeNotificationBackend, fakePhotoStore, fakeRecurring, fakeRecurringBackend, fakeRefueling, fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport, user } from './mocks.tsx'

/**
 * What the accessibility tests (`tests/app/Accessibility*.integration.test.tsx`) share. They are split into several files so
 * they run side by side: each scan of a whole page takes a while, and the tests of one file run one after another.
 */

/** The test server for the file, and object URLs for the previews of queued photos (jsdom has none). Call once at the top of a file. */
export function setupAccessibilityTests() {
  beforeAll(() => {
    server.listen({ onUnhandledFrame: 'error' })
    let n = 0
    URL.createObjectURL = () => `blob:preview-${n++}`
    URL.revokeObjectURL = () => undefined
  })
  afterEach(() => {
    server.resetHandlers()
    vi.unstubAllGlobals()
  })
  afterAll(() => server.close())
  // Each scan of a whole page takes a second or more, and several times that on CI (coverage on, the CPU shared with other test files):
  // a test with a few scans needs more than the default 15 s there.
  vi.setConfig({ testTimeout: 30_000 })
}

/** Colour contrast cannot be computed without a real renderer; everything else axe knows is checked. */
export const check = async (container: HTMLElement) => {
  const results = await axe(container, { rules: { 'color-contrast': { enabled: false } } })
  expect(results.violations.map((v) => `${v.id}: ${v.help} (${v.nodes.map((n) => n.target.join(' ')).join(', ')})`)).toEqual([])
}

/** The app at `route`, signed in as an administrator, with a vehicle that has logs, expenses, schedules and notifications. */
export function setup(route: string, viewport: 'desktop' | 'phone' = 'desktop', photos = fakePhotoStore()) {
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
    graphql.query('AdminOverview', () =>
      HttpResponse.json({
        data: {
          systemSettings: {
            __typename: 'SystemSettingsInfo',
            restartPending: true,
            file: { __typename: 'SettingsFileInfo', path: '/data/settings.json', exists: true, ignored: false },
            folders: [{ __typename: 'ServerFolder', kind: 'PICTURES', path: '/data/uploads', setting: 'Storage:Path', variable: 'Storage__Path', sameAsPictures: false }],
          },
          users: [
            { __typename: 'UserAccount', id: 'u1', isAdmin: true, isDisabled: false },
            { __typename: 'UserAccount', id: 'u2', isAdmin: false, isDisabled: false },
          ],
          vehicleCount: 2,
          health: { __typename: 'HealthReport', version: '1.2.3', databaseReachable: true },
        },
      }),
    ),
    graphql.query('SystemSettings', () =>
      HttpResponse.json({
        data: {
          systemSettings: {
            __typename: 'SystemSettingsInfo',
            file: { __typename: 'SettingsFileInfo', path: '/data/settings.json', exists: true, ignored: true, problems: ['Smtp:Port must be a whole number.'] },
            restartPending: true,
            groups: [
              {
                __typename: 'SystemSettingsGroup',
                name: 'AUTH', appliesAtOnce: false,
                settings: [
                  { __typename: 'SystemSetting', key: 'Auth:Mode', kind: 'CHOICE', choices: ['None', 'Standalone', 'Oidc', 'ProxyHeader'], value: 'Standalone', isSet: true, isSecret: false, source: 'ENVIRONMENT', editable: false, restartPending: false, defaultValue: 'None', appliesAtOnce: false },
                  { __typename: 'SystemSetting', key: 'Auth:PublicUrl', kind: 'TEXT', choices: [], value: null, isSet: false, isSecret: false, source: 'DEFAULT', editable: true, restartPending: false, defaultValue: null, appliesAtOnce: false },
                  { __typename: 'SystemSetting', key: 'Auth:AdminEmails', kind: 'LIST', choices: [], value: 'a@example.com', isSet: true, isSecret: false, source: 'FILE', editable: true, restartPending: true, defaultValue: null, appliesAtOnce: false },
                  { __typename: 'SystemSetting', key: 'Auth:Standalone:PasswordMinLength', kind: 'INTEGER', choices: [], value: null, isSet: false, isSecret: false, source: 'DEFAULT', editable: true, restartPending: false, defaultValue: '10', appliesAtOnce: true },
                  { __typename: 'SystemSetting', key: 'Auth:Oidc:ClientSecret', kind: 'SECRET', choices: [], value: null, isSet: true, isSecret: true, source: 'FILE', editable: true, restartPending: false, defaultValue: null, appliesAtOnce: false },
                  { __typename: 'SystemSetting', key: 'Auth:Standalone:AllowAdminSetPassword', kind: 'BOOLEAN', choices: [], value: null, isSet: false, isSecret: false, source: 'DEFAULT', editable: true, restartPending: false, defaultValue: 'false', appliesAtOnce: false },
                  { __typename: 'SystemSetting', key: 'Auth:AccessTokenMinutes', kind: 'INTEGER', choices: [], value: null, isSet: false, isSecret: false, source: 'DEFAULT', editable: true, restartPending: false, defaultValue: '15', appliesAtOnce: false },
                ],
              },
              {
                __typename: 'SystemSettingsGroup',
                name: 'LOGGING', appliesAtOnce: true,
                settings: [{ __typename: 'SystemSetting', key: 'Logging:LogLevel:Default', kind: 'CHOICE', choices: ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical', 'None'], value: 'Information', isSet: true, isSecret: false, source: 'SHIPPED', editable: true, restartPending: false, defaultValue: 'Information', appliesAtOnce: false }],
              },
            ],
          },
        },
      }),
    ),
  )
  const view = renderWithApollo(<App />, route)
  return { view, ui: userEvent.setup() }
}
