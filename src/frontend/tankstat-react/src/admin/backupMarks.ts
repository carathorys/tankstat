import { useTranslation } from 'react-i18next'
import { useFormat } from '../i18n/format.ts'

/**
 * Whether a backup holds its server's secrets in plain: anyone who has the file reads them (`Backups:Secrets` on while backups were not
 * encrypted). Both lists mark such a backup, in words and with an icon of its own, not by colour alone.
 */
export const secretsInPlain = (point: { encrypted: boolean; settingsFile?: { secrets: boolean } | null }) => !point.encrypted && point.settingsFile?.secrets === true

/** A held backup in words: retention never removes it, and it is not deleted until released. */
export function useHeldText() {
  const { t } = useTranslation()
  const { dateTime } = useFormat()
  return (point: { held: boolean; heldSince?: string | null }) =>
    point.held ? (point.heldSince ? t('admin.backups.heldSince', { since: dateTime(point.heldSince) }) : t('admin.backups.held')) : null
}
