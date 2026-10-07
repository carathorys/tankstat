import type { TypographyVariantsOptions } from '@mui/material/styles'

/** The system font stack the app has always used (Radix Themes' default). */
export const FONT_FAMILY =
  "-apple-system, BlinkMacSystemFont, 'Segoe UI (Custom)', Roboto, 'Helvetica Neue', 'Open Sans (Custom)', system-ui, sans-serif, 'Apple Color Emoji', 'Segoe UI Emoji'"

const rem = (px: number) => `${px / 16}rem`
const size = (px: number, lineHeight: number, letterSpacing = '0em') => ({ fontSize: rem(px), lineHeight: rem(lineHeight), letterSpacing })

/**
 * The type scale of the old Radix sizes: text 1/2/3/4 are caption/body2/body1/subtitle1 (12, 14, 16, 18 px), heading size n is h(9 - n)
 * (h6 16 px ... h1 35 px), always bold. The DOM level of a heading is its `component`, independent of the look.
 */
export const typography: TypographyVariantsOptions = {
  fontFamily: FONT_FAMILY,
  fontWeightRegular: 400,
  fontWeightMedium: 500,
  fontWeightBold: 700,
  h1: { ...size(35, 40, '-0.01em'), fontWeight: 700 },
  h2: { ...size(28, 36, '-0.0075em'), fontWeight: 700 },
  h3: { ...size(24, 30, '-0.00625em'), fontWeight: 700 },
  h4: { ...size(20, 26, '-0.005em'), fontWeight: 700 },
  h5: { ...size(18, 24, '-0.0025em'), fontWeight: 700 },
  h6: { ...size(16, 22), fontWeight: 700 },
  subtitle1: { ...size(18, 26, '-0.0025em'), fontWeight: 400 },
  subtitle2: { ...size(14, 20), fontWeight: 500 },
  body1: size(16, 24),
  body2: size(14, 20),
  caption: size(12, 16, '0.0025em'),
  button: { ...size(14, 20), fontWeight: 500, textTransform: 'none' },
  overline: { ...size(12, 16, '0.04em'), fontWeight: 500 },
}
