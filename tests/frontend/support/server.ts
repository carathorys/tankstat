import { graphql, http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'

// Handlers are registered per test with server.use(); unhandled requests fail the test. The defaults: the bell in the top bar asks for
// the unread count on every screen (an empty inbox; notification tests override it with fakeNotificationBackend), the add dialogs ask
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
  graphql.query('UnreadNotificationCount', () => HttpResponse.json({ data: { notificationCount: 0 } })),
  graphql.query('RecognitionStatus', () => HttpResponse.json({ data: { recognitionStatus: { available: false } } })),
  graphql.query('UiSettings', () => HttpResponse.json({ data: { uiSettings: { navOpen: null, language: null, colorMode: null, grids: [] } } })),
  graphql.mutation('UpdateUiSettings', ({ variables }) =>
    HttpResponse.json({
      data: { updateUiSettings: { navOpen: variables.input.navOpen ?? null, language: variables.input.language ?? null, colorMode: variables.input.colorMode ?? null } },
    }),
  ),
  graphql.mutation('SaveGridSettings', ({ variables }) => HttpResponse.json({ data: { saveGridSettings: variables.input } })),
  graphql.mutation('ResetGridSettings', () => HttpResponse.json({ data: { resetGridSettings: true } })),
)
