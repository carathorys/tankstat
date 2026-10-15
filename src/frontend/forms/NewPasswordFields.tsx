import { useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { Field } from './Field.tsx'
import { PasswordInput } from './PasswordInput.tsx'

/**
 * A new password (or passphrase) and its repetition, each a PasswordInput with its own Show button (`autocomplete="new-password"` on
 * both). The repetition is `<name>Repeat`; a different one is the field's own check (`errors.password.mismatch`), so the form stops the
 * submit, says it there and moves the focus to it, and nothing is sent. The server checks the two again.
 */
export function NewPasswordFields({
  name,
  label,
  repeatLabel,
  showLabel,
  required = true,
}: {
  name: string
  label: string
  repeatLabel: string
  /** The Show buttons' label (the password's by default). */
  showLabel?: string
  required?: boolean
}) {
  const { t } = useTranslation()
  // What the first field holds now: the form brings every field's checks up to date before it validates, so the repetition reads it then.
  const first = useRef('')
  return (
    <>
      <Field name={name} label={label} required={required}>
        <PasswordInput autoComplete="new-password" showLabel={showLabel} onChange={(e) => (first.current = e.target.value)} />
      </Field>
      <Field
        name={`${name}Repeat`}
        label={repeatLabel}
        required={required}
        invalid={{ message: t('errors.password.mismatch'), test: (value) => value !== first.current }}
      >
        <PasswordInput autoComplete="new-password" showLabel={showLabel} />
      </Field>
    </>
  )
}
