import { ServerError, ServerParseError } from '@apollo/client/errors'
import { ApiError } from '../pictures/ApiError.ts'

/** A request that was not sent because the server is known to be unreachable (see `connectivity`); the screen says so calmly. */
export class OfflineError extends Error {
  constructor() {
    super('The server cannot be reached.')
    this.name = 'OfflineError'
  }
}

/** What a failed request says about the server: not reached at all, reached but behind a gateway that cannot reach it, or it answered. */
export type FailureKind = 'network' | 'gateway' | 'answered'

/** A proxy in front of the app answers these when the app itself is down: as good as unreachable. */
const GATEWAY = new Set([502, 503, 504])

export function classifyFailure(error: unknown): FailureKind {
  if (error instanceof OfflineError) return 'answered' // nothing was sent: it says nothing new
  if (error instanceof TypeError) return 'network' // fetch rejects with a TypeError when nothing answered
  if (ServerParseError.is(error)) return 'network' // a page that is not our API (a captive portal, a login wall)
  if (ServerError.is(error) && GATEWAY.has(error.statusCode)) return 'gateway'
  if (error instanceof ApiError && GATEWAY.has(error.status)) return 'gateway'
  return 'answered'
}

export const isConnectionFailure = (error: unknown): boolean => error instanceof OfflineError || classifyFailure(error) !== 'answered'
