import { connectivity } from '../offline/connectivity.ts'
import { appUpdate } from './appUpdate.ts'

/** What browsers say when a lazily loaded page's code cannot be fetched (Chromium, Firefox, Safari), and Vite when its styles cannot. */
const CHUNK_ERRORS = [
  /Failed to fetch dynamically imported module/i,
  /error loading dynamically imported module/i,
  /Importing a module script failed/i,
  /Unable to preload CSS/i,
]

/** Whether the error is a page's code that could not be loaded (rather than a page that broke while showing). */
export function isChunkLoadError(error: unknown): boolean {
  const message = error instanceof Error ? error.message : typeof error === 'string' ? error : ''
  return CHUNK_ERRORS.some((pattern) => pattern.test(message))
}

/** Where the last reload for a page whose code could not be loaded is remembered (the URL and when), for this tab only. */
export const CHUNK_RELOAD_KEY = 'tankstat.chunkReload'
/** The same page is not reloaded again for its code within this time: a reload that did not help is not repeated (no loop). */
export const CHUNK_RELOAD_GUARD_MS = 60_000

export type ChunkRecovery = 'reloading' | 'notOfflineYet' | 'failed'

/** How long the check whether the server answers may take before the page counts as offline. */
export const PROBE_TIMEOUT_MS = 4_000

/**
 * Whether a reload would reach the server: `sw.js` is never answered from the service worker's cache, so any answer (but a gateway's that
 * the server is down) means the network and the server are there. Not when the app already knows it is offline: a reload there would
 * show the browser's own offline page.
 */
export async function serverAnswers(): Promise<boolean> {
  if (!connectivity.reachable || globalThis.navigator?.onLine === false) return false
  try {
    const response = await fetch('/sw.js', { method: 'HEAD', cache: 'no-store', signal: AbortSignal.timeout(PROBE_TIMEOUT_MS) })
    return ![502, 503, 504].includes(response.status)
  } catch {
    return false
  }
}

export interface ChunkRecoveryDeps {
  reload?: () => void
  /** Whether a reload would reach the server (`serverAnswers`). */
  probe?: () => Promise<boolean>
  behind?: boolean
  url?: string
  now?: number
  storage?: Pick<Storage, 'getItem' | 'setItem'>
}

/**
 * A page's code could not be loaded. It is usually there in a newer version: this tab runs an older release than its service worker
 * (another tab took the update, whose cache no longer holds the old code) or than the server (which no longer has it). Then a reload
 * of the same address loads the current version, offline too when the newer worker is the one answering: the person just asked for
 * this page, so nothing on screen is lost (the rule that nothing reloads by itself is about pages someone may be typing in). Once per
 * page and minute (`CHUNK_RELOAD_KEY`), so a reload that does not help ends in the ordinary error. Without a newer worker the reload
 * needs the server, which is asked first: offline, the page simply has not been kept on this device yet (a reload would only show the
 * browser's offline page).
 */
export async function recoverFromChunkError({
  reload = () => window.location.reload(),
  probe = serverAnswers,
  behind = appUpdate.behind,
  url = window.location.pathname + window.location.search,
  now = Date.now(),
  storage = globalThis.sessionStorage,
}: ChunkRecoveryDeps = {}): Promise<ChunkRecovery> {
  if (!behind && !(await probe())) return 'notOfflineYet'
  let last: { url?: string; at?: number } = {}
  try {
    last = JSON.parse(storage.getItem(CHUNK_RELOAD_KEY) ?? '{}') as typeof last
  } catch {
    // Unreadable or unavailable: as if this page was never reloaded for its code.
  }
  if (last.url === url && typeof last.at === 'number' && now - last.at < CHUNK_RELOAD_GUARD_MS) return 'failed'
  try {
    storage.setItem(CHUNK_RELOAD_KEY, JSON.stringify({ url, at: now }))
  } catch {
    return 'failed' // nowhere to remember the reload: no reload, so it can never loop
  }
  reload()
  return 'reloading'
}
