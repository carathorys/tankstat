import Box from '@mui/material/Box'
import Tab from '@mui/material/Tab'
import Tabs from '@mui/material/Tabs'
import { useId, type ReactNode } from 'react'

export interface TabbedPanel<V extends string> {
  value: V
  label: string
  /** What the panel shows; rendered only while its tab is chosen, so an unchosen tab loads nothing. */
  content: () => ReactNode
}

/**
 * Tabs with their panels. Every tab has a panel of its own (a tabpanel named by its tab, which controls it), but only the chosen one is
 * filled; the others are empty and hidden. The arrow keys move the choice, as the old tabs did; a long row scrolls sideways on a phone.
 */
export function TabbedPanels<V extends string>({
  label,
  value,
  onChange,
  tabs,
}: {
  /** Names the row of tabs. */
  label: string
  value: V
  onChange: (value: V) => void
  tabs: readonly TabbedPanel<V>[]
}) {
  const base = useId()
  const tabId = (v: V) => `${base}-tab-${v}`
  const panelId = (v: V) => `${base}-panel-${v}`
  return (
    <>
      <Tabs value={value} onChange={(_, next: V) => onChange(next)} aria-label={label} variant="scrollable" scrollButtons={false} selectionFollowsFocus>
        {tabs.map((tab) => (
          <Tab key={tab.value} value={tab.value} label={tab.label} id={tabId(tab.value)} aria-controls={panelId(tab.value)} />
        ))}
      </Tabs>
      {tabs.map((tab) => {
        const chosen = tab.value === value
        return (
          <Box key={tab.value} role="tabpanel" id={panelId(tab.value)} aria-labelledby={tabId(tab.value)} hidden={!chosen} className={chosen ? 'tk-fade' : undefined} sx={{ pt: 2 }}>
            {chosen && tab.content()}
          </Box>
        )
      })}
    </>
  )
}
