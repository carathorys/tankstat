import { keepStorage } from '../offline/tabLock.ts'
import { appUpdate } from './appUpdate.ts'
import { offlineReady } from './offlineReady.ts'

/** What of a service worker this file uses (the browser's, or a test's fake). */
export interface Worker extends EventTarget {
  readonly state: string
  postMessage(message: unknown): void
}

export interface Registration extends EventTarget {
  readonly installing: Worker | null
  readonly waiting: Worker | null
  readonly active: Worker | null
  update(): Promise<unknown>
}

export interface Container extends EventTarget {
  readonly controller: Worker | null
  register(url: string, options: { scope: string }): Promise<Registration>
}

export interface RegisterDeps {
  container: Container
  reload?: () => void
  /** The page reloads after this long even if the new worker never says it took over. */
  fallbackMs?: number
  /** Told the registration (main.tsx asks it for updates now and then: `watchForUpdates`). */
  onRegistered?: (registration: Registration) => void
  /** Told once the app is kept on this device: the browser is asked to keep the site's storage (`keepStorage`). */
  onReady?: () => void
}

/** How long a Reload waits for the new service worker to take over before it reloads anyway. */
export const RELOAD_FALLBACK_MS = 3_000

/** The worker's own message for "take over now" (the generated `sw.js` listens for it, as `registerType: 'prompt'` builds it). */
const SKIP_WAITING = { type: 'SKIP_WAITING' }

/**
 * Registers the service worker that keeps the built app (`sw.js`, see vite.config.ts) and follows it for as long as the page is open:
 * - the first one, on a first visit: the app is being stored until it has activated, then it is ready offline (`offlineReady`);
 * - every new version, found when the page opens or later (`watchForUpdates`), however many come: once it is installed it waits, and
 *   `UpdateNotice` offers it (`appUpdate`); its Reload hands the page over and reloads. When nothing waits any more (another tab took
 *   the new version), Reload just reloads, and a reload that the new worker never confirms happens anyway after `fallbackMs`;
 * - another tab taking the new version: this page still runs the old one under the new worker, whose cache no longer holds the old
 *   pages' code; the notice says a reload finishes the update, and `appUpdate.behind` lets a page that cannot load reload (`chunkRecovery`).
 * Nothing reloads by itself: a person may be in the middle of a form.
 */
export async function registerApp({ container, reload = () => window.location.reload(), fallbackMs = RELOAD_FALLBACK_MS, onRegistered, onReady = keepStorage }: RegisterDeps): Promise<void> {
  const startedWith = container.controller
  /** This page asked the new worker to take over (its own Reload): the take-over is its cue to reload. */
  let asked = false
  /** A first visit: the first take-over of the page is the first worker claiming it (`clientsClaim`), not another tab's update. */
  let claimPending = startedWith === null

  const ready = (justNow: boolean) => {
    offlineReady.set('ready', { justNow })
    onReady()
  }

  container.addEventListener('controllerchange', () => {
    if (claimPending) {
      claimPending = false
      return
    }
    if (!asked) appUpdate.offer(reload, { finishing: true })
  })

  let registration: Registration
  try {
    registration = await container.register('/sw.js', { scope: '/' })
  } catch (error) {
    console.warn('The app cannot be kept on this device for offline use: its service worker did not register.', error)
    return
  }

  const offer = () =>
    appUpdate.offer(() => {
      const waiting = registration.waiting
      if (!waiting) return reload() // another tab took the new version already: it controls this page, a reload is all
      asked = true
      const fallback = setTimeout(reload, fallbackMs)
      container.addEventListener(
        'controllerchange',
        () => {
          clearTimeout(fallback)
          reload()
        },
        { once: true },
      )
      waiting.postMessage(SKIP_WAITING)
    })

  const follow = (worker: Worker | null) => {
    if (!worker) return
    const update = registration.active !== null // a worker runs already: this one is a new version
    const settle = () => {
      if (update && worker.state === 'installed') offer()
      if (!update && worker.state === 'activated') ready(true)
    }
    worker.addEventListener('statechange', settle)
    settle()
  }

  if (registration.active) {
    claimPending = false // it activated before this page (a reload that skipped it): it does not claim the page again
    ready(false)
  } else offlineReady.set('storing')
  if (registration.waiting) offer()
  follow(registration.installing)
  // Every new version found later, not only the first (a page or an installed app can stay open across several releases).
  registration.addEventListener('updatefound', () => follow(registration.installing))
  onRegistered?.(registration)
}
