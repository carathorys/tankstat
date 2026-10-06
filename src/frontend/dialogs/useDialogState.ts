import { useCallback, useState } from 'react'

/**
 * Whether a dialog is open: its own state when it opens from its trigger, or the caller's (`open` + `onOpenChange`) when something else
 * opens it (a menu, the floating add button).
 */
export function useDialogState({ open, onOpenChange }: { open?: boolean; onOpenChange?: (open: boolean) => void } = {}) {
  const [own, setOwn] = useState(false)
  const set = useCallback(
    (next: boolean) => {
      if (open === undefined) setOwn(next)
      onOpenChange?.(next)
    },
    [open, onOpenChange],
  )
  return [open ?? own, set] as const
}
