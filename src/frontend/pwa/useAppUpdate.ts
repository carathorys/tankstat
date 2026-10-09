import { useSyncExternalStore } from 'react'
import { appUpdate } from './appUpdate.ts'

/** Whether a new version of the app is ready (`finishing`: it runs already, only a reload is missing), and how to start it. */
export function useAppUpdate(): { ready: boolean; finishing: boolean; reload: () => void } {
  const ready = useSyncExternalStore(appUpdate.subscribe, () => appUpdate.ready, () => false)
  const finishing = useSyncExternalStore(appUpdate.subscribe, () => appUpdate.finishing, () => false)
  return { ready, finishing, reload: appUpdate.reload }
}
