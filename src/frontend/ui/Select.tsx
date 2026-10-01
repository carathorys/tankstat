import { Select as RadixSelect } from 'radix-ui'

export interface Option {
  value: string
  label: string
}

export function Select({
  value,
  onValueChange,
  options,
  label,
  placeholder = 'Choose…',
}: {
  value: string
  onValueChange: (value: string) => void
  options: Option[]
  /** Accessible name of the control. */
  label: string
  placeholder?: string
}) {
  return (
    <RadixSelect.Root value={value || undefined} onValueChange={onValueChange}>
      <RadixSelect.Trigger className="select-trigger" aria-label={label}>
        <RadixSelect.Value placeholder={placeholder} />
        <RadixSelect.Icon aria-hidden> ▾</RadixSelect.Icon>
      </RadixSelect.Trigger>
      <RadixSelect.Portal>
        <RadixSelect.Content className="select-content" position="popper" sideOffset={4}>
          <RadixSelect.Viewport>
            {options.map((o) => (
              <RadixSelect.Item key={o.value} value={o.value} className="select-item">
                <RadixSelect.ItemText>{o.label}</RadixSelect.ItemText>
              </RadixSelect.Item>
            ))}
          </RadixSelect.Viewport>
        </RadixSelect.Content>
      </RadixSelect.Portal>
    </RadixSelect.Root>
  )
}
