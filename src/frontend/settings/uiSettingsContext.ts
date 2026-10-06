import { createContext, useContext } from 'react'
import type { GridSaved } from './types.ts'

/** The UI settings of the current user, kept by `UiSettingsProvider`: the browser's copy at once, the server's as soon as it answers. */
export interface UiSettingsApi {
  /** Signed in, or authentication is off: the server is asked once and told about every change. Otherwise only this browser remembers. */
  enabled: boolean
  /** The server has answered (always false while not enabled). */
  ready: boolean
  /** The server's latest answer, as a token: its identity changes with every answer, so a grid knows when to look again. */
  server: object | undefined
  /** Whether the docked navigation (desktop) is open. */
  navOpen: boolean
  setNavOpen: (open: boolean) => void
  /** Tells the server about a language the user chose (the language itself is switched by i18next). */
  setLanguage: (code: string) => void
  /** A grid's settings as the server knows them (or as saved in this session); undefined when it knows none, or has not answered yet. */
  grid: (gridId: string) => GridSaved | undefined
  saveGrid: (gridId: string, saved: GridSaved) => void
  resetGrid: (gridId: string) => void
}

export const UiSettingsContext = createContext<UiSettingsApi | null>(null)

/** Null outside the provider (a grid rendered on its own in a test): the browser alone remembers then. */
export function useUiSettings(): UiSettingsApi | null {
  return useContext(UiSettingsContext)
}
