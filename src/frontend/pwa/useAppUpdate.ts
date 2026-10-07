import { useSyncExternalStore } from 'react'
import { appUpdate } from './appUpdate.ts'

/** Whether a new version of the app is ready, and how to start it. */
export function useAppUpdate(): { ready: boolean; reload: () => void } {
  const ready = useSyncExternalStore(appUpdate.subscribe, () => appUpdate.ready, () => false)
  return { ready, reload: appUpdate.reload }
}
