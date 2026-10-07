import Chip from '@mui/material/Chip'
import { AlertTriangle, CheckCircle2, Clock } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { RecurrenceLimit, RecurrenceState } from '../gql/generated.ts'
import { RECURRENCE_TONE } from '../recurringProgress.ts'

export interface RecurrenceStatusLike {
  state: RecurrenceState
  limit: RecurrenceLimit | null
  daysLeft: number | null
  distanceLeft: number | null
}

const ICONS = { UPCOMING: CheckCircle2, DUE_SOON: Clock, OVERDUE: AlertTriangle } as const

/** The state as a badge with an icon and its name, so it never relies on colour alone. */
export function RecurringStatusBadge({ state, solid = false }: { state: RecurrenceState; solid?: boolean }) {
  const { t } = useTranslation()
  const tone = RECURRENCE_TONE[state]
  const Icon = ICONS[state]
  return <Chip key={state} className="tk-fade" size="medium" color={tone} variant={solid ? 'solid' : 'soft'} icon={<Icon size={14} aria-hidden />} label={t(`recurring.state.${state}`)} />
}
