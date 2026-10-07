import { useSyncExternalStore } from 'react'
import { connectivity, type Reachability } from './connectivity.ts'

/** The server's reachability, re-rendering when it changes. */
export function useConnectivity(): { state: Reachability; reachable: boolean } {
  const state = useSyncExternalStore(connectivity.subscribe, () => connectivity.state)
  return { state, reachable: state !== 'unreachable' }
}
