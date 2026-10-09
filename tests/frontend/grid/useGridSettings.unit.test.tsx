import { act, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { expect, it, vi } from 'vitest'
import { useGridSettings, type ColumnInfo } from '../../../src/frontend/grid/useGridSettings.ts'
import type { GridSaved } from '../../../src/frontend/settings/types.ts'
import { UiSettingsContext, type UiSettingsApi } from '../../../src/frontend/settings/uiSettingsContext.ts'

const columns: ColumnInfo[] = [
  { id: 'name', hideable: false, mobile: true, sortable: true },
  { id: 'plate', hideable: true, mobile: false, sortable: true },
  { id: 'fuel', hideable: true, mobile: false, sortable: false },
]
const sort = { column: 'name', desc: false }
const saved = (over: Partial<GridSaved> = {}): GridSaved => ({ order: ['plate', 'name', 'fuel'], hidden: ['fuel'], pageSize: 50, sortColumn: 'plate', sortDirection: 'DESC', ...over })

/** A provider as the hook sees it; `server` is only a token whose identity says "a new answer". */
const api = (over: Partial<UiSettingsApi> = {}): UiSettingsApi => ({
  server: {},
  navOpen: true,
  setNavOpen: vi.fn(),
  setLanguage: vi.fn(),
  setColorMode: vi.fn(),
  setSurface: vi.fn(),
  grid: () => undefined,
  saveGrid: vi.fn(),
  resetGrid: vi.fn(),
  ...over,
})

/** The wrapper reads `current` on every render, so a test can hand the hook a later answer with `rerender()`. */
function hookWith(current: { value: UiSettingsApi | null }) {
  const wrapper = ({ children }: { children: ReactNode }) => <UiSettingsContext.Provider value={current.value}>{children}</UiSettingsContext.Provider>
  return renderHook(() => useGridSettings('g', columns, sort), { wrapper })
}

it('without a provider the browser alone remembers, as before', () => {
  window.localStorage.setItem('tankstat.grid.g', JSON.stringify(saved()))
  const { result } = hookWith({ value: null })

  expect(result.current.state.columnOrder).toEqual(['plate', 'name', 'fuel'])
  expect(result.current.state.pagination.pageSize).toBe(50)
  act(() => result.current.reset())
  expect(window.localStorage.getItem('tankstat.grid.g')).toBeNull()
  expect(result.current.state.pagination.pageSize).toBe(25)
})

it('starts from the server copy when there is one, and tells the server about every setting (not about page changes)', () => {
  const value = api({ grid: (id) => (id === 'g' ? saved() : undefined) })
  const { result } = hookWith({ value })

  expect(result.current.state.sorting).toEqual([{ id: 'plate', desc: true }])
  act(() => result.current.update({ columnVisibility: { plate: false } }))
  expect(value.saveGrid).toHaveBeenCalledWith('g', expect.objectContaining({ hidden: ['plate'], sortColumn: 'plate', sortDirection: 'DESC' }))
  expect(JSON.parse(window.localStorage.getItem('tankstat.grid.g')!).hidden).toEqual(['plate']) // the browser keeps its copy too

  act(() => result.current.setPagination({ pageIndex: 2, pageSize: 50 }))
  expect(value.saveGrid).toHaveBeenCalledTimes(1) // turning a page is not a setting
  act(() => result.current.setPagination({ pageIndex: 0, pageSize: 10 }))
  expect(value.saveGrid).toHaveBeenCalledTimes(2)
  act(() => result.current.reset())
  expect(value.resetGrid).toHaveBeenCalledWith('g')
})

it('adopts the server copy when it arrives after the grid mounted, unless the user changed the grid meanwhile', () => {
  const current = { value: api({ server: undefined }) as UiSettingsApi | null }
  const { result, rerender } = hookWith(current)
  expect(result.current.state.pagination.pageSize).toBe(25)

  current.value = api({ grid: () => saved() })
  rerender()
  expect(result.current.state.pagination.pageSize).toBe(50)
  expect(result.current.state.columnOrder).toEqual(['plate', 'name', 'fuel'])
  expect(JSON.parse(window.localStorage.getItem('tankstat.grid.g')!).pageSize).toBe(50)

  act(() => result.current.setPagination({ pageIndex: 0, pageSize: 10 })) // the user's own change ...
  current.value = api({ grid: () => saved({ pageSize: 100 }) }) // ... must survive a later answer
  rerender()
  expect(result.current.state.pagination.pageSize).toBe(10)
})

it('an answer without a row for this grid keeps the browser copy', () => {
  window.localStorage.setItem('tankstat.grid.g', JSON.stringify(saved({ pageSize: 100 })))
  const current = { value: api({ server: undefined }) as UiSettingsApi | null }
  const { result, rerender } = hookWith(current)
  expect(result.current.state.pagination.pageSize).toBe(100)

  current.value = api() // ready, nothing for 'g'
  rerender()

  expect(result.current.state.pagination.pageSize).toBe(100)
})

it('saves a change once, also under StrictMode, and a second change builds on the first', () => {
  const value = api()
  const wrapper = ({ children }: { children: ReactNode }) => <UiSettingsContext.Provider value={value}>{children}</UiSettingsContext.Provider>
  const { result } = renderHook(() => useGridSettings('g', columns, sort), { wrapper, reactStrictMode: true })

  act(() => result.current.update({ columnVisibility: { plate: false } }))
  act(() => result.current.setPagination({ pageIndex: 0, pageSize: 10 }))

  expect(value.saveGrid).toHaveBeenCalledTimes(2) // a save inside a state updater would run twice here
  expect(result.current.state.columnVisibility).toEqual({ plate: false })
  expect(result.current.state.pagination.pageSize).toBe(10)
  expect(value.saveGrid).toHaveBeenLastCalledWith('g', expect.objectContaining({ hidden: ['plate'], pageSize: 10 }))
})

it('a column hidden by default stays hidden on a wide screen until the user shows it, and a saved choice wins', () => {
  const withDetail: ColumnInfo[] = [...columns, { id: 'detail', hideable: true, mobile: false, defaultHidden: true, sortable: false }]
  const { result } = renderHook(() => useGridSettings('d', withDetail, sort))
  expect(result.current.state.columnVisibility).toEqual({ detail: false }) // the plate and the fuel show on a desktop

  window.localStorage.setItem('tankstat.grid.e', JSON.stringify(saved({ order: ['name', 'plate', 'fuel', 'detail'], hidden: [] })))
  const shown = renderHook(() => useGridSettings('e', withDetail, sort))
  expect(shown.result.current.state.columnVisibility).toEqual({})
})

it('a column added after the settings were saved starts as its defaults say, while the saved choices stay', () => {
  const withDetail: ColumnInfo[] = [...columns, { id: 'detail', hideable: true, mobile: false, defaultHidden: true, sortable: false }]
  window.localStorage.setItem('tankstat.grid.f', JSON.stringify(saved({ order: ['name', 'plate', 'fuel'], hidden: ['fuel'] })))

  const { result } = renderHook(() => useGridSettings('f', withDetail, sort))

  expect(result.current.state.columnVisibility).toEqual({ fuel: false, detail: false })
})
