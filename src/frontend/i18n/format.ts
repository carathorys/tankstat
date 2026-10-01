import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import type { DistanceUnit, VolumeUnit } from '../gql/generated.ts'

const DISTANCE_UNITS: Record<DistanceUnit, 'kilometer' | 'mile'> = { KILOMETERS: 'kilometer', MILES: 'mile' }

/**
 * Dates, numbers, money and measurements in the selected UI language, always through Intl. Numbers are stored exactly as
 * entered in the vehicle's own units, so the unit decides how a value is shown, never a conversion.
 */
export function useFormat() {
  const { i18n, t } = useTranslation()
  const language = i18n.language

  return useMemo(() => {
    const number = (value: number, options?: Intl.NumberFormatOptions) => new Intl.NumberFormat(language, options).format(value)
    return {
      number,
      dateTime: (iso: string) => new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(iso)),
      /** A calendar date such as "2026-09-01" (no time zone shifts it to another day). */
      date: (isoDate: string) => new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeZone: 'UTC' }).format(new Date(`${isoDate}T00:00:00Z`)),
      /** Amount in the currency it was paid in; an unknown code falls back to "12.50 XYZ". */
      money: (amount: number, currency: string) => {
        try {
          return new Intl.NumberFormat(language, { style: 'currency', currency }).format(amount)
        } catch {
          return `${number(amount, { minimumFractionDigits: 2 })} ${currency}`
        }
      },
      distance: (value: number, unit: DistanceUnit) =>
        new Intl.NumberFormat(language, { style: 'unit', unit: DISTANCE_UNITS[unit], unitDisplay: 'short', maximumFractionDigits: 0 }).format(value),
      volume: (value: number, unit: VolumeUnit) => `${number(value, { maximumFractionDigits: 2 })} ${t(`units.volumeShort.${unit}`)}`,
      pricePerUnit: (value: number, currency: string, unit: VolumeUnit) => `${(() => {
        try {
          return new Intl.NumberFormat(language, { style: 'currency', currency, maximumFractionDigits: 3 }).format(value)
        } catch {
          return `${number(value, { maximumFractionDigits: 3 })} ${currency}`
        }
      })()} / ${t(`units.volumeShort.${unit}`)}`,
    }
  }, [language, t])
}

/** Parses what a person typed: both "12.5" and "12,5" are accepted (many languages write decimals with a comma). */
export function parseDecimal(text: string): number | undefined {
  const normalized = text.trim().replace(',', '.')
  if (!/^\d+(\.\d+)?$/.test(normalized)) return undefined
  return Number(normalized)
}
