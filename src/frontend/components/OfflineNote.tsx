import Alert from '@mui/material/Alert'
import { CloudOff, ScanText } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useConnectivity } from '../offline/useConnectivity.ts'

/** Atop a log's dialog while the server is out of reach: what is saved stays on this device until it is back. Calm, not an error. */
export function OfflineNote() {
  const { t } = useTranslation()
  const { reachable } = useConnectivity()
  if (reachable) return null
  return (
    <Alert severity="info" icon={<CloudOff size={16} aria-hidden />} sx={{ mb: 1.5 }}>
      {t('offline.dialogNote')}
    </Alert>
  )
}

/**
 * In an add dialog with photos kept on this device while photo reading is on (as last heard): they are read once the log reaches the
 * server, so the values a photo would give may be left empty now.
 */
export function ReadLaterNote() {
  const { t } = useTranslation()
  return (
    <Alert severity="info" icon={<ScanText size={16} aria-hidden />} sx={{ mb: 1.5 }}>
      {t('reading.later')}
    </Alert>
  )
}
