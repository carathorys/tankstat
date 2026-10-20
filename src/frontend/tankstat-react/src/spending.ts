import type { useFormat } from './i18n/format.ts'

/** What was spent in one currency this month and last month (`VehicleSummary.spending`). */
export interface CurrencySpend {
  currency: string
  thisMonth: number
  lastMonth: number
}

/**
 * A month's spending in every currency it was spent in, main currency first ("52,000 Ft · €73.90"): money is never converted, so an
 * amount in another currency is shown next to it instead of being left out. Nothing spent shows the main currency's zero; null without
 * any currency (a vehicle without costs).
 */
export function spentText(spending: readonly CurrencySpend[], month: 'thisMonth' | 'lastMonth', format: ReturnType<typeof useFormat>): string | null {
  if (spending.length === 0) return null
  const spent = spending.filter((s) => s[month] !== 0)
  return (spent.length > 0 ? spent : spending.slice(0, 1)).map((s) => format.money(s[month], s.currency)).join(' · ')
}
