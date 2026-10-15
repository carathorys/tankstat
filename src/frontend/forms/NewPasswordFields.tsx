import { useQuery } from '@apollo/client/react'
import LinearProgress from '@mui/material/LinearProgress'
import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { PasswordRequirementsDocument } from '../gql/generated.ts'
import { Field, FieldMessage } from './Field.tsx'
import { PasswordInput } from './PasswordInput.tsx'
import { passwordStrength, type Score } from './passwordStrength.ts'

/** The floor of the password minimum: what a new password needs while the server's requirements are not known (a failed request). */
export const PASSWORD_FLOOR = 10

const LEVELS = ['veryWeak', 'weak', 'fair', 'good', 'strong'] as const
const BAR_COLOR: Record<Score, 'error' | 'warning' | 'success'> = { 0: 'error', 1: 'error', 2: 'warning', 3: 'success', 4: 'success' }

/**
 * A new password (or passphrase) and its repetition, each a PasswordInput with its own Show button (`autocomplete="new-password"` on
 * both). The new one says the minimum the administrators set before anything is typed (its hint, from the public `passwordRequirements`)
 * and checks it while typing, and once something is typed how strong it is (`passwordStrength`, advisory: it never stops the form), in
 * words with a bar beside them, both in its description. The repetition is `<name>Repeat`, and a different one is the field's own check
 * (`errors.password.mismatch`). Either check stops the form at the field, says why and sends nothing; the server judges both again.
 */
export function NewPasswordFields({
  name,
  label,
  repeatLabel,
  showLabel,
  required = true,
  personal = [],
  minLength,
  hint,
  tooShort,
}: {
  name: string
  label: string
  repeatLabel: string
  /** The Show buttons' label (the password's by default). */
  showLabel?: string
  required?: boolean
  /** The person's own address and name, which a strong password leaves out (none where the form does not know whose it is). */
  personal?: readonly (string | null | undefined)[]
  /** Another minimum than the sign-in's (a backup passphrase's): then the password requirements are not asked. */
  minLength?: number
  /** Another hint than "At least {{min}} characters." (it should say the minimum too). */
  hint?: string
  /** Another message for one under the minimum than the password's (a passphrase's). */
  tooShort?: string
}) {
  const { t } = useTranslation()
  const { data } = useQuery(PasswordRequirementsDocument, { fetchPolicy: 'cache-and-network', skip: minLength !== undefined })
  const min = minLength ?? data?.passwordRequirements.minLength ?? PASSWORD_FLOOR
  // A password's ceiling too (a passphrase has none): a longer one is said at the field, as a shorter one is, before anything is sent.
  const max = minLength === undefined ? data?.passwordRequirements.maxLength : undefined
  // What the first field holds now: the form brings every field's checks up to date before it validates, so the repetition reads it then.
  const first = useRef('')
  const [typed, setTyped] = useState('')
  return (
    <>
      <Field
        name={name}
        label={label}
        required={required}
        hint={hint ?? t('forms.password.requirements', { min })}
        invalid={{
          message: max !== undefined && typed.length > max ? t('errors.password.tooLong', { max }) : (tooShort ?? t('errors.password.tooShort', { min })),
          test: (value) => value.length > 0 && (value.length < min || (max !== undefined && value.length > max)),
        }}
        extra={typed && <StrengthMeter password={typed} personal={personal} min={min} />}
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

/** How strong the typed password is: the words (and what to avoid) are the description; the bar only shows the same. */
function StrengthMeter({ password, personal, min }: { password: string; personal: readonly (string | null | undefined)[]; min: number }) {
  const { t } = useTranslation()
  const { score, reasons } = passwordStrength(password, personal, min)
  return (
    <FieldMessage component="div">
      <LinearProgress
        aria-hidden
        variant="determinate"
        value={(score + 1) * 20}
        color={BAR_COLOR[score]}
        sx={{ height: 4, borderRadius: 2, my: 0.5, maxWidth: 240 }}
      />
      {[t('forms.password.strength.label', { level: t(`forms.password.strength.levels.${LEVELS[score]}`) }), ...reasons.slice(0, 2).map((r) => t(`forms.password.strength.reasons.${r}`))].join(' ')}
    </FieldMessage>
  )
}
