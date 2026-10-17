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

/** A period as the server writes its key: a month ("2026-09") or a quarter ("2026-Q3"); null for anything else (a year, a category). */
export type Period = { year: string; month: number; quarter?: undefined } | { year: string; quarter: number; month?: undefined }

/** The one reading of a period key, for the axis and the tooltip alike. */
export function parsePeriod(key: string): Period | null {
  const month = /^(\d{4})-(\d{2})$/.exec(key)
  if (month) return { year: month[1], month: Number(month[2]) }
  const quarter = /^(\d{4})-Q(\d)$/.exec(key)
  return quarter ? { year: quarter[1], quarter: Number(quarter[2]) } : null
}

const monthDate = (period: { year: string; month: number }) => new Date(Date.UTC(Number(period.year), period.month - 1, 1))

/** "2026-09" → "Sep 2026", "2026-Q3" → "Q3 2026", "2026" → "2026"; anything else (an expense category) as it is. */
export function periodLabel(key: string, language: string): string {
  const period = parsePeriod(key)
  if (period?.month !== undefined) return new Intl.DateTimeFormat(language, { month: 'short', year: 'numeric', timeZone: 'UTC' }).format(monthDate(period))
  if (period?.quarter !== undefined) return `Q${period.quarter} ${period.year}`
  return key
}

/**
 * About how wide one month's label is on the axis, with the gap MUI keeps beside it ("márc.", "szept.", or a year under its month): what
 * `labelStep` divides the axis' room by.
 */
export const MONTH_LABEL_PX = 48

/** Every how many periods the axis names one, so the named ones fit side by side in `room` px (1 while the room is not known yet). */
export function labelStep(periods: number, room: number): number {
  return room > 0 ? Math.max(1, Math.ceil((periods * MONTH_LABEL_PX) / room)) : 1
}

/**
 * The periods as the chart's axis names them (#159), only in a chart by month or quarter (a category, "2025-03" too, is named as it is):
 * every `step`-th period, short so more of them fit: a month alone ("Nov"), with its year under it on the first one named and on every
 * named one whose year differs from the one named before ("Nov\n2025", "Feb", "May", "Aug", "Nov", "Feb\n2026": a second line, so the
 * label is no wider than its month), so a year never goes unnamed whatever the axis leaves out; a quarter "Q4 ’25". The others are not in
 * the map (the axis names nothing there). The tooltip and the table keep `periodLabel`.
 */
export function shortPeriodLabels(keys: readonly string[], language: string, grouping: ChartGrouping, step = 1): Map<string, string> {
  if (grouping !== 'MONTH' && grouping !== 'QUARTER') return new Map()
  const monthOnly = new Intl.DateTimeFormat(language, { month: 'short', timeZone: 'UTC' })
  const labels = new Map<string, string>()
  let lastYear: string | undefined
  keys.forEach((key, i) => {
    const period = parsePeriod(key)
    if (!period || i % step !== 0) return
    if (period.quarter !== undefined) labels.set(key, `Q${period.quarter} ’${period.year.slice(2)}`)
    else labels.set(key, period.year !== lastYear ? `${monthOnly.format(monthDate(period))}\n${period.year}` : monthOnly.format(monthDate(period)))
    lastYear = period.year
  })
  return labels
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

/** The stack a series' bars or areas go into: its currency's, the one key `ChartView` gives MUI and `barPlaces` groups by. */
export const stackOf = (s: Series) => `currency:${s.currency ?? ''}`

/**
 * The bars of each period: only the groups that have a value there (#159, the owner's decision), side by side in the chart's order. A
 * group is a currency when the bars are stacked (its series make one stack, `stackOf`), else a series. `at` is the place of a series' bar
 * in a period (`index` into `periods`, the chart's rows), null where it has nothing; all of them worked out once, so a chart of many
 * periods and series costs one pass, not one per bar.
 */
export function barPlaces(data: Pick<ChartData, 'series'>, periods: readonly string[], stacked: boolean): { at: (s: Series, index: number) => BarPlace | null } {
  const groupKey = (s: Series) => (stacked ? stackOf(s) : seriesId(s))
  const keys = [...new Set(data.series.map(groupKey))]
  const value = data.series.map((s) => new Map(s.points.map((p) => [p.key, p.value ?? 0])))
  const places = periods.map((period) => {
    const has = data.series.map((_, j) => (value[j].get(period) ?? 0) > 0)
    const present = keys.filter((key) => data.series.some((s, j) => has[j] && groupKey(s) === key))
    // The stack's top is its last series (in the chart's order) with a value here: the series are stacked in that order.
    const tops = new Map(data.series.flatMap((s, j) => (has[j] ? [[groupKey(s), s] as const] : [])))
    return new Map(
      data.series.flatMap((s, j) => (has[j] ? [[s, { place: present.indexOf(groupKey(s)), of: present.length, top: tops.get(groupKey(s)) === s }] as const] : [])),
    )
  })
  return { at: (s, index) => places[index]?.get(s) ?? null }
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
