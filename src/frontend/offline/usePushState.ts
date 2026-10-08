import { useEffect, useState, useSyncExternalStore } from 'react'
import type { PushEngine, PushState } from './push.ts'
import { offlineSync } from './runtime.ts'

const IDLE: PushState = { status: 'idle', last: null }

/** The sync engine (once loaded) and its state, re-rendering as it changes; null engine before the runtime started. */
export function usePushState(): { engine: PushEngine | null; state: PushState } {
  const [engine, setEngine] = useState<PushEngine | null>(null)
  useEffect(() => {
    let live = true
    void offlineSync()?.then((e) => live && setEngine(e))
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
