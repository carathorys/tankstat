import type { Components, CSSObject, Shadows, Theme } from '@mui/material/styles'
import { shadowTokens } from './palette.ts'

// How MUI's components look in Tankstat: the old Radix sizes (controls 24/32/40 px for small/medium/large, 44 px on a touch screen),
// pill-shaped controls, translucent blurred panels, soft tinted variants and Radix's soft shadows.

type T = Omit<Theme, 'components'>

/** A touch screen: every control is at least 44 px (WCAG target size). */
export const COARSE = '@media (pointer: coarse)'

/** The palette colours a control can take, each with soft tones (theme/augment.d.ts). */
export const TONES = ['primary', 'neutral', 'error', 'warning', 'success', 'info'] as const
export type Tone = (typeof TONES)[number]

/** MUI elevation n uses shadow n: 1-2 small, 3-4 panels, 5-8 menus and popovers, the rest dialogs (theme/palette.ts shadowTokens). */
export const SHADOWS = Array.from({ length: 25 }, (_, n) =>
  n === 0 ? 'none' : n <= 2 ? 'var(--tk-shadow-2)' : n <= 4 ? 'var(--tk-shadow-3)' : n <= 8 ? 'var(--tk-shadow-4)' : 'var(--tk-shadow-5)',
) as Shadows

/** The translucent, blurred panel of the app (the old `.glass`): the top bar, the sidebar, dialogs, menus, popovers, cards. */
export const glass = (theme: T): CSSObject => ({
  backgroundColor: `rgba(${theme.vars.palette.background.paperChannel} / 0.7)`,
  backgroundImage: 'none',
  backdropFilter: 'blur(14px) saturate(140%)',
  WebkitBackdropFilter: 'blur(14px) saturate(140%)',
})

/** A soft surface in a tone: tinted background, readable text, a little more tint under the pointer. */
export const softTone = (theme: T, tone: Tone): CSSObject => ({
  backgroundColor: theme.vars.palette[tone].soft,
  color: theme.vars.palette[tone].softText,
  '&:hover': { backgroundColor: theme.vars.palette[tone].softHover },
})

/** A ghost control: no background until the pointer is on it. */
export const ghostTone = (theme: T, tone: Tone): CSSObject => ({
  backgroundColor: 'transparent',
  color: tone === 'neutral' ? theme.vars.palette.text.primary : theme.vars.palette[tone].softText,
  '&:hover': { backgroundColor: theme.vars.palette[tone].soft },
})

const controlHeights = { small: 24, medium: 32, large: 40 } as const

