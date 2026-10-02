import { useTranslation } from 'react-i18next'
import type { RecurrenceStatusLike } from '../components/RecurringStatus.tsx'
import type { DistanceUnit } from '../gql/generated.ts'
import { useFormat } from '../i18n/format.ts'

/**
 * How far away the limit that decides the state is: "in 12 days", "300 km over". Undefined when there is nothing to say (an odometer-based
 * schedule of a vehicle that has no reading yet).
 */
export function useDueText(unit: DistanceUnit) {
  const { t } = useTranslation()
  const { distance } = useFormat()
  return (s: RecurrenceStatusLike): string | undefined => {
    if (s.limit === 'TIME' && s.daysLeft !== null) {
      if (s.daysLeft === 0) return t('recurring.due.today')
      return s.daysLeft > 0 ? t('recurring.due.inDays', { count: s.daysLeft }) : t('recurring.due.overdueDays', { count: -s.daysLeft })
    }
    if (s.limit === 'ODOMETER' && s.distanceLeft !== null) {
      return s.distanceLeft >= 0
        ? t('recurring.due.inDistance', { distance: distance(s.distanceLeft, unit) })
        : t('recurring.due.overdueDistance', { distance: distance(-s.distanceLeft, unit) })
    }
    return undefined
  }
}
