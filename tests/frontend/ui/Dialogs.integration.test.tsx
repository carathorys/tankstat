import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import Button from '@mui/material/Button'
import { useState } from 'react'
import { expect, it, vi } from 'vitest'
import { ConfirmDialog } from '../../../src/frontend/components/ConfirmDialog.tsx'
import { DialogButtons, DialogCancel, DialogFrame } from '../../../src/frontend/dialogs/DialogFrame.tsx'
import { DialogTrigger } from '../../../src/frontend/dialogs/DialogTrigger.tsx'
import { useDialogState } from '../../../src/frontend/dialogs/useDialogState.ts'
import { ThemeRoot } from '../../../src/frontend/theme/ThemeRoot.tsx'

function Demo({ busy = false, onClosed }: { busy?: boolean; onClosed?: () => void }) {
  const [open, setOpen] = useDialogState()
  return (
    <>
      <DialogTrigger trigger={<Button>Open</Button>} open={open} onOpen={() => setOpen(true)} />
      <DialogFrame open={open} onClose={() => setOpen(false)} onClosed={onClosed} busy={busy} title="Add refuelling" description="Enter what you filled up.">
        <DialogButtons>
          <DialogCancel />
        </DialogButtons>
      </DialogFrame>
    </>
  )
}

const show = (ui: React.ReactNode) => render(<ThemeRoot instant>{ui}</ThemeRoot>)

it('opens from its trigger, is named and described, and gives the focus back to the trigger when cancelled', async () => {
  const ui = userEvent.setup()
  const closed = vi.fn()
  show(<Demo onClosed={closed} />)
  const trigger = screen.getByRole('button', { name: 'Open' })
  expect(trigger).toHaveAttribute('aria-haspopup', 'dialog')
  expect(trigger).toHaveAttribute('aria-expanded', 'false')

  await ui.click(trigger)
  const dialog = await screen.findByRole('dialog', { name: 'Add refuelling' })
  expect(dialog).toHaveAccessibleDescription('Enter what you filled up.')
  expect(trigger).toHaveAttribute('aria-expanded', 'true')

  await ui.click(screen.getByRole('button', { name: 'Cancel' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(trigger).toHaveFocus()
  await waitFor(() => expect(closed).toHaveBeenCalledOnce())
})

it('cannot be closed with Escape while it is busy saving', async () => {
  const ui = userEvent.setup()
  const { rerender } = show(<Demo busy />)
  await ui.click(screen.getByRole('button', { name: 'Open' }))
  await screen.findByRole('dialog')

  await ui.keyboard('{Escape}')
  expect(screen.getByRole('dialog')).toBeInTheDocument()

  rerender(
    <ThemeRoot instant>
      <Demo />
    </ThemeRoot>,
  )
  await ui.keyboard('{Escape}')
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
})

it('a confirmation is an alert dialog that starts on Cancel and confirms on purpose only', async () => {
  const ui = userEvent.setup()
  const confirm = vi.fn()
  function Page() {
    const [shown] = useState(true)
    return shown ? <ConfirmDialog trigger={<Button>Delete</Button>} title="Move Octavia to the trash?" description="It can be restored." confirmLabel="Move to trash" onConfirm={confirm} /> : null
  }
  show(<Page />)
  await ui.click(screen.getByRole('button', { name: 'Delete' }))

  const dialog = await screen.findByRole('alertdialog', { name: 'Move Octavia to the trash?' })
  expect(dialog).toHaveAccessibleDescription('It can be restored.')
  expect(screen.getByRole('button', { name: 'Cancel' })).toHaveFocus()
  await ui.keyboard('{Enter}') // Enter on the focused Cancel
  await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
  expect(confirm).not.toHaveBeenCalled()

  await ui.click(screen.getByRole('button', { name: 'Delete' }))
  await ui.click(await screen.findByRole('button', { name: 'Move to trash' }))
  expect(confirm).toHaveBeenCalledOnce()
})