export const components: Components<T> = {
  MuiCssBaseline: {
    styleOverrides: {
      // The shadows of the default (dark) scheme at the root, the light ones with the light class (theme.ts: colorSchemeSelector).
      ':root': shadowTokens('dark'),
      '.light': shadowTokens('light'),
      'html, body': { height: '100%' },
    },
  },
  MuiButtonBase: { defaultProps: { disableRipple: true } },

  MuiButton: {
    defaultProps: { variant: 'contained', disableElevation: true },
    styleOverrides: {
      root: { borderRadius: 9999, gap: 8, minWidth: 0, whiteSpace: 'nowrap', [COARSE]: { minHeight: 44 } },
      sizeSmall: { minHeight: controlHeights.small, padding: '0 8px', fontSize: '0.75rem', lineHeight: '1rem', gap: 4 },
      sizeMedium: { minHeight: controlHeights.medium, padding: '0 12px' },
      sizeLarge: { minHeight: controlHeights.large, padding: '0 16px', fontSize: '1rem', lineHeight: '1.5rem', gap: 12 },
    },
    variants: [
      ...TONES.map((tone) => ({ props: { variant: 'soft' as const, color: tone }, style: ({ theme }: { theme: T }) => softTone(theme, tone) })),
      ...TONES.map((tone) => ({ props: { variant: 'ghost' as const, color: tone }, style: ({ theme }: { theme: T }) => ghostTone(theme, tone) })),
      {
        props: { variant: 'soft' as const },
        style: ({ theme }: { theme: T }) => ({ '&.Mui-disabled': { backgroundColor: theme.vars.palette.action.disabledBackground } }),
      },
      { props: { variant: 'ghost' as const }, style: { '&.Mui-disabled': { backgroundColor: 'transparent' } } },
    ],
  },

  MuiIconButton: {
    styleOverrides: {
      root: { borderRadius: 9999, padding: 0, [COARSE]: { minWidth: 44, minHeight: 44 } },
      sizeSmall: { width: controlHeights.small, height: controlHeights.small },
      sizeMedium: { width: controlHeights.medium, height: controlHeights.medium },
      sizeLarge: { width: controlHeights.large, height: controlHeights.large },
    },
  },

  MuiChip: {
    defaultProps: { size: 'small', variant: 'soft' },
    styleOverrides: {
      root: { borderRadius: 9999, fontWeight: 500, maxWidth: '100%' },
      sizeSmall: { height: 20, fontSize: '0.75rem', lineHeight: '1rem', '& .MuiChip-label': { padding: '0 6px' } },
      sizeMedium: { height: 24, fontSize: '0.75rem', '& .MuiChip-label': { padding: '0 8px' } },
      icon: { color: 'inherit', marginLeft: 4, marginRight: -2 },
    },
    variants: [
      ...TONES.map((tone) => ({
        props: { variant: 'soft' as const, color: tone },
        style: ({ theme }: { theme: T }) => ({ backgroundColor: theme.vars.palette[tone].soft, color: theme.vars.palette[tone].softText }),
      })),
      {
        props: { variant: 'soft' as const, color: 'default' as const },
        style: ({ theme }: { theme: T }) => ({ backgroundColor: theme.vars.palette.neutral.soft, color: theme.vars.palette.text.primary }),
      },
      ...TONES.map((tone) => ({
        props: { variant: 'solid' as const, color: tone },
        style: ({ theme }: { theme: T }) => ({ backgroundColor: theme.vars.palette[tone].main, color: theme.vars.palette[tone].contrastText }),
      })),
    ],
  },

  MuiPaper: {
    styleOverrides: { root: { backgroundImage: 'none' } },
    variants: [{ props: { variant: 'glass' as const }, style: ({ theme }: { theme: T }) => ({ ...glass(theme), boxShadow: theme.shadows[3] }) }],
  },
  MuiCard: { styleOverrides: { root: ({ theme }) => ({ ...glass(theme), borderRadius: 12, boxShadow: theme.shadows[3] }) } },

  MuiDialog: {
    styleOverrides: {
      paper: ({ theme }) => ({ ...glass(theme), borderRadius: 16, margin: 16, width: 'calc(100% - 32px)', maxHeight: 'calc(100% - 32px)' }),
    },
  },
  MuiDialogTitle: { styleOverrides: { root: ({ theme }) => ({ ...theme.typography.h4, padding: '24px 24px 8px' }) } },
  MuiDialogContent: { styleOverrides: { root: { padding: '0 24px 24px' } } },
  MuiDialogContentText: { styleOverrides: { root: ({ theme }) => ({ ...theme.typography.body2, color: theme.vars.palette.text.primary, marginBottom: 16 }) } },
  MuiDialogActions: { styleOverrides: { root: { padding: '0 24px 24px', gap: 12, '& > :not(style) ~ :not(style)': { marginLeft: 0 } } } },
  MuiBackdrop: { styleOverrides: { root: { backgroundColor: 'rgba(0, 0, 0, 0.5)' }, invisible: { backgroundColor: 'transparent' } } },

  MuiPopover: { styleOverrides: { paper: ({ theme }) => ({ ...glass(theme), borderRadius: 12 }) } },
  MuiMenu: { styleOverrides: { list: { padding: 8 } } },
  MuiMenuItem: {
    styleOverrides: {
      root: ({ theme }) => ({
        ...theme.typography.body2,
        minHeight: controlHeights.medium,
        borderRadius: 6,
        padding: '0 12px',
        [theme.breakpoints.up('sm')]: { minHeight: controlHeights.medium }, // MUI makes it "auto" from sm up
        [COARSE]: { minHeight: 44 }, // after the line above: a touch screen wins at any width
      }),
    },
  },

  MuiTooltip: {
    defaultProps: { enterDelay: 400 },
    styleOverrides: {
      tooltip: ({ theme }) => ({
        ...theme.typography.caption,
        backgroundColor: theme.vars.palette.neutral.main,
        color: theme.vars.palette.neutral.contrastText,
        padding: '4px 8px',
        borderRadius: 6,
      }),
    },
  },

  MuiAlert: {
    defaultProps: { variant: 'standard' },
    styleOverrides: {
      root: ({ theme }) => ({ ...theme.typography.body2, borderRadius: 12, padding: '10px 12px', gap: 8, alignItems: 'flex-start' }),
      icon: { padding: '2px 0 0', marginRight: 0, opacity: 1, color: 'inherit' },
      message: { padding: 0 },
    },
    variants: [
      ...(['error', 'warning', 'success', 'info'] as const).map((severity) => ({
        props: { variant: 'standard' as const, severity },
        style: ({ theme }: { theme: T }) => ({ backgroundColor: theme.vars.palette[severity].soft, color: theme.vars.palette[severity].softText }),
      })),
    ],
  },

  MuiFormLabel: {
    styleOverrides: {
      root: ({ theme }) => ({ ...theme.typography.body2, fontWeight: 700, color: theme.vars.palette.text.primary, '&.Mui-focused, &.Mui-error': { color: theme.vars.palette.text.primary } }),
      asterisk: { display: 'none' }, // labels never had one; a missing value is said in words
    },
  },
  MuiFormHelperText: {
    styleOverrides: { root: ({ theme }) => ({ ...theme.typography.caption, margin: 0, color: theme.vars.palette.text.secondary, '&.Mui-error': { color: theme.vars.palette.error.softText } }) },
  },
  MuiOutlinedInput: {
    styleOverrides: {
      root: ({ theme }) => ({
        ...theme.typography.body2,
        borderRadius: 9999,
        backgroundColor: 'rgba(0, 0, 0, 0.25)',
        ...theme.applyStyles('light', { backgroundColor: 'rgba(255, 255, 255, 0.9)' }),
        '& .MuiOutlinedInput-notchedOutline': { borderColor: theme.vars.palette.divider },
        '&:hover .MuiOutlinedInput-notchedOutline': { borderColor: theme.vars.palette.text.disabled },
        '&.Mui-focused .MuiOutlinedInput-notchedOutline': { borderColor: theme.vars.palette.primary.main, borderWidth: 2 },
        '&.Mui-error .MuiOutlinedInput-notchedOutline': { borderColor: theme.vars.palette.error.main },
        [COARSE]: { minHeight: 44 },
      }),
      input: { padding: '6px 12px', height: '1.25rem', '&::placeholder': { opacity: 0.6 } },
      multiline: { borderRadius: 16, padding: '6px 12px', '& .MuiOutlinedInput-input': { padding: 0, height: 'auto' } },
    },
  },
  MuiSelect: { styleOverrides: { icon: ({ theme }) => ({ color: theme.vars.palette.text.secondary }) } },

  MuiTabs: {
    styleOverrides: {
      root: ({ theme }) => ({ minHeight: 40, boxShadow: `inset 0 -1px 0 0 ${theme.vars.palette.divider}` }),
      indicator: { height: 2 },
    },
  },
  MuiTab: {
    styleOverrides: {
      root: ({ theme }) => ({
        ...theme.typography.body2,
        minHeight: 40,
        minWidth: 0,
        padding: '0 12px',
        color: theme.vars.palette.text.secondary,
        '&.Mui-selected': { color: theme.vars.palette.text.primary, fontWeight: 500 },
        [COARSE]: { minHeight: 44 },
      }),
    },
  },

  MuiTableCell: {
    styleOverrides: {
      root: ({ theme }) => ({ ...theme.typography.body2, padding: 12, borderBottomColor: theme.vars.palette.divider }),
      head: { fontWeight: 700 },
    },
  },

  MuiLink: {
    defaultProps: { underline: 'hover' },
    styleOverrides: { root: ({ theme }) => ({ color: theme.vars.palette.primary.softText }) },
  },

  MuiSwitch: {
    styleOverrides: {
      root: { width: 44, height: 24, padding: 0, overflow: 'visible' },
      switchBase: {
        padding: 2,
        '&.Mui-checked': { transform: 'translateX(20px)', color: '#ffffff' },
        '&.Mui-checked + .MuiSwitch-track': { opacity: 1 },
      },
      thumb: { width: 20, height: 20, boxShadow: 'none', backgroundColor: '#ffffff' },
      track: ({ theme }) => ({ borderRadius: 12, opacity: 1, backgroundColor: theme.vars.palette.action.disabledBackground }),
    },
  },
  MuiCheckbox: { styleOverrides: { root: ({ theme }) => ({ color: theme.vars.palette.text.disabled }) } },
  MuiRadio: { styleOverrides: { root: ({ theme }) => ({ color: theme.vars.palette.text.disabled }) } },

  MuiAvatar: {
    styleOverrides: {
      root: ({ theme }) => ({ backgroundColor: theme.vars.palette.primary.soft, color: theme.vars.palette.primary.softText, fontSize: '0.875rem', fontWeight: 500 }),
    },
  },
  MuiLinearProgress: { styleOverrides: { root: ({ theme }) => ({ height: 4, borderRadius: 2, backgroundColor: theme.vars.palette.neutral.soft }), bar: { borderRadius: 2 } } },
  MuiSkeleton: {
    styleOverrides: { root: ({ theme }) => ({ backgroundColor: theme.vars.palette.neutral.soft, '@media (prefers-reduced-motion: reduce)': { animation: 'none' } }) },
  },
  MuiDivider: { styleOverrides: { root: ({ theme }) => ({ borderColor: theme.vars.palette.divider }) } },
}
