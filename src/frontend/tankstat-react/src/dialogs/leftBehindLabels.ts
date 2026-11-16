import type { Change } from '../offline/changes.ts'
import { describe } from '../offline/waitingChanges.ts'
import { useChangeLabel } from '../pages/sync/useChangeLabel.ts'

/** Changes in words, as Waiting to sync names them (#330: the note and the confirmation of an edit that leaves them behind). */
export function useLeftBehindLabels() {
  const changeLabel = useChangeLabel()
  return async (changes: readonly Change[]): Promise<string[]> => {
    if (changes.length === 0) return []
    const groups = await describe(changes)
    return groups.flatMap((g) => g.items.map((item) => changeLabel(item, { distance: g.distanceUnit as never, volume: g.volumeUnit as never }).label))
  }
}
