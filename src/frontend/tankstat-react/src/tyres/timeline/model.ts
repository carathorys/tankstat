import type { TimelineChoices } from '../../settings/timelineChoices.ts'
import { isSwapLine } from '../entries.ts'
import type { TyreEntry, Tyres, TyreSetInfo } from '../tyres.ts'

/**
 * The tyre timeline's arithmetic (#332), pure: days as whole numbers, the periods, the gaps between them, the swaps on their
 * boundaries, the range and the scale. Nothing here draws or asks.
 */

const DAY = 864e5

/** A day (`yyyy-MM-dd`) as a whole number of days, and back. */
export const dayOf = (iso: string): number => Math.round(Date.parse(`${iso}T00:00:00Z`) / DAY)
export const isoOf = (day: number): string => new Date(day * DAY).toISOString().slice(0, 10)

/** One time on the car, as the picture and the table draw it. */
export interface Period {
  id: string
  setId: string
  from: number
  /** Null while the set is on the car. */
  until: number | null
  info: TyreSetInfo['installations'][number]
}

/** Every live period of the vehicle, oldest first. */
export function periodsOf(tyres: Tyres): Period[] {
  return tyres.sets
    .flatMap((s) => s.installations.map((info) => ({ id: info.installation.id, setId: s.set.id, from: dayOf(info.installation.from), until: info.installation.until ? dayOf(info.installation.until) : null, info })))
    .sort((a, b) => a.from - b.from || (a.id < b.id ? -1 : 1))
}

/** A stretch with no set recorded on the car: from the day one came off to the day the next went on. */
export interface Gap {
  from: number
  until: number
  /** The periods on either side. */
  before: Period
  after: Period
}

/**
 * The gaps between the periods: worked out from all of them, before any filter (a hidden set's days are no gap). A gap ends at the next
 * period; the time after the last one, with nothing on the car since, is not one.
 */
export function gapsOf(periods: readonly Period[]): Gap[] {
  const gaps: Gap[] = []
  let last: Period | undefined
  for (const p of periods) {
    if (last && last.until !== null && p.from > last.until) gaps.push({ from: last.until, until: p.from, before: last, after: p })
    // The one reaching furthest counts: periods never overlap, but one may end the day another starts.
    if (!last || last.until !== null && (p.until === null || p.until > last.until)) last = p
  }
  return gaps
}

/** A boundary: a period that starts the day another ends (another set's: a swap; its own set's: positions changed), with its line. */
export interface Boundary {
  day: number
  before: Period
  after: Period
  /** The tyre log's line of the period that starts here: what moving the swap changes. */
  line: TyreEntry
}

export function boundariesOf(periods: readonly Period[], entries: readonly TyreEntry[]): Boundary[] {
  const lines = new Map(entries.filter((e) => isSwapLine(e) && e.installationId).map((e) => [e.installationId!, e]))
  const out: Boundary[] = []
  for (const after of periods) {
    const before = periods.find((p) => p !== after && p.until === after.from)
    const line = lines.get(after.id)
    if (before && line) out.push({ day: after.from, before, after, line })
  }
  return out
}

/** The sets drawn: not hidden, and a retired one only while retired sets are shown. */
export function shownSets(tyres: Tyres, choices: TimelineChoices): TyreSetInfo[] {
  const retired = choices.layers.includes('RETIRED_SETS')
  return tyres.sets.filter((s) => !choices.hiddenSets.includes(s.set.id) && (retired || !s.set.isRetired))
}

/** The time before tracking of a set: from *in use since* to where its rough figure stops, or to its first period. */
export function beforeTracking(info: TyreSetInfo, periods: readonly Period[]): { from: number; until: number } | null {
  const { set } = info
  if (set.roughDistance <= 0) return null
  const first = periods.find((p) => p.setId === set.id)
  const stop = set.roughUntil ? dayOf(set.roughUntil) : first?.from
  if (stop === undefined) return null
  const until = first ? Math.min(stop, first.from) : stop
  const from = dayOf(set.since)
  return until > from ? { from, until } : null
}

const monthStart = (day: number, addMonths = 0): number => {
  const d = new Date(day * DAY)
  return Math.round(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + addMonths, 1) / DAY)
}

/** The days the picture spans: from the chosen range's start (whole months) to the end of next month, so today has room to its right. */
export function spanOf(range: TimelineChoices['range'], tyres: Tyres, periods: readonly Period[], today: number): { start: number; end: number } {
  const end = monthStart(today, 2)
  if (range === 'TWELVE_MONTHS') return { start: monthStart(today, -12), end }
  if (range === 'TWO_YEARS') return { start: monthStart(today, -24), end }
  const earliest = Math.min(today, ...periods.map((p) => p.from), ...tyres.sets.map((s) => dayOf(s.set.since)))
  return { start: monthStart(earliest), end }
}

export const ZOOMS = [1, 2, 4, 8, 16] as const
/** The fewest pixels a day takes at 1×: a year is then about 200 px, which a phone scrolls. */
export const MIN_PX_PER_DAY = 0.55

/** Pixels per day: the span fitted to the room at 1× (never under the minimum), times the zoom. */
export const pxPerDay = (room: number, span: { start: number; end: number }, zoom: number): number =>
  Math.max(MIN_PX_PER_DAY, room / Math.max(1, span.end - span.start)) * zoom

/** A tick of the header: a year's first day, or a month's, as many as have room for their names. */
export interface Tick {
  day: number
  year: boolean
  month: number
}

/** The fewest pixels between two ticks' labels ("Sep", "2026"). */
const TICK_ROOM = 34

export function ticksOf(span: { start: number; end: number }, perDay: number): Tick[] {
  // Every month where a month is wide enough for its name, else every quarter, half year or year.
  const step = [1, 3, 6, 12].find((months) => months * 30.4 * perDay >= TICK_ROOM) ?? 12
  const ticks: Tick[] = []
  for (let day = monthStart(span.start); day < span.end; day = monthStart(day, 1)) {
    const month = new Date(day * DAY).getUTCMonth()
    if (day >= span.start && month % step === 0) ticks.push({ day, year: month === 0, month: month + 1 })
  }
  return ticks
}

/** The day under a drag: where the swap was plus the distance dragged, in whole days. */
export const draggedDay = (from: number, dx: number, perDay: number): number => from + Math.round(dx / perDay)

/** The lines drawn as marks: what was done to a shown set that is no swap (repairs and costs). */
export const marksOf = (entries: readonly TyreEntry[], setId: string): TyreEntry[] => entries.filter((e) => e.setId === setId && !isSwapLine(e))
