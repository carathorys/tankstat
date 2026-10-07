import { createContext, useContext } from 'react'

export const DialogCloseContext = createContext<(() => void) | null>(null)

/** Closes the DialogFrame around the caller (a Cancel or Done button); does nothing outside one. */
export function useDialogClose(): () => void {
  return useContext(DialogCloseContext) ?? noop
}

const noop = () => undefined
