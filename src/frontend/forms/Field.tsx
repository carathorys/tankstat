import FormControl from '@mui/material/FormControl'
import FormHelperText, { type FormHelperTextProps } from '@mui/material/FormHelperText'
import FormLabel from '@mui/material/FormLabel'
import { useCallback, useContext, useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { FieldContext, type CompositeControl, type FieldControl, type FieldMessages } from './fieldContext.ts'
import { FormContext } from './formContext.ts'

type Control = HTMLInputElement | HTMLTextAreaElement
/** What is wrong with a value, if anything: the browser's checks first, then the field's own. */
type Problem = 'valueMissing' | 'typeMismatch' | { custom: string }

const SLOTS = { hint: 0, note: 1, error: 2 } as const

/** `customFirst`: the control knows better why it has no value (a day only partly typed is not "missing"). */
function problemOf(control: Control, custom: string | null, customFirst: boolean): Problem | null {
  control.setCustomValidity(custom ?? '')
  if (customFirst && custom !== null) return { custom }
  if (control.validity.valueMissing) return 'valueMissing'
  if (control.validity.typeMismatch) return 'typeMismatch'
  return custom === null ? null : { custom }
}

/**
 * One labelled control of a form: the label above it, an optional always-visible hint, `extra` notes (e.g. what a photo showed) and the
 * error message, all linked to the control (aria-describedby, in that order). `children` is the control (FieldInput, FieldDate, ...).
 * A missing value and `invalid` (a check on the typed value) block the form; their messages show after a submit or a finished edit (the
 * native change event) and hide while the user types.
 */
export function Field({
  name,
  label,
  hint,
  required = false,
  invalid,
  typeMismatch,
  extra,
  children,
}: {
  name: string
  label: string
  hint?: string
  required?: boolean
  invalid?: { message: string; test: (value: string) => boolean }
  /** The message for a value the input type does not accept (an e-mail address). */
  typeMismatch?: string
  /** More under the hint, inside the field (its FieldMessages are linked to the control too), e.g. what a photo showed. */
  extra?: ReactNode
  children: ReactNode
}) {
  const { t } = useTranslation()
  const id = useId()
  const labelId = `${id}-label`
  const form = useContext(FormContext)
  const [control, setControl] = useState<Control | null>(null)
  const [isComposite, setComposite] = useState(false)
  const compositeControl = useRef<CompositeControl | null>(null)
  const [shown, setShown] = useState<Problem | null>(null)
  const [messages, setMessages] = useState<{ id: string; slot: number; seq: number }[]>([])
  const seq = useRef(0)

  const check = useCallback(() => {
    if (!control) return null
    const own = compositeControl.current?.problem() ?? null
    return problemOf(control, own ?? (invalid && invalid.test(control.value) ? invalid.message : null), own !== null)
  }, [control, invalid])
  const latest = useRef(check)

  // After every render the custom validity is current: a photo or the other amounts change values without any event. A message that was
  // shown goes once the value is put right that way too: the field is told as the native control would, by a finished edit (`change`).
  useLayoutEffect(() => {
    latest.current = check
    if (check() === null && shown !== null) control?.dispatchEvent(new Event('change'))
  })

  // A failed submit (invalid) or a finished edit (change) shows the messages, typing (input) hides them until the next one.
  useEffect(() => {
    if (!control) return
    const onInvalid = (event: Event) => {
      event.preventDefault() // no browser bubble
      setShown(latest.current())
    }
    const onChange = () => setShown(latest.current())
    const onInput = () => setShown(null)
    control.addEventListener('invalid', onInvalid)
    control.addEventListener('change', onChange)
    control.addEventListener('input', onInput)
    return () => {
      control.removeEventListener('invalid', onInvalid)
      control.removeEventListener('change', onChange)
      control.removeEventListener('input', onInput)
    }
  }, [control])

  // The form brings the checks up to date before validating and focuses the first bad field.
  useEffect(() => {
    if (!form || !control) return
    return form.register(control, {
      refresh: () => void latest.current(),
      focus: () => (compositeControl.current ? compositeControl.current.focus() : control.focus()),
    })
  }, [form, control])

  const register = useCallback<FieldMessages['register']>((messageId, slot) => {
    const entry = { id: messageId, slot, seq: seq.current++ }
    setMessages((all) => [...all, entry])
    return () => setMessages((all) => all.filter((m) => m.id !== messageId))
  }, [])
  const composite = useCallback<FieldControl['composite']>((own) => {
    compositeControl.current = own
    setComposite(true)
    return () => {
      compositeControl.current = null
      setComposite(false)
    }
  }, [])

  const error =
    shown === null ? null
    : shown === 'valueMissing' ? (required ? t('forms.required', { field: label }) : null)
    : shown === 'typeMismatch' ? (typeMismatch ?? null)
    : shown.custom
  const describedBy = messages.length > 0 ? [...messages].sort((a, b) => a.slot - b.slot || a.seq - b.seq).map((m) => m.id).join(' ') : undefined

  const value = useMemo(
    () => ({ id, name, labelId, describedBy, invalid: error !== null, required, bindControl: setControl, composite, register }),
    [id, name, labelId, describedBy, error, required, composite, register],
  )

  return (
    <FieldContext.Provider value={value}>
      <FormControl fullWidth required={required} error={error !== null} sx={{ gap: 0.5 }}>
        {/* required={false}: no asterisk (a missing value is said in words), so the label's text is just the label. */}
        <FormLabel id={labelId} htmlFor={isComposite ? undefined : id} required={false}>
          {label}
        </FormLabel>
        {children}
        {hint && <FieldMessage slot="hint">{hint}</FieldMessage>}
        {extra}
        {error !== null && (
          <FieldMessage slot="error" className="tk-appear">
            {error}
          </FieldMessage>
        )}
      </FormControl>
    </FieldContext.Provider>
  )
}

/**
 * A message of a Field (its hint, a note such as what a photo showed, its error), linked to the control as part of its description.
 * Outside a Field it is just small text.
 */
export function FieldMessage({ slot = 'note', children, ...rest }: { slot?: keyof typeof SLOTS } & Omit<FormHelperTextProps, 'id' | 'error'>) {
  const field = useContext(FieldContext)
  const id = useId()
  const register = field?.register
  useLayoutEffect(() => register?.(id, SLOTS[slot]), [register, id, slot])
  return (
    <FormHelperText id={id} error={slot === 'error'} {...rest}>
      {children}
    </FormHelperText>
  )
}
