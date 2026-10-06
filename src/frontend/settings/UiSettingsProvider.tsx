import { useMutation, useQuery } from '@apollo/client/react'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ResetGridSettingsDocument, SaveGridSettingsDocument, UiSettingsDocument, UpdateUiSettingsDocument } from '../gql/generated.ts'
import { useStoredState } from '../hooks/useStoredState.ts'
import { LANGUAGES } from '../i18n/index.ts'
import type { GridSaved } from './types.ts'
import { UiSettingsContext, type UiSettingsApi } from './uiSettingsContext.ts'

const isBoolean = (value: unknown): value is boolean => typeof value === 'boolean'

/** A settings save never bothers the user: the Apollo error link already wrote a failure to the console, the browser's copy still applies. */
const tell = (saved: Promise<unknown>) => void saved.catch(() => undefined)

/**
 * What the UI remembers for the user (the sidebar, each grid, the language), on every device. The browser keeps a copy of everything
 * (localStorage, as before), so the first paint never waits; the server is asked once per session and wins when it answers, except for a
 * setting the user changed meanwhile. Nothing the server does not know about is pushed to it unprompted, and a failed save stays in the
 * browser only. Mount it with a `key` per user, so a newly signed-in user's settings win over what a visitor chose on the login screen.
 */
export function UiSettingsProvider({ enabled, children }: { enabled: boolean; children: ReactNode }) {
  const { i18n } = useTranslation()
  const [navOpen, storeNavOpen] = useStoredState('tankstat.nav.open', true, isBoolean)
  // What the user saved or reset (null) in this session: newer than the server's answer, which stays as it came.
  const [overrides, setOverrides] = useState<Record<string, GridSaved | null>>({})
  const navTouched = useRef(false) // changed in this session: the server's older value must not undo it
  const languageTouched = useRef(false)
  const { data } = useQuery(UiSettingsDocument, { skip: !enabled })
  const [updateUi] = useMutation(UpdateUiSettingsDocument)
  const [saveGridOnServer] = useMutation(SaveGridSettingsDocument)
  const [resetGridOnServer] = useMutation(ResetGridSettingsDocument)

  const server = data?.uiSettings
  const serverGrids = useMemo(
    () => Object.fromEntries((server?.grids ?? []).map((g) => [g.gridId, { order: g.order, hidden: g.hidden, pageSize: g.pageSize, sortColumn: g.sortColumn, sortDirection: g.sortDirection } satisfies GridSaved])),
    [server],
  )

  // The server's answer reaches the browser's copies: the sidebar, and the language through i18next (which caches it itself).
  useEffect(() => {
    if (!server) return
    if (server.navOpen != null && !navTouched.current) storeNavOpen(server.navOpen)
    // An unknown code is ignored here: i18next would silently fall back to English for it.
    if (server.language && !languageTouched.current && LANGUAGES.some((l) => l.code === server.language) && i18n.resolvedLanguage !== server.language) {
      void i18n.changeLanguage(server.language)
    }
  }, [server, storeNavOpen, i18n])

  const setNavOpen = useCallback(
    (open: boolean) => {
      navTouched.current = true
      storeNavOpen(open)
      if (enabled) tell(updateUi({ variables: { input: { navOpen: open } } }))
    },
    [enabled, storeNavOpen, updateUi],
  )
  const setLanguage = useCallback(
    (code: string) => {
      languageTouched.current = true
      if (enabled) tell(updateUi({ variables: { input: { language: code } } }))
    },
    [enabled, updateUi],
  )
  const saveGrid = useCallback(
    (gridId: string, saved: GridSaved) => {
      setOverrides((known) => ({ ...known, [gridId]: saved }))
      if (enabled) tell(saveGridOnServer({ variables: { input: { gridId, ...saved } } }))
    },
    [enabled, saveGridOnServer],
  )
  const resetGrid = useCallback(
    (gridId: string) => {
      setOverrides((known) => ({ ...known, [gridId]: null }))
      if (enabled) tell(resetGridOnServer({ variables: { gridId } }))
    },
    [enabled, resetGridOnServer],
  )
  const ready = server !== undefined
  const grid = useCallback(
    (gridId: string) => {
      if (!ready) return undefined
      const override = overrides[gridId]
      return override === null ? undefined : (override ?? serverGrids[gridId])
    },
    [ready, overrides, serverGrids],
  )

  const value = useMemo<UiSettingsApi>(
    () => ({ enabled, ready, server, navOpen, setNavOpen, setLanguage, grid, saveGrid, resetGrid }),
    [enabled, ready, server, navOpen, setNavOpen, setLanguage, grid, saveGrid, resetGrid],
  )
  return <UiSettingsContext.Provider value={value}>{children}</UiSettingsContext.Provider>
}
