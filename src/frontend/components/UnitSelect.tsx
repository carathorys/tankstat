import { Select, Text } from '@radix-ui/themes'
import { useId } from 'react'

/** A labelled Select (the label is linked to the trigger), used for the unit choices. */
export function LabeledSelect<T extends string>({
  label,
  value,
  options,
  onChange,
  disabled,
  placeholder,
}: {
  label: string
  value: T
  options: { value: T; label: string }[]
  onChange: (value: T) => void
  disabled?: boolean
  placeholder?: string
}) {
  const id = useId()
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-1)' }}>
      <Text as="label" size="2" weight="bold" htmlFor={id}>
        {label}
      </Text>
      <Select.Root value={value || undefined} onValueChange={(v) => onChange(v as T)} disabled={disabled}>
        <Select.Trigger id={id} placeholder={placeholder} />
        <Select.Content>
          {options.map((o) => (
            <Select.Item key={o.value} value={o.value}>
              {o.label}
            </Select.Item>
          ))}
        </Select.Content>
      </Select.Root>
    </div>
  )
}
