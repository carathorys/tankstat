import { Badge } from '@radix-ui/themes'
import { useTranslation } from 'react-i18next'

export function StatusBadge({ status }: { status: string }) {
  const { t } = useTranslation()
  const ok = status === 'ok'

  return (
    <Badge role="status" data-status={status} color={ok ? 'green' : 'red'}>
      {ok ? t('status.healthy') : t('status.degraded')}
    </Badge>
  )
}
