import { useSyncExternalStore } from 'react'

/**
 * Whether the app itself (its pages, scripts and styles) is kept on this device, so it opens and every page loads without the server:
 * `storing` until the first service worker has stored it all, `ready` once it has, `unsupported` where there is no service worker (the
 * development server, a page over plain HTTP, a browser without them). Set by `registerApp.ts`; the Offline data panel shows it and
 * `OfflineReadyNotice` says so once when it became ready in this page's life.
 */
export type OfflineReadiness = 'unsupported' | 'storing' | 'ready'

let state: OfflineReadiness = 'unsupported'
/** It became ready while this page was open (a first visit), and nobody has told the person yet. */
let untold = false
const listeners = new Set<() => void>()

export const offlineReady = {
  get state(): OfflineReadiness {
    return state
  },
  /** `justNow`: the first service worker finished storing the app while this page was open. */
  set(next: OfflineReadiness, { justNow = false }: { justNow?: boolean } = {}): void {
    if (next === state) return
    state = next
    untold = next === 'ready' && justNow
    listeners.forEach((listener) => listener())
  },
  /** True once, when the app became ready offline in this page's life: the one who asks first tells the person. */
  takeNews(): boolean {
    const news = untold
    untold = false
    return news
  },
  subscribe(listener: () => void): () => void {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },
  /** Tests: as on a page without a service worker. */
  reset(): void {
    state = 'unsupported'
    untold = false
    listeners.forEach((listener) => listener())
  },
}

/** Whether the app is kept on this device for offline use, re-rendering when that changes. */
export function useOfflineReady(): OfflineReadiness {
  return useSyncExternalStore(offlineReady.subscribe, () => offlineReady.state, () => 'unsupported')
}
