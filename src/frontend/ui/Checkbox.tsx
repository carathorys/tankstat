import { Checkbox as RadixCheckbox } from 'radix-ui'

export function Checkbox({
  checked,
  onCheckedChange,
  label,
}: {
  checked: boolean
  onCheckedChange: (checked: boolean) => void
  /** Accessible name; the box itself has no visible text. */
  label: string
}) {
  return (
    <RadixCheckbox.Root
      className="checkbox"
      aria-label={label}
      checked={checked}
      onCheckedChange={(c) => onCheckedChange(c === true)}
    >
      <RadixCheckbox.Indicator>✓</RadixCheckbox.Indicator>
    </RadixCheckbox.Root>
  )
}
