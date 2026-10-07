import type { OperationVariables } from '@apollo/client'
import { useQuery } from '@apollo/client/react'
import type { TypedDocumentNode } from '@graphql-typed-document-node/core'
import Box from '@mui/material/Box'
import Stack from '@mui/material/Stack'
import { DataGrid, type GridColDef, type GridLocaleText, type GridPaginationModel, type GridSortModel } from '@mui/x-data-grid'
import { huHU } from '@mui/x-data-grid/locales'
import type { ParseKeys } from 'i18next'
import { ChevronDown, ChevronsUpDown, ChevronUp, RefreshCw } from 'lucide-react'
import { useEffect, useMemo, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { IconAction } from '../components/IconAction.tsx'
import { visuallyHidden } from '../components/visuallyHidden.ts'
import { ErrorMessage } from '../messages.tsx'
import { ColumnsPopover } from './ColumnsPopover.tsx'
import { PAGE_SIZES, useGridSettings, type PaginationState } from './useGridSettings.ts'

export type SortDirection = 'ASC' | 'DESC'

export interface GridColumn<Row, TVars, TSort extends string> {
  id: string
  /** Translation key of the header, e.g. "columns.name". */
  label: ParseKeys
  /** Columns that identify the row must stay visible. Defaults to true (hideable). */
  hideable?: boolean
  /** Shown by default on phones. */
  mobile?: boolean
  /** Hidden until the user shows it, on every screen (a detail few need). */
  defaultHidden?: boolean
  /** Server-side sort key; columns without one are not sortable. */
  sortField?: TSort
  /** Name of the query's Boolean variable that switches this column's GraphQL fields on (@include). Omit for always-on fields. */
  include?: keyof TVars & string
  cell: (row: Row) => ReactNode
}

const NO_ROWS: never[] = []
/** The sort marks the grids have always had. */
const SORT_ICONS = {
  columnSortedAscendingIcon: () => <ChevronUp size={14} aria-hidden />,
  columnSortedDescendingIcon: () => <ChevronDown size={14} aria-hidden />,
  columnUnsortedIcon: () => <ChevronsUpDown size={14} aria-hidden />,
}
const ACTIONS = '__actions'
/** How often a grid that waits for something on the server (see `pollWhile`) asks again. */
export const GRID_POLL_MS = 10_000

/**
 * A grid whose sorting, paging and column selection are all done by the GraphQL server: the query gets the sort order, the page and one
 * Boolean variable per optional column. MUI X's Data Grid draws it in server mode (it never sorts or pages by itself); the column choices,
 * order and page size are kept with the account (`UiSettingsProvider`) and in the browser (`useGridSettings`). Data is asked afresh every
 * time the grid is shown and on demand. The first visible column names its row (row header); rows in `leaving` (a confirmed trash) fade
 * while the server removes them.
 *
 * `columns` must be memoized (stable between renders). Every model handed to the Data Grid keeps its identity until it changes: a new sort
 * model, even an equal one, sends it back to the first page.
 */
export function ServerGrid<TData extends object, TVars extends OperationVariables, Row extends Record<string, unknown>, TSort extends string>({
  gridId,
  caption,
  query,
  variables: fixedVariables,
  select,
  rowKey,
  columns,
  defaultSort,
  actions,
  toolbar,
  emptyText,
  pollWhile,
  leaving,
}: {
  gridId: string
  /** Accessible name of the grid (read by screen readers, not shown). */
  caption: string
  query: TypedDocumentNode<TData, TVars>
  /** Variables that do not depend on the grid (e.g. the vehicle whose logs are listed). */
  variables?: Partial<TVars>
  select: (data: TData) => { rows: Row[]; total: number }
  rowKey: (row: Row) => string
  columns: GridColumn<Row, TVars, TSort>[]
  defaultSort: { column: string; direction: SortDirection }
  actions?: (row: Row) => ReactNode
  /** `data` is the whole query result, for extra figures such as how many rows may be deleted. */
  toolbar?: (context: { total: number; data: TData | undefined }) => ReactNode
  emptyText: string
  /** The rows wait for something the server does in the background (a photo being read): the grid asks again until they no longer do. */
  pollWhile?: (rows: Row[]) => boolean
  /** Rows on their way out, by key: they fade while the change that removes them reaches the server. */
  leaving?: ReadonlySet<string>
}) {
  const { t, i18n } = useTranslation()
  const info = useMemo(() => columns.map((c) => ({ id: c.id, hideable: c.hideable ?? true, mobile: c.mobile ?? false, defaultHidden: c.defaultHidden ?? false, sortable: c.sortField !== undefined })), [columns])
  const sortDefault = useMemo(() => ({ column: defaultSort.column, desc: defaultSort.direction === 'DESC' }), [defaultSort.column, defaultSort.direction])
  const { state, update, setPagination, reset } = useGridSettings(gridId, info, sortDefault)

  // ---- what to ask the server for ----
  const sortColumn = columns.find((c) => c.id === state.sorting[0]?.id) ?? columns.find((c) => c.id === defaultSort.column)!
  const includes = Object.fromEntries(columns.filter((c) => c.include).map((c) => [c.include!, state.columnVisibility[c.id] !== false]))
  const variables = {
    ...fixedVariables,
    orderBy: sortColumn.sortField,
    direction: state.sorting[0]?.desc ? 'DESC' : 'ASC',
    skip: state.pagination.pageIndex * state.pagination.pageSize,
    take: state.pagination.pageSize,
    ...includes,
  } as unknown as TVars

  const { data, previousData, loading, error, refetch, startPolling, stopPolling } = useQuery(query, {
    variables,
    fetchPolicy: 'cache-and-network',
    notifyOnNetworkStatusChange: true,
  })
  const shown = (data ?? previousData) as TData | undefined
  const { rows, total } = shown ? select(shown) : { rows: NO_ROWS as Row[], total: 0 }
  const polling = pollWhile?.(rows) ?? false
  useEffect(() => {
    if (!polling) return
    startPolling(GRID_POLL_MS)
    return () => stopPolling()
  }, [polling, startPolling, stopPolling])

  // After deleting the last row of the last page, step back instead of showing an empty page.
  const lastPage = Math.max(0, Math.ceil(total / state.pagination.pageSize) - 1)
  if (total > 0 && state.pagination.pageIndex > lastPage) setPagination({ ...state.pagination, pageIndex: lastPage })

  // ---- the Data Grid's models: each keeps its identity until the state behind it changes ----
  const firstVisible = state.columnOrder.find((id) => state.columnVisibility[id] !== false)
  const columnDefs: GridColDef<Row>[] = state.columnOrder.flatMap((id) => {
    const c = columns.find((x) => x.id === id)
    if (!c) return []
    return [{ field: c.id, headerName: t(c.label), sortable: c.sortField !== undefined, rowHeader: c.id === firstVisible, flex: 1, minWidth: 72, renderCell: ({ row }) => c.cell(row) }]
  })
  if (actions) {
    columnDefs.push({
      field: ACTIONS,
      headerName: t('grid.actions'),
      renderHeader: () => <span style={visuallyHidden}>{t('grid.actions')}</span>,
      sortable: false,
      width: 112,
      align: 'right',
      headerAlign: 'right',
      renderCell: ({ row }) => actions(row),
    })
  }
  const sortModel = useMemo<GridSortModel>(() => state.sorting.map((s) => ({ field: s.id, sort: s.desc ? 'desc' : 'asc' })), [state.sorting])
  const paginationModel = useMemo<GridPaginationModel>(() => ({ page: state.pagination.pageIndex, pageSize: state.pagination.pageSize }), [state.pagination])
  const localeText = useMemo<Partial<GridLocaleText>>(() => {
    const number = new Intl.NumberFormat(i18n.language)
    const page = { first: t('grid.first'), last: t('grid.last'), previous: t('grid.previous'), next: t('grid.next') }
    return {
      ...(i18n.resolvedLanguage === 'hu' ? huHU.components.MuiDataGrid.defaultProps.localeText : {}),
      noRowsLabel: emptyText,
      columnHeaderSortIconLabel: t('grid.sort'),
      paginationRowsPerPage: t('grid.pageSize'),
      paginationDisplayedRows: ({ from, to, count }) => t('grid.range', { from: number.format(from), to: number.format(to), total: number.format(count) }),
      paginationItemAriaLabel: (type) => page[type],
    }
  }, [t, i18n.language, i18n.resolvedLanguage, emptyText])

  const choices = state.columnOrder.flatMap((id) => {
    const c = columns.find((x) => x.id === id)
    if (!c) return []
    const visible = state.columnVisibility[id] !== false
    return [{ id, label: c.label, hideable: c.hideable ?? true, visible, toggle: (show: boolean) => update({ columnVisibility: { ...state.columnVisibility, [id]: show } }) }]
  })

  return (
    <>
      <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', gap: 1.5, flexWrap: 'wrap', mb: 1.5 }}>
        <Box>{toolbar?.({ total, data: shown })}</Box>
        <Stack direction="row" sx={{ gap: 1, alignItems: 'center' }}>
          <IconAction label={t('grid.refresh')} disabled={loading} aria-busy={loading} onClick={() => void refetch()}>
            <RefreshCw size={18} aria-hidden className={loading ? 'tk-spin' : undefined} />
          </IconAction>
          <ColumnsPopover
            columns={choices}
            onMove={(id, offset) => {
              const order = [...state.columnOrder]
              const from = order.indexOf(id)
              const to = from + offset
              if (to < 0 || to >= order.length) return
              ;[order[from], order[to]] = [order[to], order[from]]
              update({ columnOrder: order })
            }}
            onReset={reset}
          />
        </Stack>
      </Stack>

      {error && <ErrorMessage error={error} />}
      <DataGrid<Row>
        aria-label={caption}
        rows={rows}
        getRowId={rowKey}
        columns={columnDefs}
        columnVisibilityModel={state.columnVisibility}
        sortingMode="server"
        sortingOrder={['asc', 'desc']}
        sortModel={sortModel}
        onSortModelChange={(model) => {
          const first = model[0]
          if (first?.sort) update({ sorting: [{ id: first.field, desc: first.sort === 'desc' }], pagination: { ...state.pagination, pageIndex: 0 } })
        }}
        paginationMode="server"
        rowCount={total}
        pageSizeOptions={PAGE_SIZES}
        paginationModel={paginationModel}
        onPaginationModelChange={(model) => setPagination(nextPage(state.pagination, model))}
        loading={loading}
        slots={SORT_ICONS}
        slotProps={{ loadingOverlay: { variant: 'linear-progress', noRowsVariant: 'skeleton' } }}
        localeText={localeText}
        getRowClassName={({ id }) => (leaving?.has(String(id)) ? 'tk-leaving' : '')}
        autoHeight
        disableVirtualization
        getRowHeight={() => 'auto'}
        columnHeaderHeight={44}
        disableColumnMenu
        disableColumnFilter
        disableColumnSelector
        disableColumnResize
        disableRowSelectionOnClick
        hideFooterSelectedRowCount
      />
    </>
  )
}

/** A new page size starts again from the first page; a page change keeps the size. */
const nextPage = (current: PaginationState, model: GridPaginationModel): PaginationState =>
  model.pageSize !== current.pageSize ? { pageIndex: 0, pageSize: model.pageSize } : { pageIndex: model.page, pageSize: model.pageSize }
