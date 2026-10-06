import Alert from '@mui/material/Alert'
import Chip from '@mui/material/Chip'
import { CircleAlert, Hourglass, ScanText, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { ReviewState } from '../gql/generated.ts'

/** How each state looks; the text always says it too (never colour alone). */
const STYLE: Record<Exclude<ReviewState, 'NONE'>, { tone: 'neutral' | 'info' | 'warning'; Icon: LucideIcon }> = {
  AWAITING_PHOTOS: { tone: 'neutral', Icon: Hourglass },
  NEEDS_REVIEW: { tone: 'info', Icon: ScanText },
  INCOMPLETE: { tone: 'warning', Icon: CircleAlert },
}

/** Next to a log in a list: its photo is still being read, values were read from it (check them), or values are missing. */
export function ReviewBadge({ state }: { state: ReviewState | undefined }) {
  const { t } = useTranslation()
  if (!state || state === 'NONE') return null
  const { tone, Icon } = STYLE[state]
  return (
    <Chip
      key={state}
      color={tone}
      className="tk-fade"
      icon={<Icon size={12} aria-hidden className={state === 'AWAITING_PHOTOS' ? 'tk-pulse' : undefined} />}
      label={t(`review.badge.${state}`)}
    />
  )
}

/** At the top of a log's edit dialog: what its photos did, and what saving it does. */
export function ReviewCallout({ state }: { state: ReviewState | undefined }) {
  const { t } = useTranslation()
  if (!state || state === 'NONE') return null
  const { tone, Icon } = STYLE[state]
  return (
    <Alert
      severity={tone === 'neutral' ? 'info' : tone}
      color={tone === 'neutral' ? 'neutral' : undefined}
      role="none"
      icon={<Icon size={16} aria-hidden />}
      className="tk-appear"
      sx={{ mb: 1.5 }}
    >
      {t(`review.callout.${state}`)}
    </Alert>
  )
}
