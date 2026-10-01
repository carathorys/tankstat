import { AlertDialog as RadixAlertDialog, Dialog as RadixDialog } from 'radix-ui'
import type { ReactNode } from 'react'

export function Dialog({
  open,
  onOpenChange,
  title,
  description,
  children,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description: string
  children: ReactNode
}) {
  return (
    <RadixDialog.Root open={open} onOpenChange={onOpenChange}>
      <RadixDialog.Portal>
        <RadixDialog.Overlay className="overlay" />
        <RadixDialog.Content className="dialog">
          <RadixDialog.Title>{title}</RadixDialog.Title>
          <RadixDialog.Description>{description}</RadixDialog.Description>
          {children}
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  )
}

/** Asks before something that cannot be undone or is easy to do by mistake. */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  onConfirm,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description: string
  confirmLabel: string
  onConfirm: () => void
}) {
  return (
    <RadixAlertDialog.Root open={open} onOpenChange={onOpenChange}>
      <RadixAlertDialog.Portal>
        <RadixAlertDialog.Overlay className="overlay" />
        <RadixAlertDialog.Content className="dialog">
          <RadixAlertDialog.Title>{title}</RadixAlertDialog.Title>
          <RadixAlertDialog.Description>{description}</RadixAlertDialog.Description>
          <div className="dialog-actions">
            <RadixAlertDialog.Cancel asChild>
              <button type="button">Cancel</button>
            </RadixAlertDialog.Cancel>
            <RadixAlertDialog.Action asChild>
              <button type="button" className="danger" onClick={onConfirm}>
                {confirmLabel}
              </button>
            </RadixAlertDialog.Action>
          </div>
        </RadixAlertDialog.Content>
      </RadixAlertDialog.Portal>
    </RadixAlertDialog.Root>
  )
}
