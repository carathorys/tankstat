/**
 * The version the web app was built as: `VERSION` of the build (CI, the Dockerfile, `mise run build`), baked in by `vite.config.ts` as
 * `__APP_VERSION__`. Shown in the footer even while the server is out of reach. Loaded by `vite.config.ts` in Node too, so it imports
 * nothing and touches no page.
 */
declare global {
  /** Replaced by the build (`define` in `vite.config.ts`); undefined only where Vite did not transform the code (Node). */
  const __APP_VERSION__: string | undefined
}

/** The fallback when a build is given no version: the same as the Dockerfile's default. */
export const DEV_VERSION = '0.0.0-dev'

/**
 * A build's `VERSION` as the app shows it: without the build metadata after `+`, or `0.0.0-dev` when there is none. The same rule as
 * `HealthReporter.VersionOf`, so the web app and the API built from one `VERSION` show the same text.
 */
export function appVersion(raw?: string): string {
  const version = raw?.split('+', 1)[0].trim()
  return version || DEV_VERSION
}

/** This build's version. */
export const APP_VERSION: string = typeof __APP_VERSION__ === 'string' ? __APP_VERSION__ : DEV_VERSION
