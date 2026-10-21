import Chip from '@mui/material/Chip'
import { CloudUpload } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { ChangeEntity } from '../offline/changes.ts'
import { usePendingMark } from '../offline/outbox.ts'

/**
 * Something with a change waiting on this device for the server: new, changed, to be removed, restored or done, "not synced". Text and an icon,
 * never colour alone; `key` makes it fade when the mark changes.
 */
export function PendingBadge({ entity, id }: { entity: ChangeEntity; id: string }) {
  const { t } = useTranslation()
  const mark = usePendingMark(entity, id)
  if (!mark) return null
  return (
    <Chip
      key={mark}
      className="tk-fade"
      color={mark === 'deleted' ? 'warning' : 'neutral'}
      icon={<CloudUpload size={14} aria-hidden />}
      label={t(`offline.pending.${mark}`)}
    />
  )
}
