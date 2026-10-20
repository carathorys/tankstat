import { createContext, useContext } from 'react'

/** A short message at the bottom of the screen: "Saved", or "Moved to the trash" with Undo. */
export interface Toast {
  message: string
  severity?: 'success' | 'info' | 'warning' | 'error'
  action?: { label: string; run: () => unknown }
}

export interface ToastApi {
  toast: (toast: Toast | string) => void
  /** A message with Undo (shown longer): `undo` puts back what was just done; if it fails, an error message says so. */
  undoable: (message: string, undo: () => Promise<unknown>) => void
}

export const ToastContext = createContext<ToastApi | null>(null)

const silent: ToastApi = { toast: () => undefined, undoable: () => undefined }

/** The app's messages (ThemeRoot provides them); nothing shows outside a provider. */
export function useToast(): ToastApi {
  return useContext(ToastContext) ?? silent
}
