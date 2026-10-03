import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import { createApolloClient } from '../../src/frontend/apolloClient.ts'
import { HealthDocument } from '../../src/frontend/gql/generated.ts'
import { gqlError, silenceConsoleError } from './mocks.tsx'
import { server } from './server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

it('the client reports a failed request through its error link, and only an unexpected one', async () => {
  const consoleError = silenceConsoleError()
  const client = createApolloClient('http://localhost/graphql')
  server.use(graphql.query('Health', () => HttpResponse.json(gqlError('Vehicle 1 does not exist.', 'NOT_FOUND', 'vehicle.notFound'))))

  await expect(client.query({ query: HealthDocument, fetchPolicy: 'network-only' })).rejects.toBeDefined()
  expect(consoleError).not.toHaveBeenCalled()

  server.use(graphql.query('Health', () => HttpResponse.json({ errors: [{ message: 'Unexpected Execution Error' }] })))
  await expect(client.query({ query: HealthDocument, fetchPolicy: 'network-only' })).rejects.toBeDefined()
  expect(consoleError).toHaveBeenCalledTimes(1)
  expect(consoleError.mock.calls[0][0]).toBe('GraphQL Health failed')

  server.use(graphql.query('Health', () => new HttpResponse(null, { status: 500 })))
  await expect(client.query({ query: HealthDocument, fetchPolicy: 'network-only' })).rejects.toBeDefined()
  expect(consoleError).toHaveBeenCalledTimes(2)
  expect(consoleError.mock.calls[1][0]).toBe('GraphQL Health could not be completed')
})
