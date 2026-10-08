import Chip from '@mui/material/Chip'
import { CloudAlert, CloudUpload } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { usePendingCount } from '../offline/outbox.ts'
import { usePushState } from '../offline/usePushState.ts'

/**
 * In the top bar while changes wait on this device for the server: how many, as a link to the page that lists them; after a sync the
 * server could not apply some of, how many (until the next sync). Gone otherwise.
 */
export function PendingChip() {
  const { t } = useTranslation()
  const count = usePendingCount()
  const notApplied = usePushState().state.last?.parked.length ?? 0
  if (count === 0 && notApplied === 0) return null
  const waiting = count > 0
  return (
    <Chip
      component={Link}
      to="/sync"
      clickable
      className="tk-appear"
      color={waiting ? 'neutral' : 'warning'}
      icon={waiting ? <CloudUpload size={14} aria-hidden /> : <CloudAlert size={14} aria-hidden />}
      label={waiting ? t('offline.waiting', { count }) : t('offline.notApplied', { count: notApplied })}
      sx={{ minHeight: 44 }}
    />
  )
}
