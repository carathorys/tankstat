import { ApolloClient, ApolloLink, HttpLink, InMemoryCache } from '@apollo/client'
import { CombinedGraphQLErrors } from '@apollo/client/errors'
import { ErrorLink } from '@apollo/client/link/error'
import { from, mergeMap, of } from 'rxjs'
import { refreshSession } from './auth/refresh.ts'
import { OfflineError } from './offline/errors.ts'
import { createOfflineLink } from './offline/offlineLink.ts'

/**
 * Writes what went wrong in a request to the browser console, for whoever has to find out why: the screen shows the user a message and
 * the console is the only place that keeps the cause. Errors of the application's own kind carry a `key` that the UI translates ("vehicle
 * not found", "wrong password"); they are expected and already shown, so they are left out. A network failure, an HTTP error or an error
 * without a key is not. Nothing leaves the browser, and the variables of the request (a password) are never printed.
 */
export function reportOperationError(error: unknown, operationName: string | undefined): void {
  if (error instanceof OfflineError) return // not sent while the server is out of reach: expected, and the screen says so
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

/** The modes whose devices hold a refresh cookie. */
const REFRESHING_MODES = new Set(['STANDALONE', 'OIDC'])

/** The refresh, told apart: signed in again, has to sign in, or the request did not get through (that is not "signed out"). */
const refresh = () => refreshSession().then((signedIn) => (signedIn ? 'in' : 'out'), () => 'failed' as const)

/**
 * The access cookie lasts a few minutes. When an operation is answered "sign in first", the refresh cookie buys a new one
 * (`refreshSession`) and the operation is sent once more; when the device has to sign in again, the original answer stands and the
 * session is asked again, so the sign-in screen appears. A refresh that did not get through leaves the answer as it is.
 *
 * The session itself is public: with the access cookie gone (the app opened again after a while) it says nobody is signed in rather than
 * "sign in first". In the modes with a refresh cookie that answer is checked once with a refresh before it stands, so whoever reads the
 * session (the app, the offline link) only ever sees the answer after it.
 */
export const refreshLink = new ApolloLink((operation, forward) => {
  const name = operation.operationName ?? ''
  if (name === 'Session') {
    return forward(operation).pipe(
      mergeMap((result) => {
        const session = (result.data as { session?: { mode?: string; user?: unknown } } | null | undefined)?.session
        if (result.errors?.length || session?.user !== null || !REFRESHING_MODES.has(session.mode ?? '')) return of(result)
        return from(refresh()).pipe(mergeMap((outcome) => (outcome === 'in' ? forward(operation) : of(result))))
      }),
    )
  }
  if (SIGN_IN_OPERATIONS.has(name)) return forward(operation)
  return forward(operation).pipe(
    mergeMap((result) => {
      if (!result.errors?.some((e) => e.extensions?.code === 'UNAUTHENTICATED')) return of(result)
      return from(refresh()).pipe(
        mergeMap((outcome) => {
          if (outcome === 'in') return forward(operation)
          if (outcome === 'out') void operation.client.refetchQueries({ include: ['Session'] }).catch(() => undefined)
          return of(result)
        }),
      )
    }),
  )
})

export function createApolloClient(uri = '/graphql') {
  const errors = new ErrorLink(({ error, operation }) => reportOperationError(error, operation.operationName))
  return new ApolloClient({ link: ApolloLink.from([errors, createOfflineLink(), refreshLink, new HttpLink({ uri })]), cache: new InMemoryCache() })
}
