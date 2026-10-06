import type { ColorSystemOptions, SimplePaletteColorOptions } from '@mui/material/styles'
import { HUES, SAND, SAND_A, step, type HueSteps, type Scheme } from './scales.ts'

/** A status colour from its Radix steps: solid 9 (10 on hover), soft a3/a4 surfaces, readable step-11 text. */
const tone = (h: HueSteps, scheme: Scheme, contrastText: string): SimplePaletteColorOptions => ({
  main: h[9],
  dark: h[10],
  light: scheme === 'dark' ? h[11] : h[8], // MUI's "light" is the lighter shade in either scheme
  contrastText,
  soft: h.a3,
  softHover: h.a4,
  softText: h[11],
})

/** The palette of one scheme: today's dim dark (sand on indigo), and a dim light one built from the same scales. */
export function colorScheme(scheme: Scheme): ColorSystemOptions {
  const gray = SAND[scheme]
  const grayA = SAND_A[scheme]
  const ink = step(gray, 12)
  const neutral: SimplePaletteColorOptions = {
    main: ink, // gray buttons are high-contrast, as they were (highContrast)
    dark: step(gray, 11),
    light: step(gray, 9),
    contrastText: step(gray, 1),
    soft: step(grayA, 3),
    softHover: step(grayA, 4),
    softText: ink,
  }
  return {
    palette: {
      primary: tone(HUES.indigo[scheme], scheme, '#ffffff'),
      secondary: neutral,
      neutral,
      error: tone(HUES.red[scheme], scheme, '#ffffff'),
      warning: tone(HUES.amber[scheme], scheme, '#21201c'), // amber is light enough for dark text in both schemes
      success: tone(HUES.green[scheme], scheme, '#ffffff'),
      info: tone(HUES.blue[scheme], scheme, '#ffffff'),
      background: scheme === 'dark' ? { default: step(gray, 1), paper: step(gray, 2) } : { default: step(gray, 3), paper: step(gray, 1) },
      text: { primary: ink, secondary: step(gray, 11), disabled: step(grayA, 8) },
      divider: step(grayA, 6),
      action: {
        active: step(gray, 11),
        hover: step(grayA, 3),
        selected: step(grayA, 4),
        focus: step(grayA, 4),
        disabled: step(grayA, 8),
        disabledBackground: step(grayA, 3),
      },
    },
  }
}

/**
 * Radix's soft shadows (1 px ring plus a soft drop), as CSS variables per scheme: MUI's shadow list points at them, so one list
 * serves both schemes.
 */
export function shadowTokens(scheme: Scheme): Record<string, string> {
  const ring = step(SAND_A[scheme], scheme === 'dark' ? 6 : 3)
  const a = (n: number) => step(SAND_A[scheme], n)
  const black = (opacity: number) => `rgba(0, 0, 0, ${opacity})`
  return scheme === 'dark'
    ? {
        '--tk-shadow-2': `0 0 0 1px ${ring}, 0 0 0 0.5px ${black(0.15)}, 0 1px 1px 0 ${black(0.4)}, 0 2px 1px -1px ${black(0.4)}, 0 1px 3px 0 ${black(0.3)}`,
        '--tk-shadow-3': `0 0 0 1px ${ring}, 0 2px 3px -2px ${black(0.15)}, 0 3px 8px -2px ${black(0.4)}, 0 4px 12px -4px ${black(0.5)}`,
        '--tk-shadow-4': `0 0 0 1px ${ring}, 0 8px 40px ${black(0.15)}, 0 12px 32px -16px ${black(0.3)}`,
        '--tk-shadow-5': `0 0 0 1px ${ring}, 0 12px 60px ${black(0.3)}, 0 12px 32px -16px ${black(0.5)}`,
      }
    : {
        '--tk-shadow-2': `0 0 0 1px ${ring}, 0 0 0 0.5px ${black(0.05)}, 0 1px 1px 0 ${a(2)}, 0 2px 1px -1px ${black(0.05)}, 0 1px 3px 0 ${black(0.05)}`,
        '--tk-shadow-3': `0 0 0 1px ${ring}, 0 2px 3px -2px ${ring}, 0 3px 12px -4px ${black(0.1)}, 0 4px 16px -8px ${black(0.1)}`,
        '--tk-shadow-4': `0 0 0 1px ${ring}, 0 8px 40px ${black(0.05)}, 0 12px 32px -16px ${ring}`,
        '--tk-shadow-5': `0 0 0 1px ${ring}, 0 12px 60px ${black(0.15)}, 0 12px 32px -16px ${a(5)}`,
      }
}
