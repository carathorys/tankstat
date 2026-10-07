import Button from '@mui/material/Button'
import type { ReactNode } from 'react'
import { DialogButtons, DialogCancel, DialogFrame } from '../dialogs/DialogFrame.tsx'
import { DialogTrigger } from '../dialogs/DialogTrigger.tsx'
import { useDialogState } from '../dialogs/useDialogState.ts'

/** A confirmation before something hard to undo. Focus starts on Cancel (the safe choice) and returns to the trigger. */
export function ConfirmDialog({
  trigger,
  title,
  description,
  confirmLabel,
  onConfirm,
}: {
  trigger: ReactNode
  title: string
  description: string
  confirmLabel: string
  onConfirm: () => void
}) {
  const [open, setOpen] = useDialogState()
  return (
    <>
      <DialogTrigger trigger={trigger} open={open} onOpen={() => setOpen(true)} />
      <DialogFrame open={open} onClose={() => setOpen(false)} title={title} description={description} role="alertdialog">
        <DialogButtons>
          <DialogCancel autoFocus />
          <Button
            color="error"
            onClick={() => {
              setOpen(false)
              onConfirm()
            }}
          >
            {confirmLabel}
          </Button>
        </DialogButtons>
      </DialogFrame>
    </>
  )
}
