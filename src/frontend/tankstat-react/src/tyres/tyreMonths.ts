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

/** What a set keeps of the months chosen: none (null) when they are its season's. */
export function ownMonths(season: TyreSeason, months: readonly number[]): number[] | null {
  const sorted = sortedMonths(months)
  return same(sorted, SEASON_MONTHS[season]) ? null : sorted
}

/** Whether the months chosen are the season's own. */
export const isSeasons = (season: TyreSeason, months: readonly number[]) => ownMonths(season, months) === null
