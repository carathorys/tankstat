import CssBaseline from '@mui/material/CssBaseline'
import { ThemeProvider } from '@mui/material/styles'
import type { ReactNode } from 'react'
import { COLOR_MODE_KEY } from './colorMode.ts'
import { createTankstatTheme } from './theme.ts'

const theme = createTankstatTheme()
const instantTheme = createTankstatTheme({ instant: true })

/**
 * The MUI theme around the app (main.tsx) and every rendered test (renderWithApollo, with `instant`): dark unless the user chose
 * otherwise, the choice kept in the browser under COLOR_MODE_KEY.
 */
export function ThemeRoot({ children, instant = false }: { children: ReactNode; instant?: boolean }) {
  return (
    <ThemeProvider theme={instant ? instantTheme : theme} defaultMode="dark" modeStorageKey={COLOR_MODE_KEY} noSsr disableTransitionOnChange>
      <CssBaseline enableColorScheme />
      {children}
    </ThemeProvider>
  )
}
