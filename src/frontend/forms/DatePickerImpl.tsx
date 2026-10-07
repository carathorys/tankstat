import { AdapterDayjs } from '@mui/x-date-pickers/AdapterDayjs'
import { DatePicker } from '@mui/x-date-pickers/DatePicker'
import { LocalizationProvider } from '@mui/x-date-pickers/LocalizationProvider'
import { huHU } from '@mui/x-date-pickers/locales'
import dayjs, { type Dayjs } from 'dayjs'
import 'dayjs/locale/en-gb'
import 'dayjs/locale/hu'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { CalendarIcon } from '../theme/icons.tsx'
import { pickerLocale } from './dates.ts'

const ISO = 'YYYY-MM-DD'
const HUNGARIAN = huHU.components.MuiLocalizationProvider.defaultProps.localeText

/**
 * The picker behind FieldDate (its own chunk, with dayjs): the day in the UI language's order, typed section by section or picked from a
 * calendar. It keeps a partly typed day while the field holds '' (nothing valid yet).
 */
export default function DatePickerImpl({
  iso,
  disableFuture,
  labelId,
  describedBy,
  invalid,
  onChange,
}: {
  iso: string
  disableFuture: boolean
  labelId: string
  describedBy: string | undefined
  invalid: boolean
  onChange: (iso: string) => void
}) {
  const { i18n } = useTranslation()
  const locale = pickerLocale(i18n.language)
  const [draft, setDraft] = useState<Dayjs | null>(() => (iso ? dayjs(iso) : null))
  const [reported, setReported] = useState(iso)
  // A day set from outside (a photo, the form's start values) replaces what the picker holds; the day it reported itself does not.
  if (iso !== reported) {
    setReported(iso)
    setDraft(iso ? dayjs(iso) : null)
  }

  return (
    <LocalizationProvider dateAdapter={AdapterDayjs} adapterLocale={locale} localeText={locale === 'hu' ? HUNGARIAN : undefined}>
      <DatePicker
        value={draft}
        onChange={(next) => {
          const valid = next !== null && next.isValid()
          const out = valid ? next.format(ISO) : ''
          setDraft(next)
          setReported(out)
          onChange(out)
        }}
        disableFuture={disableFuture}
        slots={{ openPickerIcon: CalendarIcon }}
        slotProps={{
          openPickerButton: { size: 'small' },
          textField: {
            fullWidth: true,
            error: invalid,
            // The Field's label and messages name and describe the typed sections (the group), not MUI's own label.
            slotProps: { input: { 'aria-labelledby': labelId, 'aria-describedby': describedBy } },
          },
        }}
      />
    </LocalizationProvider>
  )
}
