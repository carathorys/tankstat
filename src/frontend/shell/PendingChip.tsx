import Chip from '@mui/material/Chip'
import { CloudUpload } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { usePendingCount } from '../offline/outbox.ts'

/** In the top bar while changes wait on this device for the server: how many, as a link to the page that lists them. Gone when none does. */
export function PendingChip() {
  const { t } = useTranslation()
  const count = usePendingCount()
  if (count === 0) return null
  return (
    <Chip
      component={Link}
      to="/sync"
      clickable
      className="tk-appear"
      color="neutral"
      icon={<CloudUpload size={14} aria-hidden />}
      label={t('offline.waiting', { count })}
      sx={{ minHeight: 44 }}
    />
  )
}
