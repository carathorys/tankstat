import type { Components, CSSObject, Shadows, Theme } from '@mui/material/styles'
import { SelectChevron } from './icons.tsx'
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

/**
 * The translucent, blurred panel of the app (the old `.glass`): the top bar, the sidebar, dialogs, menus, popovers, cards. A little
 * denser in the light scheme, where a dark picture behind a panel would otherwise muddy it.
 */
export const glass = (theme: T): CSSObject => ({
  backgroundColor: `rgba(${theme.vars.palette.background.paperChannel} / 0.7)`,
  backgroundImage: 'none',
  backdropFilter: 'blur(14px) saturate(140%)',
  WebkitBackdropFilter: 'blur(14px) saturate(140%)',
  ...theme.applyStyles('light', { backgroundColor: `rgba(${theme.vars.palette.background.paperChannel} / 0.85)` }),
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

/**
 * Motion for state changes, as classes (short; under reduced motion only the change itself): tk-appear, something new comes in (a message,
 * a note under a field); tk-fade, a value swaps (a badge); tk-delayed, a waiting indicator that only shows when the wait is noticeable;
 * tk-pulse, something is still in progress; tk-spin, a refresh that is running. Leaving and moving items use motion's AnimatePresence and
 * layout (components/motion.ts).
 */
const MOTION_CLASSES: CSSObject = {
  '@keyframes tk-appear': { from: { opacity: 0, transform: 'translateY(4px)' } },
  '@keyframes tk-fade': { from: { opacity: 0 } },
  '@keyframes tk-pulse': { '50%': { opacity: 0.35 } },
  '@keyframes tk-spin': { to: { transform: 'rotate(360deg)' } },
  '.tk-appear': { animation: 'tk-appear 0.2s ease-out both' },
  '.tk-fade': { animation: 'tk-fade 0.2s ease-out both' },
  '.tk-delayed': { animation: 'tk-fade 0.2s ease-out 0.3s both' },
  '.tk-pulse': { animation: 'tk-pulse 1.6s ease-in-out infinite' },
  '.tk-spin': { animation: 'tk-spin 0.9s linear infinite' },
}

/** The surface, outline and focus ring of a text field (also the date picker's and the select's). */
const fieldSurface = (theme: T, outline: string): CSSObject => ({
  ...theme.typography.body2,
  borderRadius: 9999,
  backgroundColor: 'rgba(0, 0, 0, 0.25)',
  ...theme.applyStyles('light', { backgroundColor: 'rgba(255, 255, 255, 0.9)' }),
  [`& .${outline}`]: { borderColor: theme.vars.palette.divider },
  [`&:hover .${outline}`]: { borderColor: theme.vars.palette.text.disabled },
  [`&.Mui-focused .${outline}`]: { borderColor: theme.vars.palette.primary.main, borderWidth: 2 },
  [`&.Mui-error .${outline}`]: { borderColor: theme.vars.palette.error.main },
  [COARSE]: { minHeight: 44 },
})

export const components: Components<T> = {
  MuiCssBaseline: {
    styleOverrides: (theme) => ({
      // The shadows of the default (dark) scheme at the root, the light ones with the light class (theme.ts: colorSchemeSelector); a part
      // that is always dark (a vehicle's banner and cards: the `dark` class) has the dark ones in either scheme, like MUI's colours.
      ':root, .dark': shadowTokens('dark'),
      '.light': shadowTokens('light'),
      'html, body': { height: '100%' },
      // The dim, layered look: a soft glow of the accent and the gray behind the translucent, blurred panels.
      body: {
        background: `radial-gradient(1200px 600px at 10% -10%, ${theme.vars.palette.primary.soft}, transparent 60%), radial-gradient(900px 500px at 100% 0%, ${theme.vars.palette.neutral.soft}, transparent 55%), ${theme.vars.palette.background.default}`,
        backgroundAttachment: 'fixed',
      },
      ...MOTION_CLASSES,
      '@media (prefers-reduced-motion: reduce)': {
        '*': { scrollBehavior: 'auto !important' },
        '.tk-appear, .tk-fade, .tk-delayed, .tk-pulse, .tk-spin': { animation: 'none' },
      },
      // Phones: tighter cells in the grids that are still Radix tables, so the columns and the two action buttons fit without sideways
      // scrolling. Goes with the last of them.
      '@media (max-width: 767px)': { '.rt-TableCell, .rt-TableColumnHeaderCell': { paddingInline: 8 } },
    }),
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
      // Radix Badge sizes 1 and 2; the icon as far from the edge as the text is, with Radix's gap to the text.
      sizeSmall: { height: 20, fontSize: '0.75rem', lineHeight: '1rem', '& .MuiChip-label': { padding: '0 6px' }, '& .MuiChip-icon': { marginLeft: 6, marginRight: -2 } },
      sizeMedium: { height: 24, fontSize: '0.75rem', '& .MuiChip-label': { padding: '0 8px' }, '& .MuiChip-icon': { marginLeft: 8, marginRight: -2 } },
      icon: { color: 'inherit' },
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
      // A note in gray (no severity of its own): `color="neutral"` wins over the severity's colours.
      {
        props: { variant: 'standard' as const, color: 'neutral' as const },
        style: ({ theme }: { theme: T }) => ({ backgroundColor: theme.vars.palette.neutral.soft, color: theme.vars.palette.text.primary }),
      },
    ],
  },

  MuiFormLabel: {
    styleOverrides: {
      root: ({ theme }) => ({ ...theme.typography.body2, fontWeight: 700, color: theme.vars.palette.text.primary, '&.Mui-focused, &.Mui-error': { color: theme.vars.palette.text.primary } }),
    },
  },
  MuiFormHelperText: {
    styleOverrides: { root: ({ theme }) => ({ ...theme.typography.caption, margin: 0, color: theme.vars.palette.text.secondary, '&.Mui-error': { color: theme.vars.palette.error.softText } }) },
  },
  MuiOutlinedInput: {
    styleOverrides: {
      root: ({ theme }) => fieldSurface(theme, 'MuiOutlinedInput-notchedOutline'),
      input: { padding: '6px 8px', height: '1.25rem', '&::placeholder': { opacity: 0.6 } },
      // At least as tall as the Radix text area was; the text starts at the top.
      multiline: { borderRadius: 9, padding: '8px', minHeight: 64, alignItems: 'flex-start', '& .MuiOutlinedInput-input': { padding: 0, height: 'auto' } },
    },
  },
  MuiAutocomplete: {
    styleOverrides: {
      // The field is a plain OutlinedInput (no TextField): the same height and padding as every other field, not the autocomplete's own.
      root: { '& .MuiOutlinedInput-root': { padding: 0, '& .MuiAutocomplete-input': { padding: '6px 8px' } } },
      // The suggestions look like a menu.
      paper: ({ theme }) => ({ ...glass(theme), borderRadius: 12, marginTop: 4 }),
      listbox: { padding: 8 },
      option: ({ theme }) => ({
        ...theme.typography.body2,
        borderRadius: 6,
        // The class twice: MUI's own sizes for options come later in the page with the same weight.
        '&.MuiAutocomplete-option': { minHeight: controlHeights.medium, padding: '0 12px' },
        [COARSE]: { '&.MuiAutocomplete-option': { minHeight: 44 } },
      }),
    },
  },
  MuiSelect: {
    defaultProps: { IconComponent: SelectChevron },
    styleOverrides: {
      select: { padding: '6px 32px 6px 12px', minHeight: '1.25rem' },
      icon: ({ theme }) => ({ color: theme.vars.palette.text.secondary, right: 10, top: 'calc(50% - 8px)' }),
    },
  },
  // The date picker's field looks like a text field (its sections sit where the text would). The outlined variant sets its own corners
  // and padding after the base's, so these go on it.
  MuiPickersOutlinedInput: {
    styleOverrides: {
      root: ({ theme }) => ({
        ...fieldSurface(theme, 'MuiPickersOutlinedInput-notchedOutline'),
        padding: '0 4px 0 8px',
        '& .MuiInputAdornment-root': { marginLeft: 4 },
      }),
      sectionsContainer: { padding: '6px 0' },
    },
  },

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
      switchBase: ({ theme }) => ({
        padding: 2,
        '&.Mui-checked': { transform: 'translateX(20px)', color: '#ffffff' },
        // Full strength when on, as the Radix switch was (MUI fades it to half; a disabled one keeps MUI's own, weightier rule).
        '&.Mui-checked + .MuiSwitch-track': { opacity: 1, backgroundColor: theme.vars.palette.primary.main },
      }),
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
