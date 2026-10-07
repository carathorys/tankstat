import Box from '@mui/material/Box'
import { useId } from 'react'
import { COARSE } from '../theme/components.ts'
import { visuallyHidden } from './visuallyHidden.ts'

/**
 * A choice between a few options, drawn as one pill with segments: real radio buttons (arrow keys move between them, a screen reader
 * hears a radio group), each in its segment's label; the chosen segment is raised.
 */
export function Segmented<V extends string>({
  label,
  value,
  options,
  onChange,
}: {
  label: string
  value: V
  options: readonly { value: V; label: string }[]
  onChange: (value: V) => void
}) {
  const name = useId()
  return (
    <Box
      role="radiogroup"
      aria-label={label}
      sx={(theme) => ({ display: 'inline-flex', p: '3px', borderRadius: 9999, backgroundColor: theme.vars.palette.neutral.soft })}
    >
      {options.map((option) => (
        <Box
          component="label"
          key={option.value}
          sx={(theme) => ({
            ...theme.typography.body1,
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            minHeight: 36,
            minWidth: 80,
            [COARSE]: { minHeight: 44 },
            px: 2,
            borderRadius: 9999,
            cursor: 'pointer',
            color: theme.vars.palette.text.primary,
            // The chosen segment is raised: its own surface and shadow, and bold text, so it does not depend on a tint.
            '&:has(input:checked)': { backgroundColor: theme.vars.palette.background.paper, boxShadow: theme.shadows[2], fontWeight: theme.typography.fontWeightBold },
            '&:has(input:focus-visible)': theme.focusVisible,
          })}
        >
          <input type="radio" name={name} value={option.value} checked={option.value === value} onChange={() => onChange(option.value)} style={visuallyHidden} />
          {option.label}
        </Box>
      ))}
    </Box>
  )
}
