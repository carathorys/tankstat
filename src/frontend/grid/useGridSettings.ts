import type { ColumnOrderState, PaginationState, SortingState, ColumnVisibilityState } from '@tanstack/react-table'
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
  sortable: boolean
}

export interface GridState {
  columnOrder: ColumnOrderState
  columnVisibility: ColumnVisibilityState
  sorting: SortingState
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
    columnVisibility: Object.fromEntries(columns.filter((c) => c.hideable && narrow && !c.mobile).map((c) => [c.id, false])),
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

  return {
    columnOrder: [...savedOrder, ...ids.filter((id) => !savedOrder.includes(id))],
    columnVisibility: hidden ? Object.fromEntries(hidden.map((id) => [id, false])) : base.columnVisibility,
    sorting: [{ id: sortColumn, desc: sortChanged ? desc : base.sorting[0].desc }],
    pagination: { pageIndex: 0, pageSize: PAGE_SIZES.includes(saved.pageSize ?? 0) ? saved.pageSize! : DEFAULT_PAGE_SIZE },
  }
}

/**
 * Column visibility and order, page size and sorting of one grid, in the shape TanStack Table wants. Remembered in the browser and, through
 * `UiSettingsProvider`, with the account: the server's copy is taken when it is there, and adopted once more when it arrives after the grid
 * mounted, unless the user changed the grid meanwhile. Without a provider (a grid on its own) the browser alone remembers.
 */
export function useGridSettings(gridId: string, columns: ColumnInfo[], defaultSort: { column: string; desc: boolean }) {
  const settings = useUiSettings()
  const [state, setState] = useState<GridState>(() => merge(settings?.grid(gridId) ?? read(gridId), defaults(columns, defaultSort), columns))
  const touched = useRef(false) // the user changed this grid in this session: a later answer from the server must not undo it
  const applied = useRef(settings?.server) // the answer the state was built from

  useEffect(() => {
    if (!settings?.server || applied.current === settings.server) return
    applied.current = settings.server
    const server = settings.grid(gridId)
    if (!server || touched.current) return // nothing saved on the server keeps the browser's copy
    setState(merge(server, defaults(columns, defaultSort), columns))
    write(gridId, server)
  }, [settings, gridId, columns, defaultSort])

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

  const update = useCallback(
    (patch: Partial<GridState>) =>
      setState((prev) => {
        const next = { ...prev, ...patch }
        persist(next)
        return next
      }),
    [persist],
  )

  /** Page changes are never stored; only the choices that describe how the user wants the grid. */
  const setPagination = useCallback(
    (pagination: PaginationState) =>
      setState((prev) => {
        const next = { ...prev, pagination }
        if (pagination.pageSize !== prev.pagination.pageSize) persist(next)
        return next
      }),
    [persist],
  )

  const reset = useCallback(() => {
    touched.current = true
    write(gridId, undefined)
    settings?.resetGrid(gridId)
    setState(defaults(columns, defaultSort))
  }, [gridId, columns, defaultSort, settings])

  return { state, update, setPagination, reset }
}
