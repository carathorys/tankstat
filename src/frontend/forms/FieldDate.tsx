import Box from '@mui/material/Box'
import { lazy, Suspense, useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { visuallyHidden } from '../components/visuallyHidden.ts'
import { todayIso } from './dates.ts'
import { useFieldControl } from './fieldContext.ts'

// MUI X's picker and dayjs load with the first date field shown (a dialog), not with the page.
const DatePickerImpl = lazy(() => import('./DatePickerImpl.tsx'))

/**
 * A day (YYYY-MM-DD) in a Field: MUI X's date picker for typing or picking it, plus a hidden input with the ISO value that the form reads
 * (FormData) and the browser validates (required). A partly typed day, and a future one with `disableFuture`, are problems the field
 * shows. Controlled with `value` + `onChange`, or uncontrolled with `defaultValue`.
 */
export function FieldDate({
  value,
  defaultValue = '',
  onChange,
  disableFuture = false,
}: {
  value?: string
  defaultValue?: string
  onChange?: (iso: string) => void
  /** Only days up to today (something that already happened). */
  disableFuture?: boolean
}) {
  const { t } = useTranslation()
  const field = useFieldControl()
  const [own, setOwn] = useState(defaultValue)
  const box = useRef<HTMLDivElement>(null)
  const hidden = useRef<HTMLInputElement | null>(null)
  const iso = value ?? own

  // The field asks when it checks: a day with only some parts typed has no value yet (the picker reports nothing until it is whole),
  // and a future one may not be allowed.
  const { composite, bindControl } = field
  useLayoutEffect(
    () =>
      composite({
        focus: () => box.current?.querySelector<HTMLElement>('[role="spinbutton"]')?.focus(),
        problem: () => {
          const day = hidden.current?.value ?? ''
          const typed = [...(box.current?.querySelectorAll('[role="spinbutton"]') ?? [])].some((part) => part.hasAttribute('aria-valuenow'))
          if (day === '' && typed) return t('forms.dateIncomplete')
          return disableFuture && day > todayIso() ? t('forms.dateFuture') : null
        },
      }),
    [composite, disableFuture, t],
  )
  const bind = useCallback(
    (element: HTMLInputElement | null) => {
      hidden.current = element
      bindControl(element)
    },
    [bindControl],
  )

  // A new day in the hidden input is a finished edit, as on a native date input: the field checks it and shows what is wrong.
  const lastIso = useRef(iso)
  useEffect(() => {
    if (lastIso.current === iso) return
    lastIso.current = iso
    hidden.current?.dispatchEvent(new Event('change', { bubbles: true }))
  }, [iso])

  return (
    <Box ref={box} data-date-field="" sx={{ position: 'relative' }}>
      <Suspense fallback={<Box sx={{ height: 32 }} />}>
        <DatePickerImpl
          iso={iso}
          disableFuture={disableFuture}
          labelId={field.labelId}
          describedBy={field.describedBy}
          invalid={field.invalid}
          onChange={(next) => {
            if (value === undefined) setOwn(next)
            onChange?.(next)
          }}
        />
      </Suspense>
      <input
        ref={bind}
        name={field.name}
        value={iso}
        required={field.required}
        onChange={() => undefined}
        tabIndex={-1}
        aria-hidden
        style={visuallyHidden}
      />
    </Box>
  )
}
