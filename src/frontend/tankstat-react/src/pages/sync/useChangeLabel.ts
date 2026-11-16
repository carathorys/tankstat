import { useTranslation } from 'react-i18next'
import { useFormat } from '../../i18n/format.ts'
import type { WaitingChange } from '../../offline/waitingChanges.ts'
import type { Units } from './useFieldText.ts'

/**
 * A waiting change in words, as Waiting to sync names it: what it does (`kind`), what it carries or changes (`parts`), and the two in a
 * line (`label`: "Edit expense, 1 Sep 2026, Oil change, €35.00"). Also the note and the confirmation of an edit that leaves changes
 * behind (#330).
 */
export function useChangeLabel() {
  const { t } = useTranslation()
  const format = useFormat()
  return (item: WaitingChange, units: Partial<Units>) => {
    const { change } = item
    // Every entity and action a change can have has its text (a schedule is deleted, not trashed; only schedules are marked done).
    const kind: string = t(`sync.kinds.${change.entity}.${change.action}` as 'sync.kinds.refuelings.add')
    const money = (amount: number | null | undefined) => (amount != null && item.currency ? format.money(amount, item.currency) : null)
    const parts = [
      item.date ? format.date(item.date) : null,
      item.name ?? item.title ?? null,
      item.titles?.filter(Boolean).length ? format.list(item.titles.filter(Boolean)) : null,
      item.volume != null && units.volume ? format.volume(item.volume, units.volume) : null,
      money(item.totalCost) ?? money(item.amount),
    ].filter((p): p is string => !!p)
    return { kind, parts, label: parts.length ? `${kind}, ${parts.join(', ')}` : kind }
  }
}
