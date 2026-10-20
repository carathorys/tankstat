import Chip from '@mui/material/Chip'
import Stack from '@mui/material/Stack'
import { visuallyHidden } from './visuallyHidden.ts'

/** A short word beside a control's label (where its value comes from, that it waits for something), with what it means for a screen reader. */
export interface LabelBadge {
  /** The word shown. */
  text: string
  /** What it means, said in full instead of the word (a word alone, such as "Settings file", is not a sentence). */
  spoken: string
  tone?: 'neutral' | 'warning'
}

/**
 * Badges beside a label. They are not part of the label (the control's name stays its label); the caller links `id` into the control's
 * description, where the spoken text is read.
 */
export function LabelBadges({ id, badges }: { id?: string; badges: readonly LabelBadge[] }) {
  if (badges.length === 0) return null
  return (
    <Stack component="span" id={id} direction="row" sx={{ gap: 0.5, flexWrap: 'wrap', alignItems: 'center' }}>
      {badges.map((b) => (
        <span key={b.text}>
          <Chip component="span" size="small" variant="soft" color={b.tone === 'warning' ? 'warning' : 'default'} label={b.text} aria-hidden sx={{ height: 20, fontSize: '0.75rem' }} />
          <span style={visuallyHidden}>{b.spoken}</span>
        </span>
      ))}
    </Stack>
  )
}
