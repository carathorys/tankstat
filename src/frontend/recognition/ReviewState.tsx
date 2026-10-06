import { Badge, Callout } from '@radix-ui/themes'
import { CircleAlert, Hourglass, ScanText, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { ReviewState } from '../gql/generated.ts'

/** How each state looks; the text always says it too (never colour alone). */
const STYLE: Record<Exclude<ReviewState, 'NONE'>, { color: 'gray' | 'blue' | 'amber'; Icon: LucideIcon }> = {
  AWAITING_PHOTOS: { color: 'gray', Icon: Hourglass },
  NEEDS_REVIEW: { color: 'blue', Icon: ScanText },
  INCOMPLETE: { color: 'amber', Icon: CircleAlert },
}

/** Next to a log in a list: its photo is still being read, values were read from it (check them), or values are missing. */
export function ReviewBadge({ state }: { state: ReviewState | undefined }) {
  const { t } = useTranslation()
  if (!state || state === 'NONE') return null
  const { color, Icon } = STYLE[state]
  return (
    <Badge key={state} color={color} variant="soft" size="1" className="tk-fade">
      <Icon size={12} aria-hidden className={state === 'AWAITING_PHOTOS' ? 'tk-pulse' : undefined} />
      {t(`review.badge.${state}`)}
    </Badge>
  )
}

/** At the top of a log's edit dialog: what its photos did, and what saving it does. */
export function ReviewCallout({ state }: { state: ReviewState | undefined }) {
  const { t } = useTranslation()
  if (!state || state === 'NONE') return null
  const { color, Icon } = STYLE[state]
  return (
    <Callout.Root color={color} size="1" mb="3" className="tk-appear">
      <Callout.Icon>
        <Icon size={16} aria-hidden />
      </Callout.Icon>
      <Callout.Text>{t(`review.callout.${state}`)}</Callout.Text>
    </Callout.Root>
  )
}
