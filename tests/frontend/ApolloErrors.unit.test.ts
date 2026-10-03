import { CombinedGraphQLErrors } from '@apollo/client/errors'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, beforeEach, expect, it, vi } from 'vitest'
import { createApolloClient, reportOperationError } from '../../src/frontend/apolloClient.ts'
import { HealthDocument } from '../../src/frontend/gql/generated.ts'
import { gqlError } from './mocks.tsx'
import { server } from './server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

let consoleError: ReturnType<typeof vi.spyOn>
beforeEach(() => {
  consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined)
})
afterEach(() => consoleError.mockRestore())

const keyed = new CombinedGraphQLErrors({ errors: [{ message: 'Vehicle 1 does not exist.', extensions: { code: 'NOT_FOUND', key: 'vehicle.notFound', args: {} } }] })
const unexpected = new CombinedGraphQLErrors({ errors: [{ message: 'Unexpected Execution Error', path: ['health'], extensions: { code: 'HC0001' } }] })

it('errors the application expects (they carry a key the screen translates) stay out of the console', () => {
  reportOperationError(keyed, 'Vehicle')

  expect(consoleError).not.toHaveBeenCalled()
})

it('an error without a key is reported with the operation, its code and where it happened', () => {
  reportOperationError(unexpected, 'Health')

  expect(consoleError).toHaveBeenCalledWith('GraphQL Health failed', [{ message: 'Unexpected Execution Error', code: 'HC0001', path: ['health'] }])
})

it('only the unexpected ones of a mixed answer are reported', () => {
  const mixed = new CombinedGraphQLErrors({
    errors: [
      { message: 'Not yours', extensions: { code: 'FORBIDDEN', key: 'chart.notYours' } },
      { message: 'Unexpected Execution Error', extensions: { code: 'HC0001' } },
    ],
  })

  reportOperationError(mixed, 'SaveChart')

  const [, reported] = consoleError.mock.calls[0] as [string, { message: string }[]]
  expect(reported.map((e) => e.message)).toEqual(['Unexpected Execution Error'])
})

it('a request that never got an answer is reported as one that could not be completed', () => {
  const failure = new TypeError('Failed to fetch')

  reportOperationError(failure, undefined)

  expect(consoleError).toHaveBeenCalledWith('GraphQL (unnamed) could not be completed', failure)
})

it('the client reports a failed request through its error link, and only an unexpected one', async () => {
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
