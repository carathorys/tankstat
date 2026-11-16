import type { TyreSeason } from '../gql/generated.ts'

/**
 * The months a tyre set is usually on the car (#291; 1 = January), the server's `TyreMonths`: a season's (summer April to October, winter
 * November to March, all-season the whole year), or a set's own, which it keeps only when they differ from its season's (`usualMonths`
 * null: the season's), so a set on its season's follows a change of season.
 */
export const SEASON_MONTHS: Record<TyreSeason, readonly number[]> = {
  SUMMER: [4, 5, 6, 7, 8, 9, 10],
  WINTER: [1, 2, 3, 11, 12],
  ALL_SEASON: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12],
}

/** A list of months in calendar order, each once; what is no month (the server refuses it) left out. */
export const sortedMonths = (months: readonly number[]): number[] => [...new Set(months)].filter((m) => Number.isInteger(m) && m >= 1 && m <= 12).sort((a, b) => a - b)

const same = (a: readonly number[], b: readonly number[]) => a.length === b.length && a.every((m, n) => m === b[n])

/** The months a set is usually on the car: its own, else its season's. */
export const monthsOf = (set: { season: TyreSeason; usualMonths?: readonly number[] | null }): number[] => [...(set.usualMonths ?? SEASON_MONTHS[set.season])]

/** A season's usual month to fit its tyres (#292), as the server's `TyreReminder.DefaultMonth`: winter November, summer March, all-season none. */
export const seasonReminderMonth = (season: TyreSeason): number | null => (season === 'WINTER' ? 11 : season === 'SUMMER' ? 3 : null)

/** What a set keeps of the reminder's month chosen (0: no reminder): none (null) when it is its season's. */
export const ownReminderMonth = (season: TyreSeason, month: number): number | null => (month === (seasonReminderMonth(season) ?? 0) ? null : month)

/** The month a set is reminded from, its own or its season's; null: no reminder. */
export const reminderMonthOf = (set: { season: TyreSeason; reminderMonth?: number | null }): number | null =>
  set.reminderMonth == null ? seasonReminderMonth(set.season) : set.reminderMonth === 0 ? null : set.reminderMonth

/** The season of a reminder's month on a day (ISO), as the server's `TyreReminder.SeasonOf`: the latest 1st of that month at or before it. */
export function reminderSeasonOf(month: number, today: string): string {
  const [year, thisMonth] = [Number(today.slice(0, 4)), Number(today.slice(5, 7))]
  return `${String(month <= thisMonth ? year : year - 1).padStart(4, '0')}-${String(month).padStart(2, '0')}-01`
}

/** What a set keeps of the months chosen: none (null) when they are its season's. */
export function ownMonths(season: TyreSeason, months: readonly number[]): number[] | null {
  const sorted = sortedMonths(months)
  return same(sorted, SEASON_MONTHS[season]) ? null : sorted
}

/** Whether the months chosen are the season's own. */
export const isSeasons = (season: TyreSeason, months: readonly number[]) => ownMonths(season, months) === null
