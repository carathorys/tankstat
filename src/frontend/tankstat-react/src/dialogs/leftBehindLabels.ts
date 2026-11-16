import type { VolumeUnit } from '../gql/generated.ts'
import type { Change } from '../offline/changes.ts'
import { outbox } from '../offline/outbox.ts'
import { describe } from '../offline/waitingChanges.ts'
import { useChangeLabel } from '../pages/sync/useChangeLabel.ts'

/** Changes in words, as Waiting to sync names them (#330: the note and the confirmation of an edit that leaves them behind). */
export function useLeftBehindLabels() {
  const changeLabel = useChangeLabel()
  return async (changes: readonly Change[]): Promise<string[]> => {
    if (changes.length === 0) return []
    // Named among everything waiting, as the page names them: a set or a schedule added here is named by its own change.
    const groups = await describe(changes, outbox.changes)
    return groups.flatMap((g) => g.items.map((item) => changeLabel(item, { volume: g.volumeUnit as VolumeUnit | null }).label))
  }
}
