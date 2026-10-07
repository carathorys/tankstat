/**
 * Whether the server can be reached. `navigator.onLine` alone cannot tell (a self-hosted server on the home network is out of reach
 * from a phone on mobile data that is perfectly online), so every request reports its outcome: a request that got no answer marks the
 * server unreachable, any answer marks it reachable. While it is unreachable, `watchConnectivity` asks the server now and then.
 * `unknown` (nothing tried yet) counts as reachable.
 */
export type Reachability = 'unknown' | 'reachable' | 'unreachable'

let state: Reachability = 'unknown'
let since = Date.now()
const listeners = new Set<() => void>()

function set(next: Reachability) {
  if (state === next) return
  state = next
  since = Date.now()
  listeners.forEach((listener) => listener())
}

export const connectivity = {
  get state(): Reachability {
    return state
  },
  /** When the state last changed (Date.now()). */
  get since(): number {
    return since
  },
  get reachable(): boolean {
    return state !== 'unreachable'
  },
  /** A request got no answer (or only a gateway's). */
  failed(): void {
    set('unreachable')
  },
  /** The server answered. */
  succeeded(): void {
    set('reachable')
  },
  subscribe(listener: () => void): () => void {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },
  /** Tests: back to "nothing tried yet". */
  reset(): void {
    set('unknown')
  },
}

/** Waits before the n-th probe: soon at first, then once a minute. */
export const nextDelay = (attempt: number, delays: readonly number[] = PROBE_DELAYS): number => delays[Math.min(attempt, delays.length - 1)]
export const PROBE_DELAYS = [2_000, 5_000, 15_000, 30_000, 60_000] as const

export interface WatchDeps {
  delays?: readonly number[]
  setTimer?: (run: () => void, ms: number) => ReturnType<typeof setTimeout>
  clearTimer?: (timer: ReturnType<typeof setTimeout> | undefined) => void
  online?: () => boolean
}

/**
 * While the server is unreachable, asks it again (`probe`, true when it answered) with growing pauses, at once when the browser says it
 * is online again or the tab is shown again. The browser saying it went offline marks the server unreachable straight away.
 */
export function watchConnectivity(probe: () => Promise<boolean>, deps: WatchDeps = {}) {
  const { delays = PROBE_DELAYS, setTimer = setTimeout, clearTimer = clearTimeout, online = () => navigator.onLine } = deps
  let attempt = 0
  let timer: ReturnType<typeof setTimeout> | undefined
  let probing = false
  let stopped = false

  const plan = () => {
    clearTimer(timer)
    timer = undefined
    if (!stopped && !connectivity.reachable) timer = setTimer(() => void probeNow(), nextDelay(attempt++, delays))
  }

  async function probeNow() {
    if (probing || stopped) return
    probing = true
    try {
      if (await probe()) connectivity.succeeded()
    } catch {
      // still out of reach
    } finally {
      probing = false
    }
    plan()
  }

  const unsubscribe = connectivity.subscribe(() => {
    attempt = 0
    plan()
  })
  const onOnline = () => void probeNow()
  const onOffline = () => connectivity.failed()
  const onVisible = () => {
    if (document.visibilityState === 'visible' && !connectivity.reachable) void probeNow()
  }
  window.addEventListener('online', onOnline)
  window.addEventListener('offline', onOffline)
  document.addEventListener('visibilitychange', onVisible)
  if (!online()) connectivity.failed()

  return {
    probeNow,
    stop() {
      stopped = true
      clearTimer(timer)
      unsubscribe()
      window.removeEventListener('online', onOnline)
      window.removeEventListener('offline', onOffline)
      document.removeEventListener('visibilitychange', onVisible)
    },
  }
}
