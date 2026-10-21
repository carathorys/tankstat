import { connectivity, PROBE_TIMEOUT_MS } from '../offline/connectivity.ts'
import { deviceData } from '../offline/deviceData.ts'
import { signOutDevice } from './refresh.ts'

/** How long a sign-out left over waits to be told again when the server answered but did not end the session (a 500, say). */
export const RETRY_MS = 30_000

/**
 * Ends this device's session on the server, best effort: only while it can be reached, and never waiting longer than a probe would. The
 * request is given up at the deadline, never left running: a late answer clears the cookies by name, a new sign-in's too; a timeout counts
 * as no answer, so the server is taken as out of reach. True when the server ended it (a 2xx), or has no sessions to end (404: sign-in off
 * or behind a proxy, whose mode change ended them all); false when it could not be asked or did not do it. A gateway answering for an app
 * that is down (502, 503, 504) is no answer of the server's.
 */
export async function endSessionOnServer(): Promise<boolean> {
  if (!connectivity.reachable) return false
  try {
    const signal = typeof AbortSignal.timeout === 'function' ? AbortSignal.timeout(PROBE_TIMEOUT_MS) : undefined
    const response = await signOutDevice(signal)
    return response.ok || response.status === 404
  } catch {
    return false
  }
}

let telling: Promise<boolean> | null = null
let retry: ReturnType<typeof setTimeout> | null = null

/**
 * A sign-out the server has not heard of (`SignedOutHere`): told once it can be reached, and forgotten when it answered. True when none
 * is left, false while one still waits; rejects when the device cannot read whether one is left. Callers at the same moment share one
 * request. When the server answered without ending the session, it is told again after `RETRY_MS`: nothing else asks on the sign-in
 * screen (out of reach, the runtime asks the `Session` again once it is back, and that tells it).
 */
export function tellLeftOverSignOut(device: typeof deviceData = deviceData): Promise<boolean> {
  telling ??= (async () => {
    if (!(await device.signedOutHere())) return true
    if (!(await endSessionOnServer())) {
      if (connectivity.reachable)
        retry ??= setTimeout(() => {
          retry = null
          void tellLeftOverSignOut(device).catch(() => undefined)
        }, RETRY_MS)
      return false
    }
    await device.setSignedOutHere(null)
    return true
  })().finally(() => {
    telling = null
  })
  return telling
}

/** Tests: no sign-out being told, and none waiting to be told again. */
export function resetSignedOutHere(): void {
  if (retry) clearTimeout(retry)
  retry = null
  telling = null
}
