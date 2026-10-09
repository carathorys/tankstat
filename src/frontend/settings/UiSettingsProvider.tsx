import { CombinedGraphQLErrors } from '@apollo/client/errors'
import { useMutation, useQuery } from '@apollo/client/react'
import { useColorScheme } from '@mui/material/styles'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ResetGridSettingsDocument, SaveGridSettingsDocument, UiSettingsDocument, UpdateUiSettingsDocument, type ColorMode, type SurfaceStyle } from '../gql/generated.ts'
import { useStoredState } from '../hooks/useStoredState.ts'
import { LANGUAGES } from '../i18n/index.ts'
import type { ColorModeChoice } from '../theme/colorMode.ts'
import type { SurfaceChoice } from '../theme/surface.ts'
import { setSurface as applySurface } from '../theme/surfaceStore.ts'
import type { GridSaved } from './types.ts'
import { UiSettingsContext, type UiSettingsApi } from './uiSettingsContext.ts'

const isBoolean = (value: unknown): value is boolean => typeof value === 'boolean'

/** The server's colour modes and MUI's. */
const FROM_SERVER: Record<ColorMode, ColorModeChoice> = { LIGHT: 'light', DARK: 'dark', SYSTEM: 'system' }
const TO_SERVER: Record<ColorModeChoice, ColorMode> = { light: 'LIGHT', dark: 'DARK', system: 'SYSTEM' }
const SURFACE_FROM_SERVER: Record<SurfaceStyle, SurfaceChoice> = { GLOSSY: 'glossy', TRANSPARENT: 'transparent', OPAQUE: 'opaque' }
const SURFACE_TO_SERVER: Record<SurfaceChoice, SurfaceStyle> = { glossy: 'GLOSSY', transparent: 'TRANSPARENT', opaque: 'OPAQUE' }

/**
 * A settings save never bothers the user: the browser's copy applies whatever became of it. A request that failed is written to the console
 * by the Apollo error link; one the server refused with a key is written here, since no screen shows it (the error link leaves keyed errors
 * to the screen). Never rejects.
 */
const tell = (saved: Promise<unknown>): Promise<void> =>
  saved.then(
    () => undefined,
    (error: unknown) => {
      if (CombinedGraphQLErrors.is(error) && error.errors.every((e) => typeof e.extensions?.key === 'string')) {
        console.warn('A settings save was refused', error.errors.map((e) => e.extensions?.key))
      }
    },
  )

/**
 * One request per setting at a time, the newest value only: a change made while the last request is still on its way goes after it, and
 * the values in between are skipped (the server keeps the last write per row anyway). So two first saves of one row never cross.
 */
function useSerialSaves() {
  const queues = useRef(new Map<string, { next?: () => Promise<unknown> }>())
  return useCallback((key: string, request: () => Promise<unknown>) => {
    const running = queues.current.get(key)
    if (running) {
      running.next = request
      return
    }
    const entry: { next?: () => Promise<unknown> } = {}
    queues.current.set(key, entry)
    const run = (send: () => Promise<unknown>): void =>
      void tell(send()).then(() => {
        const next = entry.next
        entry.next = undefined
        if (next) run(next)
        else queues.current.delete(key)
      })
    run(request)
  }, [])
}

/**
 * What the UI remembers for the user (the sidebar, each grid, the language, the colour mode, the surfaces), on every device. The browser keeps a copy of everything
 * (localStorage, as before), so the first paint never waits; the server is asked once per session and wins when it answers, except for a
 * setting the user changed meanwhile. Nothing the server does not know about is pushed to it unprompted, and a failed save stays in the
 * browser only. Mount it with a `key` per user, so a newly signed-in user's settings win over what a visitor chose on the login screen.
 */
export function UiSettingsProvider({ enabled, children }: { enabled: boolean; children: ReactNode }) {
  const { i18n } = useTranslation()
  const { setMode } = useColorScheme()
  const [navOpen, storeNavOpen] = useStoredState('tankstat.nav.open', true, isBoolean)
  // What the user saved or reset (null) in this session: newer than the server's answer, which stays as it came.
  const [overrides, setOverrides] = useState<Record<string, GridSaved | null>>({})
  const navTouched = useRef(false) // changed in this session: the server's older value must not undo it
  const languageTouched = useRef(false)
  const colorModeTouched = useRef(false)
  const surfaceTouched = useRef(false)
  const { data } = useQuery(UiSettingsDocument, { skip: !enabled, fetchPolicy: 'network-only' }) // never another user's cached answer
  const send = useSerialSaves()
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
  // ... and the colour mode through MUI (which keeps the browser's copy).
  useEffect(() => {
    if (server?.colorMode && !colorModeTouched.current) setMode(FROM_SERVER[server.colorMode])
  }, [server, setMode])
  // ... and the surfaces (the class on <html> and the browser's copy).
  useEffect(() => {
    if (server?.surface && !surfaceTouched.current) applySurface(SURFACE_FROM_SERVER[server.surface])
  }, [server])

  const setNavOpen = useCallback(
    (open: boolean) => {
      navTouched.current = true
      storeNavOpen(open)
      if (enabled) send('nav', () => updateUi({ variables: { input: { navOpen: open } } }))
    },
    [enabled, storeNavOpen, updateUi, send],
  )
  const setLanguage = useCallback(
    (code: string) => {
      languageTouched.current = true
      if (enabled) send('language', () => updateUi({ variables: { input: { language: code } } }))
    },
    [enabled, updateUi, send],
  )
  const setColorMode = useCallback(
    (mode: ColorModeChoice) => {
      colorModeTouched.current = true
      if (enabled) send('colorMode', () => updateUi({ variables: { input: { colorMode: TO_SERVER[mode] } } }))
    },
    [enabled, updateUi, send],
  )
  const setSurface = useCallback(
    (surface: SurfaceChoice) => {
      surfaceTouched.current = true
      if (enabled) send('surface', () => updateUi({ variables: { input: { surface: SURFACE_TO_SERVER[surface] } } }))
    },
    [enabled, updateUi, send],
  )
  const saveGrid = useCallback(
    (gridId: string, saved: GridSaved) => {
      setOverrides((known) => ({ ...known, [gridId]: saved }))
      if (enabled) send(`grid:${gridId}`, () => saveGridOnServer({ variables: { input: { gridId, ...saved } } }))
    },
    [enabled, saveGridOnServer, send],
  )
  const resetGrid = useCallback(
    (gridId: string) => {
      setOverrides((known) => ({ ...known, [gridId]: null }))
      if (enabled) send(`grid:${gridId}`, () => resetGridOnServer({ variables: { gridId } }))
    },
    [enabled, resetGridOnServer, send],
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
    () => ({ server, navOpen, setNavOpen, setLanguage, setColorMode, setSurface, grid, saveGrid, resetGrid }),
    [server, navOpen, setNavOpen, setLanguage, setColorMode, setSurface, grid, saveGrid, resetGrid],
  )
  return <UiSettingsContext.Provider value={value}>{children}</UiSettingsContext.Provider>
}
