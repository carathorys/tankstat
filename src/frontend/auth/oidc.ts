/** Marks "signed out on purpose" for this tab: the sign-in screen then waits for a click instead of going straight back to the provider. */
export const SIGNED_OUT_KEY = 'tankstat.auth.signedOut'

/** The failure reasons the sign-in screen has a specific text for (the rest get a general one); see OidcFailures on the server. */
export type WordedReason = 'account_disabled' | 'access_denied'

export function wordedReason(value: string | null): WordedReason | null {
  return value === 'account_disabled' || value === 'access_denied' ? value : null
}

/**
 * Where the browser goes to sign in: the server's challenge endpoint, told to come back to this page (path and query) afterwards. The
 * failure markers (`signIn`, `reason`) are left out, so a retry does not carry them along.
 */
export function oidcLoginUrl(pathname: string, search: string): string {
  const params = new URLSearchParams(search)
  params.delete('signIn')
  params.delete('reason')
  const query = params.toString()
  return `/auth/oidc/login?returnUrl=${encodeURIComponent(query ? `${pathname}?${query}` : pathname)}`
}

/** Remembered for the tab (sessionStorage); a storage that is unavailable means "not set" and "not remembered". */
export const signedOut = {
  isSet(): boolean {
    try {
      return window.sessionStorage.getItem(SIGNED_OUT_KEY) !== null
    } catch {
      return false
    }
  },
  mark(): void {
    try {
      window.sessionStorage.setItem(SIGNED_OUT_KEY, '1')
    } catch {
      // not remembering is acceptable
    }
  },
  clear(): void {
    try {
      window.sessionStorage.removeItem(SIGNED_OUT_KEY)
    } catch {
      // nothing to forget
    }
  },
}
