import type { ChartDataQuery, ChartGrouping, ChartKind, ChartMetric, ChartRange } from '../gql/generated.ts'
import { PLACEHOLDER_PAIRS, SERIES_COLORS } from '../theme/scales.ts'

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

/** Series colours that work on the dark and the light scheme alike. */
export const PALETTE: readonly string[] = SERIES_COLORS

export const seriesId = (s: Series) => `${s.kind}|${s.currency ?? ''}`

/** "2026-09" → "Sep 2026", "2026-Q3" → "Q3 2026", "2026" → "2026"; anything else (an expense category) as it is. */
export function periodLabel(key: string, language: string): string {
  const month = /^(\d{4})-(\d{2})$/.exec(key)
  if (month) return new Intl.DateTimeFormat(language, { month: 'short', year: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(Number(month[1]), Number(month[2]) - 1, 1)))
  const quarter = /^(\d{4})-Q(\d)$/.exec(key)
  if (quarter) return `Q${quarter[2]} ${quarter[1]}`
  return key
}

/**
 * The periods as the chart's axis names them, short so more of them fit (#159): a month alone ("Nov"), with its year under it on the first
 * and on every January ("Nov\n2025", "Dec", "Jan\n2026": a second line, so the label is no wider than its month and fits beside the
 * chart's edge too); a quarter "Q4 ’25". A year or a category as it is. The tooltip and the table keep `periodLabel`.
 */
export function shortPeriodLabels(keys: readonly string[], language: string): Map<string, string> {
  const monthOnly = new Intl.DateTimeFormat(language, { month: 'short', timeZone: 'UTC' })
  return new Map(
    keys.map((key, i) => {
      const month = /^(\d{4})-(\d{2})$/.exec(key)
      if (month) {
        const name = monthOnly.format(new Date(Date.UTC(Number(month[1]), Number(month[2]) - 1, 1)))
        return [key, i === 0 || month[2] === '01' ? `${name}\n${month[1]}` : name]
      }
      const quarter = /^(\d{4})-Q(\d)$/.exec(key)
      return [key, quarter ? `Q${quarter[2]} ’${quarter[1].slice(2)}` : key]
    }),
  )
}

/** The value axes of a chart: what each is called, which series it draws, and whether it is shown. */
export interface ChartAxes {
  /** One per currency in a chart of costs (in the order the series name them), else one. */
  axes: { id: string; currency: string | null }[]
  /** The id of the axis a series is drawn on. */
  axisOf: (s: Series) => string
  /** False when several currencies each have a scale of their own: then no axis, and no grid lines, would mean anything for all of them. */
  shown: boolean
}

/**
 * Costs are never converted (`fuel-log-and-stats.md`), so they have no common measure: in a chart of costs every currency is drawn on a
 * scale of its own, and the highest value of each reaches the same height (#159). Any other unit shares one axis.
 */
export function chartAxes(data: Pick<ChartData, 'unit' | 'series'>): ChartAxes {
  if (data.unit !== 'CURRENCY') return { axes: [{ id: 'value', currency: null }], axisOf: () => 'value', shown: true }
  const currencies = [...new Set(data.series.map((s) => s.currency ?? ''))]
  const id = (currency: string) => `currency:${currency}`
  return { axes: currencies.map((c) => ({ id: id(c), currency: c || null })), axisOf: (s) => id(s.currency ?? ''), shown: currencies.length <= 1 }
}

/** Where a bar goes among a period's bars: its place, how many there are, and whether it ends its stack (its top corners are rounded). */
export interface BarPlace {
  place: number
  of: number
  top: boolean
}

/**
 * The bars of each period: only the groups that have a value there (#159, the owner's decision), side by side in the chart's order. A
 * group is a currency when the bars are stacked (its series make one stack), else a series. `group` is a series' group in the whole chart,
 * the order the chart lays every group out in; `at` the place of one of its bars in a period (`index` into `periods`, the chart's rows),
 * null where its group has nothing.
 */
export function barPlaces(
  data: Pick<ChartData, 'series'>,
  periods: readonly string[],
  stacked: boolean,
): { groups: number; group: (s: Series) => number; at: (s: Series, index: number) => BarPlace | null } {
  const groupKey = (s: Series) => (stacked ? `currency:${s.currency ?? ''}` : seriesId(s))
  const keys = [...new Set(data.series.map(groupKey))]
  const has = (s: Series, index: number) => (s.points.find((p) => p.key === periods[index])?.value ?? 0) > 0
  return {
    groups: keys.length,
    group: (s) => keys.indexOf(groupKey(s)),
    at: (s, index) => {
      if (!has(s, index)) return null
      const present = keys.filter((key) => data.series.some((other) => groupKey(other) === key && has(other, index)))
      // The stack's top is its last series (in the chart's order) with a value here: the series are stacked in that order.
      const mine = data.series.filter((other) => groupKey(other) === groupKey(s) && has(other, index))
      return { place: present.indexOf(groupKey(s)), of: present.length, top: mine[mine.length - 1] === s }
    },
  }
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


/** The placeholder behind a vehicle without a picture: a gradient picked from the vehicle's id. */
export function placeholderGradient(id: string): string {
  const hash = [...id].reduce((h, c) => (h * 31 + c.charCodeAt(0)) >>> 0, 7)
  const [a, b] = PLACEHOLDER_PAIRS[hash % PLACEHOLDER_PAIRS.length]
  return `linear-gradient(135deg, ${a}, ${b})`
}

/** What a new chart starts from. */
export const NEW_RECIPE: ChartRecipe = { metric: 'TOTAL_SPEND', grouping: 'MONTH', kind: 'BAR', range: 'LAST12_MONTHS', stacked: false, from: null, to: null }

/** The inputs of the chart query for a recipe (dates only count for a custom range). */
export const toConfig = (r: ChartRecipe) => ({ metric: r.metric, grouping: r.grouping, kind: r.kind, range: r.range, stacked: r.stacked, from: r.range === 'CUSTOM' ? r.from : null, to: r.range === 'CUSTOM' ? r.to : null })
