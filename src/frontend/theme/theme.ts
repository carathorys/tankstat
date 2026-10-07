import { createTheme, type Theme } from '@mui/material/styles'
import { components, SHADOWS } from './components.ts'
import { colorScheme } from './palette.ts'
import { typography } from './typography.ts'

const NO_TIME = { shortest: 0, shorter: 0, short: 0, standard: 0, complex: 0, enteringScreen: 0, leavingScreen: 0 }

/**
 * The app's theme: both colour schemes as CSS variables (dark by default, the light one under the `light` class on <html>), the old
 * Radix breakpoints under MUI's names plus xxl (Radix initial/xs/sm/md/lg/xl = xs/sm/md/lg/xl/xxl here), MUI's 8 px spacing unit
 * (Radix space 1..9 = 0.5, 1, 1.5, 2, 3, 4, 5, 6, 8). `instant` (tests): no transitions, so a closed dialog is gone at once.
 */
export function createTankstatTheme({ instant = false }: { instant?: boolean } = {}): Theme {
  return createTheme({
    cssVariables: { colorSchemeSelector: 'class' },
    defaultColorScheme: 'dark',
    colorSchemes: { dark: colorScheme('dark'), light: colorScheme('light') },
    breakpoints: { values: { xs: 0, sm: 520, md: 768, lg: 1024, xl: 1280, xxl: 1640 } },
    shape: { borderRadius: 12 },
    shadows: SHADOWS,
    typography,
    components,
    focusVisible: true,
    motion: { reducedMotion: instant ? 'always' : 'system' },
    ...(instant ? { transitions: { create: () => 'none', duration: NO_TIME } } : {}),
  })
}
