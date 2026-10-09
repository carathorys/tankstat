import { graphql, http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'

// Handlers are registered per test with server.use(); unhandled requests fail the test. The defaults: the bell in the top bar asks for
// the unread count on every screen (an empty inbox; notification tests override it with fakeNotificationBackend), the top bar and the
// navigation ask for the changes the server could not apply (none; sync tests answer ParkedChanges themselves), the add dialogs ask
// whether photos are read on the server (they are not; photo reading tests override it with fakeRecognition), and the app asks for the
// user's UI settings once and tells the server about every change (nothing saved, every save echoed; settings tests use
// fakeSettingsBackend). The echoes answer exactly the fields the documents select, or Apollo warns about missing fields in every test.
// A test that opens a vehicle page only to check something else (a card's link, a trash from the Details tab) gets an empty dashboard
// and no schedules; dashboard and recurring tests answer them with their own fakes.
export const server = setupServer(
  graphql.query('VehicleDashboard', ({ variables }) =>
    HttpResponse.json({ data: { vehicle: { __typename: 'Vehicle', id: variables.id, summary: null }, vehicleCharts: [] } }),
  ),
  graphql.query('RecurringExpenses', ({ variables }) =>
    HttpResponse.json({ data: { vehicle: { __typename: 'Vehicle', id: variables.vehicleId, recurring: [] } } }),
  ),
  // The token endpoints (auth/refresh.ts): no test has a refresh cookie, so a refresh says "sign in"; signing out always works.
  http.post('/auth/token/refresh', () => HttpResponse.json({ key: 'auth.unauthenticated' }, { status: 401 })),
  http.post('/auth/token/logout', () => new HttpResponse(null, { status: 204 })),
  // The Account page lists the devices the user is signed in on (Standalone and OIDC): by default only this one.
  graphql.query('MySessions', () =>
    HttpResponse.json({
      data: {
        mySessions: [
          { id: 's-this', client: 'Firefox on Linux', createdAt: '2026-09-01T08:00:00Z', lastUsedAt: '2026-10-01T08:00:00Z', expiresAt: '2026-12-30T08:00:00Z', current: true },
        ],
      },
    }),
  ),
  graphql.query('UnreadNotificationCount', () => HttpResponse.json({ data: { notificationCount: 0 } })),
  graphql.query('ParkedChanges', () => HttpResponse.json({ data: { parkedChanges: [] } })),
  graphql.query('RecognitionStatus', () => HttpResponse.json({ data: { recognitionStatus: { available: false } } })),
  graphql.query('UiSettings', () => HttpResponse.json({ data: { uiSettings: { navOpen: null, language: null, colorMode: null, surface: null, grids: [] } } })),
  // The Account page's Offline data section: the default window, no vehicles of its own (tests with vehicles answer these themselves).
  graphql.query('OfflineSettings', () => HttpResponse.json({ data: { offlineSettings: { __typename: 'OfflineSettingsInfo', defaultWindow: 'span:P2M', vehicles: [] } } })),
  graphql.query('OfflineVehicles', () => HttpResponse.json({ data: { myVehicles: [] } })),
  graphql.query('OfflineEstimates', () => HttpResponse.json({ data: { myVehicles: [] } })),
  graphql.mutation('UpdateOfflineSettings', ({ variables }) =>
    HttpResponse.json({ data: { updateOfflineSettings: { __typename: 'OfflineSettingsInfo', ...variables.input, vehicles: variables.input.vehicles.map((v: object) => ({ __typename: 'OfflineVehicleWindow', ...v })) } } }),
  ),
  graphql.mutation('UpdateUiSettings', ({ variables }) =>
    HttpResponse.json({
      data: { updateUiSettings: { navOpen: variables.input.navOpen ?? null, language: variables.input.language ?? null, colorMode: variables.input.colorMode ?? null, surface: variables.input.surface ?? null } },
    }),
  ),
  graphql.mutation('SaveGridSettings', ({ variables }) => HttpResponse.json({ data: { saveGridSettings: variables.input } })),
  graphql.mutation('ResetGridSettings', () => HttpResponse.json({ data: { resetGridSettings: true } })),
)
