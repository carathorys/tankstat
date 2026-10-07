import Stack from '@mui/material/Stack'
import Switch from '@mui/material/Switch'
import Typography from '@mui/material/Typography'
import { useId } from 'react'
import { COARSE } from '../theme/components.ts'

/** A Switch with its label and an optional hint next to it, both linked to the switch (the hint as its description). */
export function LabeledSwitch({
  label,
  hint,
  checked,
  onChange,
}: {
  label: string
  hint?: string
  checked: boolean
  onChange: (checked: boolean) => void
}) {
  const id = useId()
  const hintId = `${id}-hint`
  return (
    <Stack direction="row" sx={{ alignItems: 'center', gap: 1.5, [COARSE]: { minHeight: 44 } }}>
      <Switch id={id} checked={checked} onChange={(event) => onChange(event.target.checked)} slotProps={{ input: { 'aria-describedby': hint ? hintId : undefined } }} />
      <Stack>
        <Typography component="label" htmlFor={id} variant="body2" sx={{ fontWeight: 700 }}>
          {label}
        </Typography>
        {hint && (
          <Typography id={hintId} variant="caption" sx={{ color: 'text.secondary' }}>
            {hint}
          </Typography>
        )}
      </Stack>
    </Stack>
  )
}
