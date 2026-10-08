import { ApolloLink } from '@apollo/client'
import { getMainDefinition } from '@apollo/client/utilities'
import { catchError, from, mergeMap, of, tap, throwError, type Observable } from 'rxjs'
import { connectivity } from './connectivity.ts'
import { ANONYMOUS_USER, deviceData } from './deviceData.ts'
import { classifyFailure, OfflineError } from './errors.ts'
import { outbox } from './outbox.ts'
import { policyFor, snapshotKey } from './snapshotPolicy.ts'

/** Signing in or out: what comes back afterwards may be another account's, so nothing is kept until `Session` says whose it is. */
const SIGN_IN_MUTATIONS = new Set(['Login', 'Logout', 'ResetPassword'])

interface SessionAnswer {
  session?: { mode?: string; user?: { id?: string } | null }
}

/** Whose data a `Session` answer is: the user's, the anonymous user's when sign-in is off, or nobody's. */
export function userOf(data: unknown): string | null {
  const session = (data as SessionAnswer | null)?.session
  if (!session) return null
  if (session.user?.id) return session.user.id
  return session.mode === 'NONE' ? ANONYMOUS_USER : null
}

/** The last answer the device kept of a query, for the device's own answers to build on (a vehicle's page, the home list). */
const keptBy = (device: typeof deviceData) => async (operationName: string, variables: Record<string, unknown>) =>
  (await device.read(snapshotKey(operationName, variables)))?.data as Record<string, unknown> | undefined

/**
 * Whether changes waiting on this device concern what a query asks: then the device answers it itself (when it holds the logs), so what
 * the user changed shows, in its place, until the server has it.
 */
function waitingFor(name: string | undefined, variables: Record<string, unknown>): boolean {
  if (outbox.changes.length === 0) return false
  switch (name) {
    case 'Refuelings':
    case 'Expenses':
    case 'LogDefaults':
    case 'ExpenseCategories':
    case 'RecurringExpenses':
    case 'ChartData':
      return outbox.vehicleIds().has(String(variables.vehicleId))
    case 'VehicleDetails':
    case 'VehicleCard':
    case 'VehicleDashboard':
      return outbox.vehicleIds().has(String(variables.id))
    case 'Welcome':
      return outbox.changes.some((c) => c.entity === 'vehicles' || c.entity === 'recurring')
    case 'RefuelingDetails':
    case 'ExpenseDetails':
      return outbox.changes.some((c) => c.targetId === variables.id || c.input?.expenseId === variables.id)
    case 'RefuelingTrash':
    case 'ExpenseTrash':
      return true
    default:
      return false
  }
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

    const fromDevice = (): Observable<ApolloLink.Result> => {
      if (policy === 'onlineOnly') return throwError(() => new OfflineError('onlineOnly'))
      if (policy === 'never') return throwError(() => new OfflineError())
      // The downloaded window answers what it can (any page or order of a vehicle's logs); otherwise the last answer seen.
      // Loaded when first needed (not with the app's first paint); the service worker keeps it for offline starts.
      const answer = async () => {
        const { answerLocally } = await import('./localResolvers.ts')
        return (await answerLocally(await device.rows(), name, operation.variables, keptBy(device))) ?? (await device.read(key))?.data
      }
      return from(answer()).pipe(
        // Nobody's session kept (signed out, or never signed in here): the sign-in screen needs the server, nothing is "not loaded".
        mergeMap((data) => (data !== undefined ? of({ data } as ApolloLink.Result) : throwError(() => new OfflineError(name === 'Session' ? undefined : 'notLoaded')))),
      )
    }

    const fromServer = (): Observable<ApolloLink.Result> =>
      forward(operation).pipe(
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
              void device.signedIn(userOf(data)).then(() => device.keep(key, data)).catch(() => undefined)
            } else if (policy === 'keep') {
              void device.keep(key, result.data).catch(() => undefined)
            }
          },
          error: (error: unknown) => {
            if (classifyFailure(error) !== 'answered') connectivity.failed()
          },
        }),
        // The server went out of reach during this request: answer it like the next one would be (never for the probe itself).
        catchError((error: unknown) => (!bypass && classifyFailure(error) !== 'answered' && isQuery && policy !== 'never' ? fromDevice() : throwError(() => error))),
      )

    if (!bypass && !connectivity.reachable) return fromDevice()
    if (!bypass && isQuery && waitingFor(name, operation.variables)) {
      const local = import('./localResolvers.ts').then(async ({ answerLocally }) => answerLocally(await device.rows(), name, operation.variables, keptBy(device)))
      return from(local).pipe(mergeMap((data) => (data !== undefined ? of({ data } as ApolloLink.Result) : fromServer())))
    }
    return fromServer()
  })
}
