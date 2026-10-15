import Button from '@mui/material/Button'
import { useTranslation } from 'react-i18next'
import { DialogButtons, DialogCancel, DialogFrame } from '../dialogs/DialogFrame.tsx'
import type { HeldHere } from './useSignOut.ts'

/**
 * Asked before a sign-out that deletes what only this device holds (#162): how many changes have not reached the server and how many
 * rows are set aside, all deleted with the rest of the account's data on this device. Cancel keeps everything.
 */
export function SignOutConfirm({ held, onCancel, onConfirm }: { held: HeldHere | null; onCancel: () => void; onConfirm: () => void }) {
  const { t } = useTranslation()
  const parts = [
    held && held.changes > 0 ? t('sync.signOutChanges', { count: held.changes }) : null,
    held && held.found > 0 ? t('sync.signOutFound', { count: held.found }) : null,
    t('sync.signOutDeleted'),
  ].filter(Boolean)
  return (
    <DialogFrame open={held !== null} onClose={onCancel} title={t('sync.signOutTitle')} description={parts.join(' ')} role="alertdialog">
      <DialogButtons>
        <DialogCancel autoFocus />
        <Button color="error" onClick={onConfirm}>
          {t('sync.signOutConfirm')}
        </Button>
      </DialogButtons>
    </DialogFrame>
  )
}
