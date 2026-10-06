import { useColorScheme } from '@mui/material/styles'
import { Theme } from '@radix-ui/themes'
import type { ReactNode } from 'react'

/**
 * The screens that are still Radix follow MUI's colour scheme. Both read the light/dark class of their root, so it must be the same: a
 * Radix root in another scheme would hand MUI's components inside it that scheme's variables.
 */
export function RadixTheme({ children }: { children: ReactNode }) {
  const { colorScheme } = useColorScheme()
  return (
    <Theme accentColor="indigo" grayColor="sand" appearance={colorScheme ?? 'dark'} radius="full" panelBackground="translucent">
      {children}
    </Theme>
  )
}
