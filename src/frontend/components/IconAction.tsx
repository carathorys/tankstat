import IconButton, { type IconButtonProps } from '@mui/material/IconButton'
import Tooltip from '@mui/material/Tooltip'
import { ghostTone, softTone, type Tone } from '../theme/components.ts'

/**
 * A button with only an icon: `label` is both what assistive technology announces and the tooltip sighted users see on hover or focus.
 * `variant` soft (tinted, the default), ghost (no background until hovered) or solid; `tone` its colour.
 */
export function IconAction({
  label,
  tone = 'neutral',
  variant = 'soft',
  children,
  disabled,
  sx,
  ...rest
}: Omit<IconButtonProps, 'aria-label' | 'color'> & { label: string; tone?: Tone; variant?: 'soft' | 'ghost' | 'solid' }) {
  const button = (
    <IconButton
      aria-label={label}
      disabled={disabled}
      sx={[
        (theme) =>
          variant === 'soft' ? softTone(theme, tone)
          : variant === 'ghost' ? ghostTone(theme, tone)
          : { backgroundColor: theme.vars.palette[tone].main, color: theme.vars.palette[tone].contrastText, '&:hover': { backgroundColor: theme.vars.palette[tone].dark } },
        (theme) => ({ '&.Mui-disabled': { color: theme.vars.palette.action.disabled, backgroundColor: variant === 'ghost' ? 'transparent' : theme.vars.palette.action.disabledBackground } }),
        ...(Array.isArray(sx) ? sx : [sx]),
      ]}
      {...rest}
    >
      {children}
    </IconButton>
  )
  // The tooltip listens on a wrapper (a disabled button gets no pointer events), always the same one, so the button is never remounted
  // when it turns disabled or back (it keeps the focus). It describes the button, which its label already names.
  return (
    <Tooltip title={label} describeChild>
      <span style={{ display: 'inline-flex' }}>{button}</span>
    </Tooltip>
  )
}
