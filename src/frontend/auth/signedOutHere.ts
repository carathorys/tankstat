import { connectivity } from '../offline/connectivity.ts'
import { deviceData } from '../offline/deviceData.ts'
import { PROBE_TIMEOUT_MS, withDeadline } from '../offline/runtime.ts'
import { signOutDevice } from './refresh.ts'

/**
 * Ends this device's session on the server, best effort: only while it can be reached, and never waiting longer than a probe would.
 * True when the server ended it (a 2xx), or it was gone already (401: a logout that landed after the deadline, say); false when it could
 * not be asked or did not do it. A gateway answering for an app that is down (502, 503, 504) is no answer of the server's.
 */
export async function endSessionOnServer(): Promise<boolean> {
  if (!connectivity.reachable) return false
  try {
    const response = await withDeadline(signOutDevice(), PROBE_TIMEOUT_MS)
    return response.ok || response.status === 401
  } catch {
    return false
  }
}

let telling: Promise<boolean> | null = null

/**
 * A sign-out the server has not heard of (`SignedOutHere`): told once it can be reached, and forgotten when it answered. True when none
 * is left, false while one still waits. Callers at the same moment share one request.
 */
export function tellLeftOverSignOut(): Promise<boolean> {
  telling ??= (async () => {
    if (!(await deviceData.signedOutHere())) return true
    if (!(await endSessionOnServer())) return false
    await deviceData.setSignedOutHere(null)
    return true
  })().finally(() => {
    telling = null
  })
  return telling
}

/**
 * Someone is signing in on this device: a sign-out left over is told once more, then forgotten whatever came of it, since the new sign-in's
 * cookies replace the old ones here (on the server, an old session nobody holds any more ends with its lifetime).
 */
export async function forgetLeftOverSignOut(): Promise<void> {
  await tellLeftOverSignOut()
  await deviceData.setSignedOutHere(null)
}
