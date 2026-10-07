/**
 * Keeping a device signed in. The access cookie lasts a few minutes; when a request comes back "sign in first", the refresh cookie (only
 * ever sent to /auth/token/...) buys a new one and the request is sent once more. Both cookies are HttpOnly: no token passes through
 * this code. The header tells the server the request comes from this app (a page of another site cannot send it).
 */
const HEADERS = { 'X-Requested-With': 'fetch' }

let pending: Promise<boolean> | null = null

/**
 * Asks for a new access cookie. True when the device is signed in again; false when it has to sign in (or the server has no token
 * endpoints: no authentication, or a proxy that signs every request in itself). A network failure rejects: that is not "signed out".
 * Callers at the same moment share one request, so a refresh token is never traded in twice by this tab.
 */
export function refreshSession(): Promise<boolean> {
  pending ??= fetch('/auth/token/refresh', { method: 'POST', credentials: 'same-origin', headers: HEADERS })
    .then((response) => response.ok)
    .finally(() => {
      pending = null
    })
  return pending
}

/** Signs this device out on the server (its session ends, both cookies go), also once the access cookie ran out. */
export async function signOutDevice(): Promise<void> {
  await fetch('/auth/token/logout', { method: 'POST', credentials: 'same-origin', headers: HEADERS })
}

/** `fetch` for the REST endpoints: an answer of 401 is tried once more after a refresh. */
export async function fetchSignedIn(input: string, init: RequestInit): Promise<Response> {
  const response = await fetch(input, init)
  if (response.status !== 401 || !(await refreshSession().catch(() => false))) return response
  return fetch(input, init)
}
