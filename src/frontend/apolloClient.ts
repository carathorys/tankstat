import { ApolloClient, ApolloLink, HttpLink, InMemoryCache } from '@apollo/client'
import { CombinedGraphQLErrors } from '@apollo/client/errors'
import { ErrorLink } from '@apollo/client/link/error'
import { from, mergeMap, of } from 'rxjs'
import { refreshSession } from './auth/refresh.ts'

/**
 * Writes what went wrong in a request to the browser console, for whoever has to find out why: the screen shows the user a message and
 * the console is the only place that keeps the cause. Errors of the application's own kind carry a `key` that the UI translates ("vehicle
 * not found", "wrong password"); they are expected and already shown, so they are left out. A network failure, an HTTP error or an error
 * without a key is not. Nothing leaves the browser, and the variables of the request (a password) are never printed.
 */
export function reportOperationError(error: unknown, operationName: string | undefined): void {
  const name = operationName ?? '(unnamed)'
  if (CombinedGraphQLErrors.is(error)) {
    const unexpected = error.errors.filter((e) => typeof e.extensions?.key !== 'string')
    if (unexpected.length > 0) {
      console.error(
        `GraphQL ${name} failed`,
        unexpected.map((e) => ({ message: e.message, code: e.extensions?.code, path: e.path })),
      )
    }
    return
  }
  console.error(`GraphQL ${name} could not be completed`, error)
}

/** Operations of someone signing in or out: an UNAUTHENTICATED answer to them is the answer (changing a password is not one: it refreshes). */
const SIGN_IN_OPERATIONS = new Set(['Session', 'Login', 'Logout', 'RequestPasswordReset', 'ResetPassword'])

/**
 * The access cookie lasts a few minutes. When an operation is answered "sign in first", the refresh cookie buys a new one
 * (`refreshSession`) and the operation is sent once more; when the device has to sign in again, the original answer stands and the
 * session is asked again, so the sign-in screen appears.
 */
export const refreshLink = new ApolloLink((operation, forward) => {
  if (SIGN_IN_OPERATIONS.has(operation.operationName ?? '')) return forward(operation)
  return forward(operation).pipe(
    mergeMap((result) => {
      if (!result.errors?.some((e) => e.extensions?.code === 'UNAUTHENTICATED')) return of(result)
      return from(refreshSession().catch(() => false)).pipe(
        mergeMap((signedIn) => {
          if (signedIn) return forward(operation)
          void operation.client.refetchQueries({ include: ['Session'] }).catch(() => undefined)
          return of(result)
        }),
      )
    }),
  )
})

export function createApolloClient(uri = '/graphql') {
  const errors = new ErrorLink(({ error, operation }) => reportOperationError(error, operation.operationName))
  return new ApolloClient({ link: ApolloLink.from([errors, refreshLink, new HttpLink({ uri })]), cache: new InMemoryCache() })
}
