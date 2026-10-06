import type { Theme } from '@mui/material/styles'

/**
 * A row of the navigation (`sx`): icon and text, a comfortable touch target, a gray tint under the pointer and the accent's for the page
 * that is open (aria-current, set by NavLink). Buttons among the links (install, sign out) share it.
 */
export const navItemSx = (theme: Theme) => ({
  ...theme.typography.body1,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'flex-start',
  gap: 1.5,
  minHeight: 44,
  px: 1.5,
  borderRadius: '9px',
  color: theme.vars.palette.text.primary,
  textDecoration: 'none',
  '&:hover': { backgroundColor: theme.vars.palette.neutral.softHover },
  '&[aria-current="page"]': { backgroundColor: theme.vars.palette.primary.softHover, fontWeight: theme.typography.fontWeightMedium },
  '&:focus-visible': theme.focusVisible,
})
