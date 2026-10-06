import Button from '@mui/material/Button'
import Dialog from '@mui/material/Dialog'
import DialogContent from '@mui/material/DialogContent'
import DialogContentText from '@mui/material/DialogContentText'
import DialogTitle from '@mui/material/DialogTitle'
import Stack from '@mui/material/Stack'
import { useId, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { DialogCloseContext, useDialogClose } from './dialogContext.ts'

/**
 * A dialog of the app: its title names it and its description describes it; Escape and a click outside close it unless it is `busy`
 * (closing while saving would lose track of the save). `onClosed` runs once it is fully gone, e.g. to put the focus somewhere when the
 * button that opened it is gone too. MUI returns the focus to that button otherwise.
 */
export function DialogFrame({
  open,
  onClose,
  onClosed,
  title,
  description,
  maxWidth = 450,
  busy = false,
  role = 'dialog',
  children,
}: {
  open: boolean
  onClose: () => void
  onClosed?: () => void
  title: ReactNode
  description?: ReactNode
  maxWidth?: number
  busy?: boolean
  /** "alertdialog" for a confirmation before something hard to undo. */
  role?: 'dialog' | 'alertdialog'
  children: ReactNode
}) {
  const descriptionId = useId()
  return (
    <Dialog
      open={open}
      onClose={(_, reason) => {
        if (busy && (reason === 'escapeKeyDown' || reason === 'backdropClick')) return
        onClose()
      }}
      aria-describedby={description ? descriptionId : undefined}
      maxWidth={false}
      slotProps={{ paper: { role, sx: { maxWidth } }, transition: { onExited: onClosed } }}
    >
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        {description && <DialogContentText id={descriptionId}>{description}</DialogContentText>}
        <DialogCloseContext.Provider value={onClose}>{children}</DialogCloseContext.Provider>
      </DialogContent>
    </Dialog>
  )
}

/** The buttons at the end of a dialog, on the right. */
export function DialogButtons({ children }: { children: ReactNode }) {
  return <Stack direction="row" sx={{ gap: 1.5, justifyContent: 'flex-end', alignItems: 'center', flexWrap: 'wrap', mt: 0.5 }}>{children}</Stack>
}

/** Cancel: closes the dialog it is in (the safe choice, so a confirmation starts on it). `label` for a dialog that only informs: "Close". */
export function DialogCancel({ disabled, autoFocus, label }: { disabled?: boolean; autoFocus?: boolean; label?: string }) {
  const { t } = useTranslation()
  const close = useDialogClose()
  return (
    <Button type="button" variant="soft" color="neutral" disabled={disabled} autoFocus={autoFocus} onClick={close}>
      {label ?? t('common.cancel')}
    </Button>
  )
}
