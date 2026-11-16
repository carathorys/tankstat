import { CombinedGraphQLErrors, ServerError, ServerParseError } from '@apollo/client/errors'
import { ApiError } from '../pictures/ApiError.ts'

/**
 * Why the device could not answer while the server is out of reach: nothing of it is kept here yet (`notLoaded`), or it is a screen that
 * needs the server (`onlineOnly`). Without a reason: a change, which needs the server.
 */
export type OfflineReason = 'notLoaded' | 'onlineOnly'

/** A request that was not sent because the server is known to be unreachable (see `connectivity`); the screen says so calmly. */
export class OfflineError extends Error {
  readonly reason?: OfflineReason
  constructor(reason?: OfflineReason) {
    super('The server cannot be reached.')
    this.name = 'OfflineError'
    this.reason = reason
  }
}

/** What a failed request says about the server: not reached at all, reached but behind a gateway that cannot reach it, or it answered. */
export type FailureKind = 'network' | 'gateway' | 'answered'

/** A proxy in front of the app answers these when the app itself is down: as good as unreachable. */
const GATEWAY = new Set([502, 503, 504])

export function classifyFailure(error: unknown): FailureKind {
  if (error instanceof OfflineError) return 'answered' // nothing was sent: it says nothing new
  if (error instanceof TypeError) return 'network' // fetch rejects with a TypeError when nothing answered
  if ((error as { name?: unknown } | null)?.name === 'TimeoutError') return 'network' // nothing answered in time (`timedFetch`)
  if (ServerParseError.is(error)) return 'network' // a page that is not our API (a captive portal, a login wall)
  if (ServerError.is(error) && GATEWAY.has(error.statusCode)) return 'gateway'
  if (error instanceof ApiError && GATEWAY.has(error.status)) return 'gateway'
  return 'answered'
}

/** The key of the server's "busy" answer (#335): a run holds what the save needs. */
export const BUSY_KEY = 'import.busy'

/**
 * The server answered that it is busy with a run (#335, the owner's decision 2): the save is to be tried again later. It is no rule's
 * refusal, so a change kept on the device stays kept, and no sign of a server out of reach either: it answered.
 */
export function isBusy(error: unknown): boolean {
  if (error instanceof ApiError) return error.key === BUSY_KEY
  return CombinedGraphQLErrors.is(error) && error.errors.some((e) => e.extensions?.key === BUSY_KEY)
}

export const isConnectionFailure = (error: unknown): boolean => error instanceof OfflineError || classifyFailure(error) !== 'answered'
