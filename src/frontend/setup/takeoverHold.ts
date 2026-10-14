import { useEffect, useSyncExternalStore } from 'react'

/**
 * Whether the setup page waits for a restart it asked for (its last step's, a restore's). The takeover needs the server reachable,
 * since offline the session is the device's last answer, maybe from before a setup done elsewhere; but a restart puts the server out of
 * reach on purpose, and the page in its place would lose the wait that goes on once the server is back. Small and free of the setup
 * page's imports: the shell reads it.
 */
let holds = 0
const listeners = new Set<() => void>()

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

function change(by: number) {
  holds += by
  listeners.forEach((listener) => listener())
}

/** True while some part of the setup page holds the takeover through a restart. */
export function useTakeoverHeld(): boolean {
  return useSyncExternalStore(subscribe, () => holds > 0)
}

/** Holds the takeover while `active` and the component is mounted: the setup page stays while the server it restarts is away. */
export function useHoldTakeover(active: boolean) {
  useEffect(() => {
    if (!active) return
    change(1)
    return () => change(-1)
  }, [active])
}
