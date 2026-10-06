import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import type { ParseKeys } from 'i18next'
import { useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '../messages.tsx'
import { Field } from './Field.tsx'
import { FieldInput } from './FieldInput.tsx'
import { Form } from './Form.tsx'

export interface FieldDef {
  name: string
  /** Translation key of the label, e.g. "fields.email". */
  label: ParseKeys
  type?: 'text' | 'email' | 'password'
  autoComplete?: string
  /** Defaults to true: an empty field blocks the form. */
  required?: boolean
  /** Initial value, e.g. when editing. */
  defaultValue?: string
}

/** A form of text fields (sign-in, passwords, a user's name and e-mail) with the shared busy and error handling of the async submit. */
export function FieldForm({
  fields,
  submitLabel,
  onSubmit,
  children,
}: {
  fields: FieldDef[]
  submitLabel: ParseKeys
  onSubmit: (values: Record<string, string>) => Promise<unknown>
  /** Next to the submit button, e.g. Cancel. */
  children?: ReactNode
}) {
  const { t } = useTranslation()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>()

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const values = Object.fromEntries(new FormData(event.currentTarget)) as Record<string, string>
    setBusy(true)
    setError(undefined)
    try {
      await onSubmit(values)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Form onSubmit={submit}>
      <Stack sx={{ gap: 1.5 }}>
        {fields.map((f) => (
          <Field key={f.name} name={f.name} label={t(f.label)} required={f.required ?? true} typeMismatch={f.type === 'email' ? t('forms.emailInvalid') : undefined}>
            <FieldInput type={f.type ?? 'text'} autoComplete={f.autoComplete} defaultValue={f.defaultValue} />
          </Field>
        ))}
        {error !== undefined && <ErrorMessage error={error} />}
        <Stack direction="row" sx={{ gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
          <Button type="submit" disabled={busy}>
            {t(submitLabel)}
          </Button>
          {children}
        </Stack>
      </Stack>
    </Form>
  )
}
