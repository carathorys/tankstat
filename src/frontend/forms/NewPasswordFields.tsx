import { useQuery } from '@apollo/client/react'
import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { PasswordRequirementsDocument } from '../gql/generated.ts'
import { Field } from './Field.tsx'
import { PasswordInput } from './PasswordInput.tsx'

/** The floor of the password minimum: what a new password needs while the server's requirements are not known (a failed request). */
export const PASSWORD_FLOOR = 10

/**
 * A new password (or passphrase) and its repetition, each a PasswordInput with its own Show button (`autocomplete="new-password"` on
 * both). The new one says the minimum the administrators set before anything is typed (its hint, from the public `passwordRequirements`)
 * and checks it while typing; the repetition is `<name>Repeat`, and a different one is the field's own check (`errors.password.mismatch`).
 * Either stops the form at the field, says why and sends nothing; the server judges both again.
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
  const { data } = useQuery(PasswordRequirementsDocument, { fetchPolicy: 'cache-and-network' })
  const min = data?.passwordRequirements.minLength ?? PASSWORD_FLOOR
  // The ceiling too: a longer one is said at the field, as a shorter one is, before anything is sent.
  const max = data?.passwordRequirements.maxLength
  // What the first field holds now: the form brings every field's checks up to date before it validates, so the repetition reads it then.
  const first = useRef('')
  const [typed, setTyped] = useState('')
  return (
    <>
      <Field
        name={name}
        label={label}
        required={required}
        hint={t('forms.password.requirements', { min })}
        invalid={{
          message: max !== undefined && typed.length > max ? t('errors.password.tooLong', { max }) : t('errors.password.tooShort', { min }),
          test: (value) => value.length > 0 && (value.length < min || (max !== undefined && value.length > max)),
        }}
      >
        <PasswordInput
          autoComplete="new-password"
          showLabel={showLabel}
          onChange={(e) => {
            first.current = e.target.value
            setTyped(e.target.value)
          }}
        />
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
