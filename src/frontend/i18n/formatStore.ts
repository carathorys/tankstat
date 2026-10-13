import { useSyncExternalStore } from 'react'
import type { DateFormat, TimeFormat } from '../gql/generated.ts'

/**
 * How times and dates are written: the user's choice (null: none), and the instance's default for a user who made none (null: as the
 * language writes them). The browser keeps a copy, so the first paint already writes them the chosen way; `UiSettingsProvider` brings the
 * account's choice and the instance's defaults.
 */
export interface Formats {
  time: TimeFormat | null
  date: DateFormat | null
  defaultTime: TimeFormat | null
  defaultDate: DateFormat | null
}

export const TIME_FORMATS: readonly TimeFormat[] = ['TWENTY_FOUR_HOUR', 'TWELVE_HOUR']
export const DATE_FORMATS: readonly DateFormat[] = ['ISO', 'DAY_MONTH_YEAR_DOTS', 'DAY_MONTH_YEAR_SLASHES', 'MONTH_DAY_YEAR', 'YEAR_MONTH_DAY_DOTS']

const KEY = 'tankstat.formats'
const NONE: Formats = { time: null, date: null, defaultTime: null, defaultDate: null }

const pick = <T extends string>(value: unknown, allowed: readonly T[]): T | null => (allowed.includes(value as T) ? (value as T) : null)

function read(): Formats {
  try {
    const stored = JSON.parse(localStorage.getItem(KEY) ?? 'null') as Partial<Record<keyof Formats, unknown>> | null
    if (!stored) return NONE
    return {
      time: pick(stored.time, TIME_FORMATS),
      date: pick(stored.date, DATE_FORMATS),
      defaultTime: pick(stored.defaultTime, TIME_FORMATS),
      defaultDate: pick(stored.defaultDate, DATE_FORMATS),
    }
  } catch {
    return NONE // nothing readable kept: as the language writes them
  }
}

let current: Formats | undefined
const listeners = new Set<() => void>()

export function currentFormats(): Formats {
  return (current ??= read())
}

/**
 * Only the formats this page knows, whoever hands them over: one a newer server sends (a format added since, while this tab waits for
 * its update) is none here, so the language's own, never a page that cannot draw a date.
 */
function known(change: Partial<Formats>): Partial<Formats> {
  const out: Partial<Formats> = {}
  if ('time' in change) out.time = pick(change.time, TIME_FORMATS)
  if ('date' in change) out.date = pick(change.date, DATE_FORMATS)
  if ('defaultTime' in change) out.defaultTime = pick(change.defaultTime, TIME_FORMATS)
  if ('defaultDate' in change) out.defaultDate = pick(change.defaultDate, DATE_FORMATS)
  return out
}

/** Changes what is given, keeps the browser's copy (a storage failure only loses the copy) and redraws whatever writes a time or date. */
export function setFormats(change: Partial<Formats>) {
  const next = { ...currentFormats(), ...known(change) }
  if (Object.keys(next).every((k) => next[k as keyof Formats] === currentFormats()[k as keyof Formats])) return
  current = next
  try {
    localStorage.setItem(KEY, JSON.stringify(next))
  } catch {
    // private mode or storage blocked: this page still writes them so
  }
  listeners.forEach((listener) => listener())
}

/** Tests: read the browser's copy afresh (nothing chosen once it is cleared), as a new page. */
export function resetFormats() {
  current = undefined
  listeners.forEach((listener) => listener())
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function useFormats(): Formats {
  return useSyncExternalStore(subscribe, currentFormats, () => NONE)
}
