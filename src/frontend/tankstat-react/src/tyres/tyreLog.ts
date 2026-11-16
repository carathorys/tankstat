import type { TyreEntryKind } from '../gql/generated.ts'
import type { InstallationState } from './tyreHistory.ts'

/**
 * How the tyre log follows the history, ported from the server's `TyreLog` (#288) so a tyre change made offline shows its lines as the
 * server will make them. Every installation has its line, with the installation's id: one a change creates gets a line (a change of positions
 * when the same set came off that day, else a swap), one it trashes takes its line along, one whose first day moved moves its line (and the
 * line's reading), one moved to another set takes its line along and the kinds around it are worked out again (#332). An installation a join took away from its first day leaves what its line held for that day (a reading, a note) there, on a
 * Note line of the set. Pure; pinned to the server by `tests/backend/contracts/tyre-swaps/tyre-swaps.json`.
 */
export interface LineState {
  id: string
  kind: TyreEntryKind
  setId: string | null
  installationId: string | null
  day: string
  odometer: number | null
  note: string | null
}

/** An installation a join took away from its first day (`swap`'s `joined`). */
export interface Joined {
  id: string
  day: string
}

/** What following a change did to the lines, for the readings that go with them. */
export interface Followed {
  lines: LineState[]
  /** Lines that went to the trash with their installation. */
  trashed: LineState[]
  /** Lines whose day moved: the day they had. */
  moved: { line: LineState; from: string }[]
}

/** The line of a new installation: a change of positions when the same set came off on its first day. */
export function lineFor(installation: InstallationState, live: readonly InstallationState[]): LineState {
  const positions = live.some((o) => o.setId === installation.setId && o.until === installation.from)
  return {
    id: installation.id, kind: positions ? 'POSITIONS_CHANGED' : 'SWAP', setId: installation.setId, installationId: installation.id,
    day: installation.from, odometer: null, note: null,
  }
}

/**
 * The lines after a change of the history: `before` and `after` are its live installations, `joined` what a join took away from its day.
 * `keptId` names a Note line that keeps what a joined line held (the device's own id: the server gives it one of its own).
 */
export function follow(
  before: readonly InstallationState[], after: readonly InstallationState[], lines: readonly LineState[], joined: readonly Joined[],
  keptId: (line: LineState) => string,
): Followed {
  let live = lines.map((l) => ({ ...l }))
  const trashed: LineState[] = []
  const moved: Followed['moved'] = []
  for (const j of joined) {
    const line = live.find((l) => l.id === j.id)
    if (!line || (line.odometer === null && line.note === null)) continue
    live.push({ id: keptId(line), kind: 'NOTE', setId: line.setId, installationId: null, day: line.day, odometer: line.odometer, note: line.note })
    line.odometer = null
    line.note = null
  }
  for (const gone of before.filter((b) => !after.some((a) => a.id === b.id))) {
    const line = live.find((l) => l.id === gone.id)
    if (!line) continue
    trashed.push(line)
    live = live.filter((l) => l !== line)
  }
  for (const now of after) {
    const was = before.find((b) => b.id === now.id)
    const line = live.find((l) => l.id === now.id)
    if (was && line && line.day !== now.from) {
      moved.push({ line, from: line.day })
      line.day = now.from
    }
  }
  for (const created of after.filter((a) => !before.some((b) => b.id === a.id))) live.push(lineFor(created, after))
  // A period moved to another set (#332): its line takes the set, and its kind and the kind of the line that starts where it ends are
  // worked out again, as a new line's would be.
  for (const now of after) {
    const was = before.find((b) => b.id === now.id)
    if (!was || was.setId === now.setId) continue
    for (const affected of [now, ...after.filter((o) => o.id !== now.id && now.until !== null && o.from === now.until)]) {
      const line = live.find((l) => l.id === affected.id)
      if (!line || (line.kind !== 'SWAP' && line.kind !== 'POSITIONS_CHANGED')) continue
      line.setId = affected.setId
      line.kind = after.some((o) => o.id !== affected.id && o.setId === affected.setId && o.until === affected.from) ? 'POSITIONS_CHANGED' : 'SWAP'
    }
  }
  return { lines: live, trashed, moved }
}
