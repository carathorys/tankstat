import { graphql, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'

// Handlers are registered per test with server.use(); unhandled requests fail the test. The one default: the bell in the top bar asks
// for the unread count on every screen (an empty inbox); notification tests override it with fakeNotificationBackend.
export const server = setupServer(graphql.query('UnreadNotificationCount', () => HttpResponse.json({ data: { notificationCount: 0 } })))
