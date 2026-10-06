import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Field } from '../forms/Field.tsx'
import { FieldInput } from '../forms/FieldInput.tsx'
import { useFormat } from '../i18n/format.ts'
import type { DistanceUnit } from '../gql/generated.ts'
import { useOdometerLabel } from './useOdometerLabel.ts'

/**
 * Whole-number reading in the vehicle's own distance unit. Reusable for any odometer-based entry (refuelings, later inspections).
 * Uncontrolled with `defaultValue`, or controlled with `value` and `onChange` (when a photo can fill it in).
 */
export function OdometerField({
  unit,
  defaultValue,
  value,
  onChange,
  last,
  optional,
  required = !optional,
  extra,
}: {
  unit: DistanceUnit
  defaultValue?: number
  value?: string
  onChange?: (value: string) => void
  /** The latest reading of the vehicle, shown as a hint. */
  last?: { value: number; date: string } | null
  /** The reading may be left empty (an expense where the odometer was not noted). */
  optional?: boolean
  /** Whether it must be filled in; by default unless it is optional (a reading a photo will provide may also be left empty). */
  required?: boolean
  /** Shown under the hint, e.g. what a photo showed. */
  extra?: ReactNode
}) {
  const { t } = useTranslation()
  const { distance, date } = useFormat()
  const label = useOdometerLabel(unit, optional)
  return (
    <Field
      name="odometer"
      label={label}
      required={required}
      hint={last ? t('refuelings.hints.lastReading', { value: distance(last.value, unit), date: date(last.date) }) : undefined}
      invalid={{ message: t('errors.odometer.negative'), test: (v) => v !== '' && !/^\d+$/.test(v.trim()) }}
      extra={extra}
    >
      {value === undefined ? (
        <FieldInput inputMode="numeric" autoComplete="off" defaultValue={defaultValue?.toString() ?? ''} />
      ) : (
        <FieldInput inputMode="numeric" autoComplete="off" value={value} onChange={(e) => onChange?.(e.target.value)} />
      )}
    </Field>
  )
}
