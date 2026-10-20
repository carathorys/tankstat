import { useTranslation } from 'react-i18next'
import type { DistanceUnit } from '../gql/generated.ts'

/** The odometer field's label in the vehicle's unit (also what announcements call the field). */
export function useOdometerLabel(unit: DistanceUnit, optional?: boolean) {
  const { t } = useTranslation()
  const unitName = t(`units.distance.${unit}`).toLowerCase()
  return optional ? t('expenses.fields.odometer', { unit: unitName }) : t('refuelings.fields.odometer', { unit: unitName })
}
