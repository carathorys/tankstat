import { useApolloClient } from '@apollo/client/react'
import { useState } from 'react'
import type { AuthMode } from '../gql/generated.ts'
import { deviceData } from '../offline/deviceData.ts'
import { outbox } from '../offline/outbox.ts'
import { signedOut } from './oidc.ts'
import { endSessionOnServer } from './signedOutHere.ts'

/**
 * Signs this device out, whether the server can be reached or not (#162): its session ends on the server when it can (best effort, also
 * after the access cookie ran out), else the device remembers that it signed out (no secret: the cookies are HttpOnly, only the server can
 * clear them), so it shows the sign-in screen and tells the server once it is back. Then everything the device keeps for the account
 * goes, and the store starts afresh, which shows the sign-in screen. Shared by the navigation and the Account page's list of devices.
 */
export function useSignOut(mode: AuthMode): () => Promise<void> {
  const client = useApolloClient()
  return async () => {
    if (!(await endSessionOnServer())) await deviceData.setSignedOutHere({ mode, at: Date.now() })
    // Before the store resets: the sign-in screen that follows must wait for a click, not send the browser back to the provider.
    if (mode === 'OIDC') signedOut.mark()
    await deviceData.signOut()
    // Offline a query asked again may fail (the server's health is never the device's): each says so itself; the sign-out is done.
    await client.resetStore().catch(() => undefined)
  }
}

/** What only this device holds of the account, which a sign-out deletes: changes not sent yet, and rows set aside after a restore. */
export interface HeldHere {
  changes: number
  found: number
}

/**
 * A sign-out with its warning: when the device holds something of the account that the server lacks, the person is asked first, since
 * signing out deletes it (`confirm` drives the dialog, `SignOutConfirm`); otherwise it signs out at once. `before` runs as it goes (the
 * phone drawer closes itself).
 */
export function useSignOutFlow(mode: AuthMode, before?: () => void) {
  const signOut = useSignOut(mode)
  const [held, setHeld] = useState<HeldHere | null>(null)
  const go = async () => {
    setHeld(null)
    before?.()
    await signOut()
  }
  const start = async () => {
    const found = (await (await deviceData.rows())?.found().catch(() => []))?.length ?? 0
    const holds = { changes: outbox.count(), found }
    if (holds.changes > 0 || holds.found > 0) setHeld(holds)
    else await go()
  }
  return { start, confirm: { held, onCancel: () => setHeld(null), onConfirm: go } }
}
