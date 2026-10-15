import Box from '@mui/material/Box'
import InputAdornment from '@mui/material/InputAdornment'
import OutlinedInput from '@mui/material/OutlinedInput'
import { Eye, EyeOff } from 'lucide-react'
import { useCallback, useEffect, useRef, useState, type FocusEvent, type KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { IconAction } from '../components/IconAction.tsx'
import { FieldMessage } from './Field.tsx'
import { useFieldControl } from './fieldContext.ts'
import type { FieldInputProps } from './FieldInput.tsx'

/** How long a shown password stays shown without a keystroke. */
export const REVEAL_IDLE_MS = 30_000

/**
 * The control of a Field for a password, a passphrase or another secret: a real `<input type="password">` (its `autoComplete` as given,
 * so password managers and the browser behave as they do natively; paste allowed; no spellcheck or autocapitalize), with a Show button of
 * its own (`aria-pressed`, `aria-controls`). What it shows is never shown at first, and hides again by itself: after `REVEAL_IDLE_MS` without
 * a keystroke, when the focus leaves the field and its button, when its form is submitted (also a submit the form's checks stop), and when
 * it unmounts; the choice is never kept. While Caps Lock is on in it, the field's description says so (a polite status, in words).
 */
export function PasswordInput({
  showLabel,
  slotProps,
  sx,
  ...rest
}: Omit<FieldInputProps, 'type' | 'endAdornment' | 'uppercase' | 'inputMode'> & {
  /** The Show button's label (it says what it shows); the password's by default. */
  showLabel?: string
}) {
  const { t } = useTranslation()
  const field = useFieldControl()
  const [shown, setShown] = useState(false)
  const [capsLock, setCapsLock] = useState(false)
  const input = useRef<HTMLInputElement | null>(null)
  const idle = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)

  const hide = useCallback(() => {
    clearTimeout(idle.current)
    setShown(false)
  }, [])
  const keepShown = useCallback(() => {
    clearTimeout(idle.current)
    idle.current = setTimeout(hide, REVEAL_IDLE_MS)
  }, [hide])

  // The form's submit hides it, whether the form's checks let it through or not (the event fires either way).
  useEffect(() => {
    const form = input.current?.form
    if (!form) return
    form.addEventListener('submit', hide)
    return () => form.removeEventListener('submit', hide)
  }, [hide])
  useEffect(() => () => clearTimeout(idle.current), [])

  const bind = useCallback(
    (element: HTMLInputElement | null) => {
      input.current = element
      field.bindControl(element)
    },
    [field],
  )

  const toggle = () => {
    if (shown) hide()
    else {
      setShown(true)
      keepShown()
    }
  }
  const keys = (event: KeyboardEvent) => {
    setCapsLock(event.getModifierState('CapsLock'))
    if (shown) keepShown()
  }
  // Leaving the field and its button hides it; a move between the two does not.
  const root = useRef<HTMLDivElement | null>(null)
  const blur = (event: FocusEvent) => {
    if (root.current?.contains(event.relatedTarget as Node | null)) return
    hide()
    setCapsLock(false)
  }

  return (
    <>
      <OutlinedInput
        fullWidth
        {...rest}
        id={field.id}
        name={field.name}
        type={shown ? 'text' : 'password'}
        ref={root}
        inputRef={bind}
        aria-describedby={field.describedBy}
        onBlur={blur}
        onKeyDown={keys}
        onKeyUp={keys}
        sx={[{ '& input::-ms-reveal, & input::-ms-clear': { display: 'none' } }, ...(Array.isArray(sx) ? sx : [sx])]}
        slotProps={{
          ...slotProps,
          input: { spellCheck: false, autoCapitalize: 'off', autoCorrect: 'off', ...slotProps?.input },
        }}
        endAdornment={
          <InputAdornment position="end">
            <IconAction
              label={showLabel ?? t('forms.password.show')}
              variant="ghost"
              aria-pressed={shown}
              aria-controls={field.id}
              disabled={rest.disabled}
              type="button"
              // The focus stays in the input: a tap on Show is no blur (Safari never focuses a button on a click anyway).
              onMouseDown={(event) => event.preventDefault()}
              onClick={toggle}
              onBlur={blur}
            >
              {shown ? <EyeOff size={18} aria-hidden /> : <Eye size={18} aria-hidden />}
            </IconAction>
          </InputAdornment>
        }
      />
      {/* Present from the start, so a screen reader announces the note when it appears. */}
      <Box role="status">
        {capsLock && <FieldMessage>{t('forms.password.capsLock')}</FieldMessage>}
      </Box>
    </>
  )
}
