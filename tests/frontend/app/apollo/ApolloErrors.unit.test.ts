import { CombinedGraphQLErrors } from '@apollo/client/errors'
import { beforeEach, expect, it } from 'vitest'
import { reportOperationError } from '../../../../src/frontend/apolloClient.ts'
import { silenceConsoleError } from '../../support/mocks.tsx'

let consoleError: ReturnType<typeof silenceConsoleError>
beforeEach(() => {
  consoleError = silenceConsoleError()
})

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
