import { ApolloLink } from '@apollo/client'
import { getMainDefinition, print } from '@apollo/client/utilities'
import type { DocumentNode } from 'graphql'
import { catchError, from, mergeMap, of, tap, throwError, type Observable } from 'rxjs'
import { connectivity } from './connectivity.ts'
import { ANONYMOUS_USER, deviceData } from './deviceData.ts'
import { classifyFailure, OfflineError } from './errors.ts'
import { policyFor, snapshotKey } from './snapshotPolicy.ts'

/** Signing in or out: what comes back afterwards may be another account's, so nothing is kept until `Session` says whose it is. */
const SIGN_IN_MUTATIONS = new Set(['Login', 'Logout', 'ResetPassword'])

interface SessionAnswer {
  session?: { mode?: string; user?: { id?: string } | null }
}

const documentIds = new WeakMap<DocumentNode, string>()

/**
 * A short fingerprint of a query's document (FNV-1a of its printed text): an answer kept for another version of the query (an older build,
 * before a field was added) is not answered from, since it may lack what the screen now reads.
 */
export function documentId(query: DocumentNode): string {
  let id = documentIds.get(query)
  if (id === undefined) {
    let hash = 0x811c9dc5
    for (const char of print(query)) hash = Math.imul(hash ^ char.charCodeAt(0), 0x01000193)
    id = (hash >>> 0).toString(36)
    documentIds.set(query, id)
  }
  return id
}

/** Whose data a `Session` answer is: the user's, the anonymous user's when sign-in is off, or nobody's. */
export function userOf(data: unknown): string | null {
  const session = (data as SessionAnswer | null)?.session
  if (!session) return null
  if (session.user?.id) return session.user.id
  return session.mode === 'NONE' ? ANONYMOUS_USER : null
}

/**
 * Tells `connectivity` how every GraphQL request went, and keeps the last answer of the queries worth keeping on this device
 * (`snapshotPolicy.ts`, `deviceData.ts`). While the server is known to be unreachable nothing is sent: a kept query is answered from the
 * downloaded window when it can be (`localResolvers.ts`), else with its last answer; anything else fails at once with an `OfflineError`
 * saying why. A query that fails because the server just went out of
 * reach is answered the same way. The reachability probe passes `context: { offline: 'bypass' }` to get through.
 */
export function createOfflineLink(device = deviceData): ApolloLink {
  return new ApolloLink((operation, forward) => {
    const bypass = operation.getContext().offline === 'bypass'
    const definition = getMainDefinition(operation.query)
    const isQuery = definition.kind === 'OperationDefinition' && definition.operation === 'query'
    const name = operation.operationName
    const policy = isQuery ? policyFor(name) : 'never'
    const key = snapshotKey(name ?? '', operation.variables)
    const doc = policy === 'never' ? undefined : documentId(operation.query)

    const fromDevice = (): Observable<ApolloLink.Result> => {
      if (policy === 'onlineOnly') return throwError(() => new OfflineError('onlineOnly'))
      if (policy === 'never') return throwError(() => new OfflineError())
      // The downloaded window answers what it can (any page or order of a vehicle's logs); otherwise the last answer seen.
      // Loaded when first needed (not with the app's first paint); the service worker keeps it for offline starts.
      const answer = async () => {
        const { answerLocally } = await import('./localResolvers.ts')
        return (await answerLocally(await device.rows(), name, operation.variables)) ?? (await device.read(key, doc))?.data
      }
      return from(answer()).pipe(
        // Nobody's session kept (signed out, or never signed in here): the sign-in screen needs the server, nothing is "not loaded".
        mergeMap((data) => (data !== undefined ? of({ data } as ApolloLink.Result) : throwError(() => new OfflineError(name === 'Session' ? undefined : 'notLoaded')))),
      )
    }

    if (!bypass && !connectivity.reachable) return fromDevice()
    return forward(operation).pipe(
      tap({
        next: (result) => {
          connectivity.succeeded()
          if (result.errors?.length || result.data == null) return
          if (!isQuery) {
            if (name && SIGN_IN_MUTATIONS.has(name)) device.unconfirm()
            return
          }
          if (name === 'Session') {
            const data = result.data
            void device.signedIn(userOf(data)).then(() => device.keep(key, data, undefined, doc)).catch(() => undefined)
          } else if (policy === 'keep') {
            void device.keep(key, result.data, undefined, doc).catch(() => undefined)
          }
        },
        error: (error: unknown) => {
          if (classifyFailure(error) !== 'answered') connectivity.failed()
        },
      }),
      // The server went out of reach during this request: answer it like the next one would be (never for the probe itself).
      catchError((error: unknown) => (!bypass && classifyFailure(error) !== 'answered' && isQuery && policy !== 'never' ? fromDevice() : throwError(() => error))),
    )
  })
}
