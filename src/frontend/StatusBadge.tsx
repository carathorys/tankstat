import Chip from '@mui/material/Chip'
import { useTranslation } from 'react-i18next'

export function StatusBadge({ status }: { status: string }) {
  const { t } = useTranslation()
  const ok = status === 'ok'

  return <Chip component="span" role="status" data-status={status} color={ok ? 'success' : 'error'} label={ok ? t('status.healthy') : t('status.degraded')} />
}
