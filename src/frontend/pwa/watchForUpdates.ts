/** How often a page that stays open asks the server whether there is a new version. */
export const CHECK_EVERY_MS = 60 * 60 * 1000
/** A tab shown again checks too, unless it has just checked. */
export const RECHECK_AFTER_MS = 5 * 60 * 1000

/**
 * A browser only looks for a new service worker when a page is opened, so a tab (or an installed app) that stays open would never hear
 * of a release. This asks again every hour and when the tab is shown again. Offline, or when the server does not answer, the check is
 * skipped quietly: the next one tries again. Returns the way to stop.
 */
export function watchForUpdates(registration: { update: () => Promise<unknown> }, { online = () => navigator.onLine } = {}): () => void {
  let last = Date.now()
  const check = () => {
    if (!online()) return
    last = Date.now()
    registration.update().catch(() => undefined)
  }
  const timer = setInterval(check, CHECK_EVERY_MS)
  const shown = () => {
    if (document.visibilityState === 'visible' && Date.now() - last >= RECHECK_AFTER_MS) check()
  }
  document.addEventListener('visibilitychange', shown)
  return () => {
    clearInterval(timer)
    document.removeEventListener('visibilitychange', shown)
  }
}
