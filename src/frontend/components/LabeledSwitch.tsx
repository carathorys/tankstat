import { Flex, Switch, Text } from '@radix-ui/themes'
import { useId } from 'react'

/** A Switch with its label and an optional hint next to it, both linked to the switch (the hint as its description). */
export function LabeledSwitch({
  label,
  hint,
  checked,
  onChange,
}: {
  label: string
  hint?: string
  checked: boolean
  onChange: (checked: boolean) => void
}) {
  const id = useId()
  return (
    <Flex align="center" gap="3">
      <Switch id={id} checked={checked} onCheckedChange={onChange} size="3" aria-describedby={hint ? `${id}-hint` : undefined} />
      <Flex direction="column">
        <Text as="label" size="2" weight="bold" htmlFor={id}>
          {label}
        </Text>
        {hint && (
          <Text id={`${id}-hint`} size="1" color="gray">
            {hint}
          </Text>
        )}
      </Flex>
    </Flex>
  )
}
