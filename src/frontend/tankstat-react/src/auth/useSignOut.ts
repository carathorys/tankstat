import { useApolloClient } from '@apollo/client/react'
import { useRef, useState } from 'react'
import type { AuthMode } from '../gql/generated.ts'
import { deviceData } from '../offline/deviceData.ts'
import { outbox } from '../offline/outbox.ts'
import { signedOut } from './oidc.ts'
import { endSessionOnServer, tellLeftOverSignOut } from './signedOutHere.ts'

/**
 * Signs this device out, whether the server can be reached or not (#162). The device notes first that it signed out (no secret: the
 * cookies are HttpOnly, only the server can clear them), so a sign-out cut short (the app closed while the server is slow) still holds:
 * the sign-in screen shows, and the server is told once it is back. Then everything the device keeps for the account goes, the server is
 * told (best effort, under the probes' deadline; the note goes once it ended the session), and the store starts afresh, which shows the
 * sign-in screen. When the device cannot note it, the server must hear of it now, or nothing is deleted and it rejects: the cookies would
 * sign the device straight back in. Shared by the navigation and the Account page's list of devices.
 */
export function useSignOut(mode: AuthMode): () => Promise<void> {
  const client = useApolloClient()
  return async () => {
    const noted = await deviceData.setSignedOutHere({ mode, at: Date.now() }).then(
      () => true,
      (error: unknown) => (console.warn('This device cannot remember that it signed out.', error), false),
    )
    if (!noted && !(await endSessionOnServer())) throw new Error('The sign-out can neither be kept on this device nor told to the server.')
    // Before the store resets: the sign-in screen that follows must wait for a click, not send the browser back to the provider.
    if (mode === 'OIDC') signedOut.mark()
    await deviceData.signOut(mode)
    if (noted) await tellLeftOverSignOut().catch(() => false)
    // Offline a query asked again may fail (the server's health is never the device's): each says so itself; the sign-out is done.
    await client.resetStore().catch(() => undefined)
  }
}

/** What only this device holds of the account, which a sign-out deletes: changes not sent yet, and rows set aside after a restore. */
export interface HeldHere {
  changes: number
  found: number
}

/** The sign-out's warning: what it would delete, whether it is shown, and whether the last try failed (the dialog says so). */
export interface SignOutWarning {
  held: HeldHere
  open: boolean
  failed: boolean
}

const NOTHING: SignOutWarning = { held: { changes: 0, found: 0 }, open: false, failed: false }

/**
 * A sign-out with its warning: when the device holds something of the account that the server lacks, the person is asked first, since
 * signing out deletes it (`confirm` drives the dialog, `SignOutConfirm`); otherwise it signs out at once. A sign-out that failed opens the
 * dialog again, saying so. One at a time: a second press while one runs does nothing. `before` runs as it goes (the phone drawer closes
 * itself).
 */
export function useSignOutFlow(mode: AuthMode, before?: () => void) {
  const signOut = useSignOut(mode)
  const [warning, setWarning] = useState<SignOutWarning>(NOTHING)
  const running = useRef(false)
  const go = async () => {
    if (running.current) return
    running.current = true
    setWarning((w) => ({ ...w, open: false, failed: false })) // the counts stay while the dialog closes
    try {
      before?.()
      await signOut()
    } catch {
      setWarning((w) => ({ ...w, open: true, failed: true }))
    } finally {
      running.current = false
    }
  }
  const start = async () => {
    if (running.current) return
    // Only the rows a download judged (`checked`): the others may well be on the server, and Waiting to sync lists none of them.
    const found = (await (await deviceData.rows())?.found().catch(() => []))?.filter((row) => row.checked).length ?? 0
    const held = { changes: outbox.count(), found }
    if (held.changes > 0 || held.found > 0) setWarning({ held, open: true, failed: false })
    else {
      setWarning({ held, open: false, failed: false })
      await go()
    }
  }
  return { start, confirm: { ...warning, onCancel: () => setWarning((w) => ({ ...w, open: false, failed: false })), onConfirm: go } }
}
