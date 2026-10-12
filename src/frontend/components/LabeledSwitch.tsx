import Stack from '@mui/material/Stack'
import Switch from '@mui/material/Switch'
import Typography from '@mui/material/Typography'
import { useId } from 'react'
import { COARSE } from '../theme/components.ts'
import { LabelBadges, type LabelBadge } from './LabelBadges.tsx'

/** A Switch with its label and an optional hint next to it, both linked to the switch (the hint as its description). */
export function LabeledSwitch({
  label,
  hint,
  checked,
  onChange,
  disabled,
  badges,
}: {
  label: string
  hint?: string
  checked: boolean
  onChange: (checked: boolean) => void
  /** Shown but not changeable; the hint should say why. */
  disabled?: boolean
  /** Short words beside the label (where a value comes from); read as the start of the switch's description. */
  badges?: readonly LabelBadge[]
}) {
  const id = useId()
  const hintId = `${id}-hint`
  const badgesId = `${id}-badges`
  const hasBadges = (badges?.length ?? 0) > 0
  const describedBy = [hasBadges && badgesId, hint && hintId].filter(Boolean).join(' ') || undefined
  return (
    <Stack direction="row" sx={{ alignItems: 'center', gap: 1.5, [COARSE]: { minHeight: 44 } }}>
      <Switch id={id} checked={checked} disabled={disabled} onChange={(event) => onChange(event.target.checked)} slotProps={{ input: { 'aria-describedby': describedBy } }} />
      <Stack>
        <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
          <Typography component="label" htmlFor={id} variant="body2" sx={{ fontWeight: 700 }}>
            {label}
          </Typography>
          {hasBadges && <LabelBadges id={badgesId} badges={badges!} />}
        </Stack>
        {hint && (
          <Typography id={hintId} variant="caption" sx={{ color: 'text.secondary' }}>
            {hint}
          </Typography>
        )}
      </Stack>
    </Stack>
  )
}
