import * as RadixForm from '@radix-ui/react-form'
import { Button, Flex, Text, TextField } from '@radix-ui/themes'
import type { ParseKeys } from 'i18next'
import { useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from './messages.tsx'

export interface FieldDef {
  name: string
  /** Translation key of the label, e.g. "fields.email". */
  label: ParseKeys
  type?: 'text' | 'email' | 'password'
  autoComplete?: string
  /** Defaults to true: the browser-side check blocks submitting an empty field. */
  required?: boolean
}

/** Text fields on Radix Form (built-in validation messages) with shared busy/error handling for the async submit. */
export function FieldForm({
  fields,
  submitLabel,
  onSubmit,
  children,
}: {
  fields: FieldDef[]
  submitLabel: ParseKeys
  onSubmit: (values: Record<string, string>) => Promise<unknown>
  children?: ReactNode
}) {
  const { t } = useTranslation()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>()

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const values = Object.fromEntries(new FormData(e.currentTarget)) as Record<string, string>
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
    <RadixForm.Root onSubmit={submit}>
      <Flex direction="column" gap="3">
        {fields.map((f) => (
          <RadixForm.Field key={f.name} name={f.name} asChild>
            <Flex direction="column" gap="1">
              <RadixForm.Label asChild>
                <Text as="label" size="2" weight="bold">
                  {t(f.label)}
                </Text>
              </RadixForm.Label>
              <RadixForm.Control asChild>
                <TextField.Root type={f.type ?? 'text'} autoComplete={f.autoComplete} required={f.required ?? true} />
              </RadixForm.Control>
              <RadixForm.Message match="valueMissing" asChild>
                <Text size="1" color="red">
                  {t('forms.required', { field: t(f.label) })}
                </Text>
              </RadixForm.Message>
              {f.type === 'email' && (
                <RadixForm.Message match="typeMismatch" asChild>
                  <Text size="1" color="red">
                    {t('forms.emailInvalid')}
                  </Text>
                </RadixForm.Message>
              )}
            </Flex>
          </RadixForm.Field>
        ))}
        {error !== undefined && <ErrorMessage error={error} />}
        <Flex gap="3" align="center" wrap="wrap">
          <RadixForm.Submit asChild>
            <Button disabled={busy}>{t(submitLabel)}</Button>
          </RadixForm.Submit>
          {children}
        </Flex>
      </Flex>
    </RadixForm.Root>
  )
}
