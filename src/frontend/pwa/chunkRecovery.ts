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

export interface ChunkRecoveryDeps {
  reload?: () => void
  reachable?: boolean
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
 * page and minute (`CHUNK_RELOAD_KEY`), so a reload that does not help ends in the ordinary error. Offline without a newer worker, the
 * page simply has not been kept on this device yet.
 */
export function recoverFromChunkError({
  reload = () => window.location.reload(),
  reachable = connectivity.reachable,
  behind = appUpdate.behind,
  url = window.location.pathname + window.location.search,
  now = Date.now(),
  storage = globalThis.sessionStorage,
}: ChunkRecoveryDeps = {}): ChunkRecovery {
  if (!reachable && !behind) return 'notOfflineYet'
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
