import type { ChartDataQuery, ChartGrouping, ChartKind, ChartMetric, ChartRange } from '../gql/generated.ts'

export type ChartData = ChartDataQuery['vehicleChartData']
export type Series = ChartData['series'][number]

/** The recipe of a chart (what a saved chart stores and what the builder edits). */
export interface ChartRecipe {
  metric: ChartMetric
  grouping: ChartGrouping
  kind: ChartKind
  range: ChartRange
  stacked: boolean
  from: string | null
  to: string | null
}

/** Series colours: Radix scale steps that work on the dark and the light theme. */
export const PALETTE = ['var(--indigo-9)', 'var(--cyan-9)', 'var(--amber-9)', 'var(--grass-9)', 'var(--tomato-9)', 'var(--violet-9)', 'var(--teal-9)', 'var(--pink-9)']

export const seriesId = (s: Series) => `${s.kind}|${s.currency ?? ''}`

/** "2026-09" → "Sep 2026", "2026-Q3" → "Q3 2026", "2026" → "2026"; anything else (an expense category) as it is. */
export function periodLabel(key: string, language: string): string {
  const month = /^(\d{4})-(\d{2})$/.exec(key)
  if (month) return new Intl.DateTimeFormat(language, { month: 'short', year: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(Number(month[1]), Number(month[2]) - 1, 1)))
  const quarter = /^(\d{4})-Q(\d)$/.exec(key)
  if (quarter) return `Q${quarter[2]} ${quarter[1]}`
  return key
}

/** Metrics that add up (and so can be shown as the parts of a donut). */
export const ADDITIVE: ChartMetric[] = ['TOTAL_SPEND', 'FUEL_COST', 'EXPENSE_COST', 'FUEL_VOLUME', 'DISTANCE', 'FILL_UPS']

/** The same rules the server enforces, so the builder only offers what is valid. */
export function normalise(r: ChartRecipe): ChartRecipe {
  const grouping = r.grouping === 'CATEGORY' && r.metric !== 'EXPENSE_COST' ? 'MONTH' : r.grouping
  const kind = r.kind === 'DONUT' && !ADDITIVE.includes(r.metric) ? 'BAR' : r.kind
  const stacked = r.stacked && r.metric === 'TOTAL_SPEND' && grouping !== 'CATEGORY'
  const custom = r.range === 'CUSTOM'
  return { ...r, grouping, kind, stacked, from: custom ? r.from : null, to: custom ? r.to : null }
}

export const isReady = (r: ChartRecipe) => r.range !== 'CUSTOM' || (r.from !== null && r.to !== null && r.from <= r.to)

/** A deterministic colour pair per vehicle, for the picture placeholder. */
const PLACEHOLDERS = [
  ['var(--indigo-8)', 'var(--cyan-8)'],
  ['var(--teal-8)', 'var(--grass-8)'],
  ['var(--violet-8)', 'var(--pink-8)'],
  ['var(--orange-8)', 'var(--tomato-8)'],
  ['var(--blue-8)', 'var(--sky-8)'],
  ['var(--plum-8)', 'var(--crimson-8)'],
]

/** The placeholder behind a vehicle without a picture: a gradient picked from the vehicle's id. */
export function placeholderGradient(id: string): string {
  const hash = [...id].reduce((h, c) => (h * 31 + c.charCodeAt(0)) >>> 0, 7)
  const [a, b] = PLACEHOLDERS[hash % PLACEHOLDERS.length]
  return `linear-gradient(135deg, ${a}, ${b})`
}

/** What a new chart starts from. */
export const NEW_RECIPE: ChartRecipe = { metric: 'TOTAL_SPEND', grouping: 'MONTH', kind: 'BAR', range: 'LAST12_MONTHS', stacked: false, from: null, to: null }

/** The inputs of the chart query for a recipe (dates only count for a custom range). */
export const toConfig = (r: ChartRecipe) => ({ metric: r.metric, grouping: r.grouping, kind: r.kind, range: r.range, stacked: r.stacked, from: r.range === 'CUSTOM' ? r.from : null, to: r.range === 'CUSTOM' ? r.to : null })
