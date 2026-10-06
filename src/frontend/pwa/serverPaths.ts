/**
 * Navigations the service worker must leave to the server instead of answering with the app shell: the API, the OIDC endpoints (an
 * index.html answer to /auth/oidc/login or /auth/oidc/callback would end sign-in, and the sign-in screen would then redirect to it again,
 * for ever), pictures and uploads. Workbox tests each pattern against the path plus the query string.
 */
export const SERVER_PATHS: RegExp[] = [/^\/graphql/, /^\/auth\//, /^\/media\//, /^\/imports\//]

/** Whether a navigation to this path (with its query string) goes to the server, never to the shell. */
export const isServerPath = (pathAndQuery: string) => SERVER_PATHS.some((pattern) => pattern.test(pathAndQuery))
