/** A new version of the app that the browser has downloaded and that waits for the user's go-ahead (see `UpdateNotice`). */
let apply: (() => void) | null = null
const listeners = new Set<() => void>()
const notify = () => listeners.forEach((listener) => listener())

/**
 * Whether a new version is ready. main.tsx tells it when the service worker has one waiting (`offer`); components read it through
 * `useAppUpdate`. Nothing reloads by itself: a person in the middle of a form must not lose it.
 */
export const appUpdate = {
  get ready(): boolean {
    return apply !== null
  },
  /** The service worker has a new version waiting; `reload` hands it the page and reloads. */
  offer(reload: () => void): void {
    apply = reload
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
    notify()
  },
}
