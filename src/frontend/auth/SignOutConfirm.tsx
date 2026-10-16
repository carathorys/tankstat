import Button from '@mui/material/Button'
import { useTranslation } from 'react-i18next'
import { DialogButtons, DialogCancel, DialogFrame } from '../dialogs/DialogFrame.tsx'
import { ErrorMessage } from '../messages.tsx'
import type { SignOutWarning } from './useSignOut.ts'

/**
 * Asked before a sign-out that deletes what only this device holds (#162): how many changes have not reached the server and how many
 * rows are set aside, all deleted with the rest of the account's data on this device. Cancel keeps everything. Shown again, saying so,
 * when a sign-out failed (the device could neither keep it nor tell the server): nothing was deleted then.
 */
export function SignOutConfirm({ held, open, failed, onCancel, onConfirm }: SignOutWarning & { onCancel: () => void; onConfirm: () => void }) {
  const { t } = useTranslation()
  // With nothing held (a sign-out that failed), only the failure is said.
  const parts =
    held.changes > 0 || held.found > 0
      ? [
          held.changes > 0 ? t('sync.signOutChanges', { count: held.changes }) : null,
          held.found > 0 ? t('sync.signOutFound', { count: held.found }) : null,
          t('sync.signOutDeleted'),
        ].filter(Boolean)
      : []
  return (
    <DialogFrame open={open} onClose={onCancel} title={t('sync.signOutTitle')} description={parts.join(' ') || undefined} role="alertdialog">
      {failed && <ErrorMessage>{t('sync.signOutFailed')}</ErrorMessage>}
      <DialogButtons>
        <DialogCancel autoFocus />
        <Button color="error" onClick={onConfirm}>
          {t('sync.signOutConfirm')}
        </Button>
      </DialogButtons>
    </DialogFrame>
  )
}
