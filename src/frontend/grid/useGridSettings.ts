import { useCallback, useEffect, useRef, useState } from 'react'
import type { GridSaved } from '../settings/types.ts'
import { useUiSettings } from '../settings/uiSettingsContext.ts'

export const PAGE_SIZES = [10, 25, 50, 100]
const DEFAULT_PAGE_SIZE = 25

export interface ColumnInfo {
  id: string
  hideable: boolean
  /** Shown by default on narrow (phone) screens. */
  mobile: boolean
  /** Hidden until the user shows it (on every screen). */
  defaultHidden?: boolean
  sortable: boolean
}

export interface PaginationState {
  /** Zero-based. */
  pageIndex: number
  pageSize: number
}

export interface GridState {
  /** Every column's id, in the order they are shown. */
  columnOrder: string[]
  /** Hidden columns map to false (MUI's column visibility model); a column not named is shown. */
  columnVisibility: Record<string, boolean>
  /** One entry: the column the server sorts by. */
  sorting: { id: string; desc: boolean }[]
  pagination: PaginationState
}

const storageKey = (gridId: string) => `tankstat.grid.${gridId}`

function read(gridId: string): Partial<GridSaved> | undefined {
  try {
    const raw = window.localStorage.getItem(storageKey(gridId))
    const parsed: unknown = raw ? JSON.parse(raw) : undefined
    return parsed && typeof parsed === 'object' ? (parsed as Partial<GridSaved>) : undefined
  } catch {
    return undefined // storage unavailable or corrupt: fall back to defaults
  }
}

function write(gridId: string, saved: GridSaved | undefined) {
  try {
    if (saved) window.localStorage.setItem(storageKey(gridId), JSON.stringify(saved))
    else window.localStorage.removeItem(storageKey(gridId))
  } catch {
    // not persisting is acceptable
  }
}

const isNarrow = () => window.matchMedia?.('(max-width: 767px)').matches ?? false

function defaults(columns: ColumnInfo[], sort: { column: string; desc: boolean }): GridState {
  const narrow = isNarrow()
  return {
    columnOrder: columns.map((c) => c.id),
    columnVisibility: Object.fromEntries(columns.filter((c) => c.hideable && (c.defaultHidden || (narrow && !c.mobile))).map((c) => [c.id, false])),
    sorting: [{ id: sort.column, desc: sort.desc }],
    pagination: { pageIndex: 0, pageSize: DEFAULT_PAGE_SIZE },
  }
}

/** Saved settings are validated against the current columns, so a renamed or removed column cannot break the grid. */
function merge(saved: Partial<GridSaved> | undefined, base: GridState, columns: ColumnInfo[]): GridState {
  if (!saved) return base
  const ids = columns.map((c) => c.id)
  const savedOrder = (Array.isArray(saved.order) ? saved.order : []).filter((id) => ids.includes(id))
  const hidden = Array.isArray(saved.hidden) ? saved.hidden.filter((id) => columns.some((c) => c.id === id && c.hideable)) : undefined
  const sortable = columns.filter((c) => c.sortable).map((c) => c.id)
  const sortColumn = sortable.includes(saved.sortColumn ?? '') ? saved.sortColumn! : base.sorting[0].id
  const desc = saved.sortDirection === 'DESC' || saved.sortDirection === 'ASC' ? saved.sortDirection === 'DESC' : base.sorting[0].desc
  const sortChanged = sortColumn === saved.sortColumn

  // A column the saved settings do not list is newer than them: it starts as the defaults say (hidden by default, or on a phone).
  const added = Array.isArray(saved.order) ? ids.filter((id) => !saved.order!.includes(id) && base.columnVisibility[id] === false) : []
  return {
    columnOrder: [...savedOrder, ...ids.filter((id) => !savedOrder.includes(id))],
    columnVisibility: hidden ? Object.fromEntries([...hidden, ...added].map((id) => [id, false])) : base.columnVisibility,
    sorting: [{ id: sortColumn, desc: sortChanged ? desc : base.sorting[0].desc }],
    pagination: { pageIndex: 0, pageSize: PAGE_SIZES.includes(saved.pageSize ?? 0) ? saved.pageSize! : DEFAULT_PAGE_SIZE },
  }
}

/**
 * Column visibility and order, page size and sorting of one grid. Remembered in the browser and, through
 * `UiSettingsProvider`, with the account: the server's copy is taken when it is there, and adopted once more when it arrives after the grid
 * mounted, unless the user changed the grid meanwhile. Without a provider (a grid on its own) the browser alone remembers.
 */
export function useGridSettings(gridId: string, columns: ColumnInfo[], defaultSort: { column: string; desc: boolean }) {
  const settings = useUiSettings()
  const [state, setState] = useState<GridState>(() => merge(settings?.grid(gridId) ?? read(gridId), defaults(columns, defaultSort), columns))
  const latest = useRef(state) // what the next change builds on, also when two changes come before a render
  const touched = useRef(false) // the user changed this grid in this session: a later answer from the server must not undo it
  const applied = useRef(settings?.server) // the answer the state was built from

  const commit = useCallback((next: GridState) => {
    latest.current = next
    setState(next)
  }, [])

  useEffect(() => {
    if (!settings?.server || applied.current === settings.server) return
    applied.current = settings.server
    const server = settings.grid(gridId)
    if (!server || touched.current) return // nothing saved on the server keeps the browser's copy
    commit(merge(server, defaults(columns, defaultSort), columns))
    write(gridId, server)
  }, [settings, gridId, columns, defaultSort, commit])

  const persist = useCallback(
    (next: GridState) => {
      touched.current = true
      const saved: GridSaved = {
        order: next.columnOrder,
        hidden: Object.entries(next.columnVisibility).filter(([, visible]) => visible === false).map(([id]) => id),
        pageSize: next.pagination.pageSize,
        sortColumn: next.sorting[0]?.id ?? defaultSort.column,
        sortDirection: next.sorting[0]?.desc ? 'DESC' : 'ASC',
      }
      write(gridId, saved)
      settings?.saveGrid(gridId, saved)
    },
    [gridId, defaultSort.column, settings],
  )

  // Never inside a state updater: saving is a side effect, and React runs updaters twice in StrictMode.
  const update = useCallback(
    (patch: Partial<GridState>) => {
      const next = { ...latest.current, ...patch }
      commit(next)
      persist(next)
    },
    [commit, persist],
  )

  /** Page changes are never stored; only the choices that describe how the user wants the grid. */
  const setPagination = useCallback(
    (pagination: PaginationState) => {
      const previous = latest.current
      const next = { ...previous, pagination }
      commit(next)
      if (pagination.pageSize !== previous.pagination.pageSize) persist(next)
    },
    [commit, persist],
  )

  const reset = useCallback(() => {
    touched.current = true
    write(gridId, undefined)
    settings?.resetGrid(gridId)
    commit(defaults(columns, defaultSort))
  }, [gridId, columns, defaultSort, settings, commit])

  return { state, update, setPagination, reset }
}
