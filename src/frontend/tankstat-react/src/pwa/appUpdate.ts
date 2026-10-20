/** A new version of the app that the browser has downloaded and that waits for the user's go-ahead (see `UpdateNotice`). */
let apply: (() => void) | null = null
/** The new version already runs the page's service worker (another tab took it): only a reload is missing. */
let finishing = false
/** A newer service worker controls this page than the one it started with: the page's own chunks may be gone from its cache. */
let behind = false
const listeners = new Set<() => void>()
const notify = () => listeners.forEach((listener) => listener())

/**
 * Whether a new version is ready. `registerApp.ts` tells it when the service worker has one waiting, or when another tab already switched
 * to it (`offer`); components read it through `useAppUpdate`. Nothing reloads by itself: a person in the middle of a form must not lose it.
 */
export const appUpdate = {
  get ready(): boolean {
    return apply !== null
  },
  /** The new version runs already (another tab took it): the notice says a reload finishes the update. */
  get finishing(): boolean {
    return finishing
  },
  /** This page runs under a newer service worker than it started with (see `chunkRecovery.ts`). */
  get behind(): boolean {
    return behind
  },
  /** A new version is there; `reload` hands it the page and reloads. With `finishing`, it already controls the page. */
  offer(reload: () => void, options: { finishing?: boolean } = {}): void {
    apply = reload
    finishing = options.finishing ?? false
    if (finishing) behind = true
    notify()
  },
  /** Starts the new version (the page reloads). */
  reload(): void {
    apply?.()
  },
  subscribe(listener: () => void): () => void {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },
  /** Tests: forget an offered version. */
  reset(): void {
    apply = null
    finishing = false
    behind = false
    notify()
  },
}
