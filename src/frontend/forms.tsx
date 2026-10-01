import { Label } from 'radix-ui'
import { useState, type FormEvent, type ReactNode } from 'react'

/** A tiny controlled form: field values by name, async submit, shared busy/error handling. */
export function Form({
  fields,
  submitLabel,
  onSubmit,
  children,
}: {
  fields: { name: string; label: string; type?: string; autoComplete?: string }[]
  submitLabel: string
  onSubmit: (values: Record<string, string>) => Promise<unknown>
  children?: ReactNode
}) {
  const [values, setValues] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(undefined)
    try {
      await onSubmit(values)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit}>
      {fields.map((f) => (
        <p key={f.name}>
          <Label.Root htmlFor={`field-${f.name}`}>{f.label}</Label.Root>{' '}
            <input
              id={`field-${f.name}`}
              name={f.name}
              type={f.type ?? 'text'}
              autoComplete={f.autoComplete}
              value={values[f.name] ?? ''}
              onChange={(e) => setValues({ ...values, [f.name]: e.target.value })}
            />
        </p>
      ))}
      {error && <p role="alert">{error}</p>}
      <button type="submit" disabled={busy}>
        {submitLabel}
      </button>
      {children}
    </form>
  )
}
