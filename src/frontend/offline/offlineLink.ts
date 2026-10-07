import { ApolloLink } from '@apollo/client'
import { tap, throwError } from 'rxjs'
import { connectivity } from './connectivity.ts'
import { classifyFailure, OfflineError } from './errors.ts'

/**
 * Tells `connectivity` how every GraphQL request went, and sends nothing while the server is known to be unreachable: the operation
 * fails at once with an `OfflineError` (what the page already showed stays). The reachability probe passes `context: { offline:
 * 'bypass' }` to get through.
 */
export function createOfflineLink(): ApolloLink {
  return new ApolloLink((operation, forward) => {
    if (operation.getContext().offline !== 'bypass' && !connectivity.reachable) return throwError(() => new OfflineError())
    return forward(operation).pipe(
      tap({
        next: () => connectivity.succeeded(),
        error: (error: unknown) => {
          if (classifyFailure(error) !== 'answered') connectivity.failed()
        },
      }),
    )
  })
}
