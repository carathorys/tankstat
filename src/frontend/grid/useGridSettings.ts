import type { ColumnOrderState, PaginationState, SortingState, ColumnVisibilityState } from '@tanstack/react-table'
import { useCallback, useState } from 'react'

export const PAGE_SIZES = [10, 25, 50, 100]
const DEFAULT_PAGE_SIZE = 25

export interface ColumnInfo {
  id: string
  hideable: boolean
  /** Shown by default on narrow (phone) screens. */
  mobile: boolean
  sortable: boolean
}

/** What the browser remembers about one grid (nothing else is stored anywhere). */
interface Saved {
  order: string[]
  hidden: string[]
  pageSize: number
  sortColumn: string
  sortDirection: 'ASC' | 'DESC'
}

export interface GridState {
  columnOrder: ColumnOrderState
  columnVisibility: ColumnVisibilityState
  sorting: SortingState
  pagination: PaginationState
}

const storageKey = (gridId: string) => `tankstat.grid.${gridId}`

function read(gridId: string): Partial<Saved> | undefined {
  try {
    const raw = window.localStorage.getItem(storageKey(gridId))
    const parsed: unknown = raw ? JSON.parse(raw) : undefined
    return parsed && typeof parsed === 'object' ? (parsed as Partial<Saved>) : undefined
  } catch {
    return undefined // storage unavailable or corrupt: fall back to defaults
  }
}

function write(gridId: string, saved: Saved | undefined) {
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
function merge(saved: Partial<Saved> | undefined, base: GridState, columns: ColumnInfo[]): GridState {
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

/** Column visibility and order, page size and sorting of one grid, in the shape TanStack Table wants, remembered in the browser. */
export function useGridSettings(gridId: string, columns: ColumnInfo[], defaultSort: { column: string; desc: boolean }) {
  const [state, setState] = useState<GridState>(() => merge(read(gridId), defaults(columns, defaultSort), columns))

  const persist = useCallback(
    (next: GridState) => {
      write(gridId, {
        order: next.columnOrder,
        hidden: Object.entries(next.columnVisibility).filter(([, visible]) => visible === false).map(([id]) => id),
        pageSize: next.pagination.pageSize,
        sortColumn: next.sorting[0]?.id ?? defaultSort.column,
        sortDirection: next.sorting[0]?.desc ? 'DESC' : 'ASC',
      })
    },
    [gridId, defaultSort.column],
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
    write(gridId, undefined)
    setState(defaults(columns, defaultSort))
  }, [gridId, columns, defaultSort])

  return { state, update, setPagination, reset }
}
