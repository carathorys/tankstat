import Chip from '@mui/material/Chip'
import { CloudOff } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { visuallyHidden } from '../components/visuallyHidden.ts'
import { useConnectivity } from '../offline/useConnectivity.ts'

/**
 * In the top bar: "Offline" while the server is out of reach, and one live region that says so when it goes away and when it is back
 * (only on a change, so a screen reader hears it once).
 */
export function ConnectivityIndicator({ compact = false }: { compact?: boolean }) {
  const { t } = useTranslation()
  const { reachable } = useConnectivity()
  const [shown, setShown] = useState(reachable)
  const [announcement, setAnnouncement] = useState('')
  if (reachable !== shown) {
    setShown(reachable)
    setAnnouncement(reachable ? t('connectivity.announceOnline') : t('connectivity.announceOffline'))
  }
  return (
    <>
      <span role="status" style={visuallyHidden}>
        {announcement}
      </span>
      {/* On a phone only the icon shows; the word stays in the chip for screen readers. */}
      {!reachable && (
        <Chip
          className="tk-appear"
          color="warning"
          icon={<CloudOff size={14} aria-hidden />}
          label={compact ? <span style={visuallyHidden}>{t('connectivity.offline')}</span> : t('connectivity.offline')}
          sx={compact ? { flexShrink: 0, '& .MuiChip-label': { px: 0.5 } } : { flexShrink: 0 }}
        />
      )}
    </>
  )
}
