import { Tabs as RadixTabs } from 'radix-ui'
import type { ReactNode } from 'react'

export function Tabs({
  defaultValue,
  tabs,
}: {
  defaultValue: string
  tabs: { value: string; label: string; content: ReactNode }[]
}) {
  return (
    <RadixTabs.Root defaultValue={defaultValue} className="tabs">
      <RadixTabs.List className="tabs-list">
        {tabs.map((t) => (
          <RadixTabs.Trigger key={t.value} value={t.value} className="tabs-trigger">
            {t.label}
          </RadixTabs.Trigger>
        ))}
      </RadixTabs.List>
      {tabs.map((t) => (
        <RadixTabs.Content key={t.value} value={t.value}>
          {t.content}
        </RadixTabs.Content>
      ))}
    </RadixTabs.Root>
  )
}
