import { connectivity } from '../offline/connectivity.ts'
import { deviceData } from '../offline/deviceData.ts'
import { PROBE_TIMEOUT_MS, withDeadline } from '../offline/runtime.ts'
import { signOutDevice } from './refresh.ts'

/**
 * Ends this device's session on the server, best effort: only while it can be reached, and never waiting longer than a probe would.
 * True when the server answered (whatever it said: a session already gone is ended too); false when it could not be asked.
 */
export async function endSessionOnServer(): Promise<boolean> {
  if (!connectivity.reachable) return false
  try {
    await withDeadline(signOutDevice(), PROBE_TIMEOUT_MS)
    return true
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
