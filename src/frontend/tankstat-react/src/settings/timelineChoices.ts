import type { TimelineLayer, TimelineRange } from '../gql/generated.ts'

/** What the user chose to see on the tyre timeline (#332): the range, the layers shown and the sets hidden. */
export interface TimelineChoices {
  range: TimelineRange
  layers: TimelineLayer[]
  hiddenSets: string[]
}

export const TIMELINE_RANGES: readonly TimelineRange[] = ['ALL', 'TWO_YEARS', 'TWELVE_MONTHS']
/** The layers in the order the *Show* menu lists them. */
export const TIMELINE_LAYERS: readonly TimelineLayer[] = ['BEFORE_TRACKING', 'GAPS', 'MARKS', 'RETIRED_SETS', 'TABLE']

/** The owner's decision 88: the whole range, everything but the retired sets, no set hidden. */
export const DEFAULT_TIMELINE: TimelineChoices = { range: 'ALL', layers: ['BEFORE_TRACKING', 'GAPS', 'MARKS', 'TABLE'], hiddenSets: [] }

/** A stored or received value that this build can draw: a range and layers it knows (a newer server's are not), ids as text. */
export function isTimelineChoices(value: unknown): value is TimelineChoices {
  if (!value || typeof value !== 'object') return false
  const v = value as Record<string, unknown>
  return (
    (TIMELINE_RANGES as readonly unknown[]).includes(v.range) &&
    Array.isArray(v.layers) && v.layers.every((l) => (TIMELINE_LAYERS as readonly unknown[]).includes(l)) &&
    Array.isArray(v.hiddenSets) && v.hiddenSets.every((id) => typeof id === 'string')
  )
}
