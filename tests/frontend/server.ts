import { graphql, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'

// Handlers are registered per test with server.use(); unhandled requests fail the test. The defaults: the bell in the top bar asks for
// the unread count on every screen (an empty inbox; notification tests override it with fakeNotificationBackend), and the add dialogs ask
// whether photos are read on the server (they are not; photo reading tests override it with fakeRecognition).
export const server = setupServer(
  graphql.query('UnreadNotificationCount', () => HttpResponse.json({ data: { notificationCount: 0 } })),
  graphql.query('RecognitionStatus', () => HttpResponse.json({ data: { recognitionStatus: { available: false } } })),
)
