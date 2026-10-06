/** Marks "signed out on purpose" for this tab: the sign-in screen then waits for a click instead of going straight back to the provider. */
export const SIGNED_OUT_KEY = 'tankstat.auth.signedOut'

/** The failure reasons (OidcFailures on the server) that have a text of their own; every other reason gets the general one. */
export const REASON_TEXT = {
  account_disabled: 'errors.auth.accountDisabled',
  access_denied: 'auth.failedReasons.access_denied',
} as const satisfies Record<string, string>

export const failureText = (reason: string | null): (typeof REASON_TEXT)[keyof typeof REASON_TEXT] | 'auth.failedText' =>
  reason !== null && Object.hasOwn(REASON_TEXT, reason) ? REASON_TEXT[reason as keyof typeof REASON_TEXT] : 'auth.failedText' // own keys only: 'toString' is not a reason

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
