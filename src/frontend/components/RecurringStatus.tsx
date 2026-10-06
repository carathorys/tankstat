import { Badge } from '@radix-ui/themes'
import { AlertTriangle, CheckCircle2, Clock } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { RecurrenceLimit, RecurrenceState } from '../gql/generated.ts'

export interface RecurrenceStatusLike {
  state: RecurrenceState
  limit: RecurrenceLimit | null
  daysLeft: number | null
  distanceLeft: number | null
}

const STYLE = {
  UPCOMING: { color: 'green', icon: CheckCircle2 },
  DUE_SOON: { color: 'amber', icon: Clock },
  OVERDUE: { color: 'red', icon: AlertTriangle },
} as const

/** The state as a badge with an icon and its name, so it never relies on colour alone. */
export function RecurringStatusBadge({ state, solid = false }: { state: RecurrenceState; solid?: boolean }) {
  const { t } = useTranslation()
  const { color, icon: Icon } = STYLE[state]
  return (
    <Badge key={state} className="tk-fade" color={color} variant={solid ? 'solid' : 'soft'} size="2">
      <Icon size={14} aria-hidden />
      {t(`recurring.state.${state}`)}
    </Badge>
  )
}
