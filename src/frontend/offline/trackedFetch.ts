import { connectivity } from './connectivity.ts'
import { classifyFailure, OfflineError } from './errors.ts'

/**
 * `fetch` for the REST endpoints that tells `connectivity` what happened. While the server is known to be unreachable nothing is sent
 * (a request to a server that is down can hang for a long time); the caller gets an `OfflineError` at once.
 */
export async function trackedFetch(input: string, init?: RequestInit): Promise<Response> {
  if (!connectivity.reachable) throw new OfflineError()
  let response: Response
  try {
    response = await fetch(input, init)
  } catch (error) {
    if (classifyFailure(error) !== 'answered') connectivity.failed()
    throw error
  }
  if ([502, 503, 504].includes(response.status)) connectivity.failed()
  else connectivity.succeeded()
  return response
}
