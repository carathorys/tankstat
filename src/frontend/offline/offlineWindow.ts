/**
 * The offline window: which logs of a vehicle this device downloads. The account keeps it as a rule (the server only checks its shape,
 * `Domain/Settings/OfflineWindow.cs`); the device turns a rule into a start date on its own clock at every download, so time zones never
 * reach the server:
 * `none` (the vehicle and its schedules only), `all`, `thisYear`, `thisAndLastYear`, `from:yyyy-MM-dd`, or `span:` an ISO 8601 duration
 * back from now (`span:P2Y6M4DT2H48M12S`).
 */
export interface Span {
  years: number
  months: number
  days: number
  hours: number
  minutes: number
  seconds: number
}

export type OfflineWindow =
  | { kind: 'none' }
  | { kind: 'all' }
  | { kind: 'thisYear' }
  | { kind: 'thisAndLastYear' }
  | { kind: 'from'; date: string }
  | ({ kind: 'span' } & Span)

/** Without a choice: the last two months (the server's `OfflineWindow.Default`). */
export const DEFAULT_RULE = 'span:P2M'

const DURATION = /^P(?:(\d{1,4})Y)?(?:(\d{1,5})M)?(?:(\d{1,6})D)?(?:T(?:(\d{1,7})H)?(?:(\d{1,9})M)?(?:(\d{1,10})S)?)?$/
const DATE = /^(\d{4})-(\d{2})-(\d{2})$/

export function parseRule(rule: string | null | undefined): OfflineWindow | null {
  const value = rule?.trim() ?? ''
  if (value === 'none' || value === 'all' || value === 'thisYear' || value === 'thisAndLastYear') return { kind: value }
  if (value.startsWith('from:')) {
    const date = value.slice(5)
    return isDate(date) ? { kind: 'from', date } : null
  }
  if (!value.startsWith('span:')) return null
  const text = value.slice(5)
  const match = DURATION.exec(text)
  if (!match || text.endsWith('T')) return null
  const [years, months, days, hours, minutes, seconds] = match.slice(1).map((n) => (n === undefined ? 0 : Number(n)))
  const span = { years, months, days, hours, minutes, seconds }
  return Object.values(span).some((n) => n > 0) ? { kind: 'span', ...span } : null
}

export function formatRule(window: OfflineWindow): string {
  switch (window.kind) {
    case 'from':
      return `from:${window.date}`
    case 'span': {
      const date = `${part(window.years, 'Y')}${part(window.months, 'M')}${part(window.days, 'D')}`
      const time = `${part(window.hours, 'H')}${part(window.minutes, 'M')}${part(window.seconds, 'S')}`
      return `span:P${date}${time ? `T${time}` : ''}`
    }
    default:
      return window.kind
  }
}

/**
 * Where the window starts today, as a local date (`yyyy-MM-dd`): null downloads every log, `false` none. A span counts back on the
 * calendar (a month back from 31 March is the last day of February) and its hours, minutes and seconds round down to the whole day.
 */
export function fromDate(rule: string | null | undefined, now: Date = new Date()): string | null | false {
  const window = parseRule(rule) ?? parseRule(DEFAULT_RULE)!
  switch (window.kind) {
    case 'none':
      return false
    case 'all':
      return null
    case 'thisYear':
      return iso(now.getFullYear(), 1, 1)
    case 'thisAndLastYear':
      return iso(now.getFullYear() - 1, 1, 1)
    case 'from':
      return window.date
    case 'span': {
      const totalMonths = now.getFullYear() * 12 + now.getMonth() - (window.years * 12 + window.months)
      const year = Math.floor(totalMonths / 12)
      const month = totalMonths - year * 12
      const day = Math.min(now.getDate(), daysIn(year, month))
      const back = new Date(year, month, day, now.getHours(), now.getMinutes(), now.getSeconds())
      back.setDate(back.getDate() - window.days)
      back.setTime(back.getTime() - ((window.hours * 60 + window.minutes) * 60 + window.seconds) * 1000)
      return iso(back.getFullYear(), back.getMonth() + 1, back.getDate())
    }
  }
}

/** Whether a window starting at `a` takes in fewer days than one starting at `b` (null: all of them; `false`: none). */
export function narrower(a: string | null | false, b: string | null | false): boolean {
  if (a === b) return false
  if (a === false) return true
  if (b === false) return false
  if (a === null) return false
  if (b === null) return true
  return a > b
}

const part = (n: number, unit: string) => (n > 0 ? `${n}${unit}` : '')
const pad = (n: number) => String(n).padStart(2, '0')
const iso = (year: number, month: number, day: number) => `${String(year).padStart(4, '0')}-${pad(month)}-${pad(day)}`
const daysIn = (year: number, month: number) => new Date(year, month + 1, 0).getDate()

function isDate(text: string): boolean {
  const match = DATE.exec(text)
  if (!match) return false
  const [year, month, day] = match.slice(1).map(Number)
  return year >= 1900 && month >= 1 && month <= 12 && day >= 1 && day <= daysIn(year, month - 1)
}
