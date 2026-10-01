import { useState } from 'react'
import type { SortDirection, VehicleSortField } from '../gql/generated.ts'

export const PAGE_SIZES = [10, 25, 50, 100]
const DEFAULT_PAGE_SIZE = 25

export interface GridSettings {
  /** Column ids in display order. */
  order: string[]
  /** Hidden column ids (never includes a column that cannot be hidden). */
  hidden: string[]
  pageSize: number
  sortField: VehicleSortField
  sortDirection: SortDirection
}

export interface ColumnInfo {
  id: string
  hideable: boolean
  /** Shown by default on narrow (phone) screens. */
  mobile: boolean
  sortField?: VehicleSortField
}

const storageKey = (gridId: string) => `tankstat.grid.${gridId}`

function read(gridId: string): Partial<GridSettings> | undefined {
  try {
    const raw = window.localStorage.getItem(storageKey(gridId))
    return raw ? (JSON.parse(raw) as Partial<GridSettings>) : undefined
  } catch {
    return undefined // storage unavailable or corrupt: fall back to defaults
  }
}

function write(gridId: string, settings: GridSettings | undefined) {
  try {
    if (settings) window.localStorage.setItem(storageKey(gridId), JSON.stringify(settings))
    else window.localStorage.removeItem(storageKey(gridId))
  } catch {
    // not persisting is acceptable
  }
}

const isNarrow = () => window.matchMedia?.('(max-width: 767px)').matches ?? false

function defaultSettings(columns: ColumnInfo[], sort: { field: VehicleSortField; direction: SortDirection }): GridSettings {
  const narrow = isNarrow()
  return {
    order: columns.map((c) => c.id),
    hidden: columns.filter((c) => c.hideable && narrow && !c.mobile).map((c) => c.id),
    pageSize: DEFAULT_PAGE_SIZE,
    sortField: sort.field,
    sortDirection: sort.direction,
  }
}

/** Saved settings are validated against the current columns, so a renamed or removed column cannot break the grid. */
function merge(saved: Partial<GridSettings> | undefined, defaults: GridSettings, columns: ColumnInfo[]): GridSettings {
  if (!saved) return defaults
  const ids = columns.map((c) => c.id)
  const savedOrder = (saved.order ?? []).filter((id) => ids.includes(id))
  const sortable = columns.flatMap((c) => (c.sortField ? [c.sortField] : []))

  return {
    order: [...savedOrder, ...ids.filter((id) => !savedOrder.includes(id))],
    hidden: (saved.hidden ?? defaults.hidden).filter((id) => columns.some((c) => c.id === id && c.hideable)),
    pageSize: PAGE_SIZES.includes(saved.pageSize ?? 0) ? saved.pageSize! : defaults.pageSize,
    sortField: sortable.includes(saved.sortField as VehicleSortField) ? saved.sortField! : defaults.sortField,
    sortDirection: saved.sortDirection === 'DESC' || saved.sortDirection === 'ASC' ? saved.sortDirection : defaults.sortDirection,
  }
}

/** Column visibility/order, page size and sorting of one grid, remembered in the browser. */
export function useGridSettings(
  gridId: string,
  columns: ColumnInfo[],
  defaultSort: { field: VehicleSortField; direction: SortDirection },
) {
  const [settings, setSettings] = useState(() => merge(read(gridId), defaultSettings(columns, defaultSort), columns))

  const update = (patch: Partial<GridSettings>) => {
    const next = { ...settings, ...patch }
    setSettings(next)
    write(gridId, next)
  }
  const reset = () => {
    setSettings(defaultSettings(columns, defaultSort))
    write(gridId, undefined)
  }

  return { settings, update, reset }
}
