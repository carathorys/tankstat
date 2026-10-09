import { useEffect, useState, useSyncExternalStore } from 'react'
import type { PushEngine, PushState } from './push.ts'
import { offlineSync } from './runtime.ts'

const IDLE: PushState = { status: 'idle', last: null }

/** The sync engine (once loaded) and its state, re-rendering as it changes; null engine before the runtime started. */
export function usePushState(): { engine: PushEngine | null; state: PushState } {
  const [engine, setEngine] = useState<PushEngine | null>(null)
  useEffect(() => {
    let live = true
    // Not loaded (the app is not kept on this device yet, offline): nothing to follow until a later page asks again.
    void offlineSync()
      ?.then((e) => live && setEngine(e))
      .catch(() => undefined)
    return () => {
      live = false
    }
  }, [])
  const state = useSyncExternalStore(
    (listener) => engine?.subscribe(listener) ?? (() => undefined),
    () => engine?.state ?? IDLE,
  )
  return { engine, state }
}
