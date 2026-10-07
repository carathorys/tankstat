import { useApolloClient } from '@apollo/client/react'
import type { AuthMode } from '../gql/generated.ts'
import { signedOut } from './oidc.ts'
import { signOutDevice } from './refresh.ts'

/**
 * Signs this device out: its session ends on the server (also after the access cookie ran out), then the store starts afresh, which
 * shows the sign-in screen. Shared by the navigation and the Account page's list of devices.
 */
export function useSignOut(mode: AuthMode): () => Promise<void> {
  const client = useApolloClient()
  return async () => {
    await signOutDevice()
    // Before the store resets: the sign-in screen that follows must wait for a click, not send the browser back to the provider.
    if (mode === 'OIDC') signedOut.mark()
    await client.resetStore()
  }
}
