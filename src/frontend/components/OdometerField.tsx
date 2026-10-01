import { TextField } from '@radix-ui/themes'
import { useTranslation } from 'react-i18next'
import { Field } from '../forms.tsx'
import { useFormat } from '../i18n/format.ts'
import type { DistanceUnit } from '../gql/generated.ts'

/** Whole-number reading in the vehicle's own distance unit. Reusable for any odometer-based entry (refuelings, later inspections). */
export function OdometerField({
  unit,
  defaultValue,
  last,
  optional,
}: {
  unit: DistanceUnit
  defaultValue?: number
  /** The latest reading of the vehicle, shown as a hint. */
  last?: { value: number; date: string } | null
  /** The reading may be left empty (an expense where the odometer was not noted). */
  optional?: boolean
}) {
  const { t } = useTranslation()
  const { distance, date } = useFormat()
  return (
    <Field
      name="odometer"
      label={t(optional ? 'expenses.fields.odometer' : 'refuelings.fields.odometer', { unit: t(`units.distance.${unit}`).toLowerCase() })}
      required={!optional}
      hint={last ? t('refuelings.hints.lastReading', { value: distance(last.value, unit), date: date(last.date) }) : undefined}
      invalid={{ message: t('errors.odometer.negative'), test: (v) => v !== '' && !/^\d+$/.test(v.trim()) }}
    >
      <TextField.Root inputMode="numeric" autoComplete="off" defaultValue={defaultValue?.toString() ?? ''} />
    </Field>
  )
}
