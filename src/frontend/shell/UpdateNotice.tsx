import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import { RefreshCw } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useAppUpdate } from '../pwa/useAppUpdate.ts'

/**
 * Above the page while a new version of the app is ready: say so and let the person choose when to reload (nothing reloads by itself).
 * When another tab already switched to it, this page still runs the old one: a reload finishes the update.
 */
export function UpdateNotice() {
  const { t } = useTranslation()
  const { ready, finishing, reload } = useAppUpdate()
  if (!ready) return null
  return (
    <Alert
      role="status"
      severity="info"
      icon={<RefreshCw size={16} aria-hidden />}
      className="tk-appear"
      sx={{ mb: 2, alignItems: 'center' }}
      action={
        <Button size="small" variant="soft" onClick={reload}>
          {t('update.reload')}
        </Button>
      }
    >
      {finishing ? t('update.finish') : t('update.available')}
    </Alert>
  )
}
