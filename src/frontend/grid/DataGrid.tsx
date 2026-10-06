import type { OperationVariables } from '@apollo/client'
import { useQuery } from '@apollo/client/react'
import type { TypedDocumentNode } from '@graphql-typed-document-node/core'
import {
  columnOrderingFeature,
  columnVisibilityFeature,
  rowPaginationFeature,
  rowSortingFeature,
  tableFeatures,
  useTable,
  type ColumnDef,
  type Updater,
} from '@tanstack/react-table'
import { Box, Button, Flex, IconButton, Table, Text, VisuallyHidden } from '@radix-ui/themes'
import type { ParseKeys } from 'i18next'
import { ChevronDown, ChevronUp, ChevronsUpDown, RefreshCw } from 'lucide-react'
import { motion } from 'motion/react'
import { useEffect, useMemo, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '../messages.tsx'
import { ColumnsPopover } from './ColumnsPopover.tsx'
import { Pager } from './Pager.tsx'
import { useGridSettings } from './useGridSettings.ts'

export type SortDirection = 'ASC' | 'DESC'

export interface GridColumn<Row, TVars, TSort extends string> {
  id: string
  /** Translation key of the header, e.g. "columns.name". */
  label: ParseKeys
  /** Columns that identify the row must stay visible. Defaults to true (hideable). */
  hideable?: boolean
  /** Shown by default on phones. */
  mobile?: boolean
  /** Server-side sort key; columns without one are not sortable. */
  sortField?: TSort
  /** Name of the query's Boolean variable that switches this column's GraphQL fields on (@include). Omit for always-on fields. */
  include?: keyof TVars & string
  cell: (row: Row) => ReactNode
}

// The grid state (sorting, paging, columns) lives in TanStack Table; the data itself comes sorted and paged from the server.
const features = tableFeatures({ columnVisibilityFeature, columnOrderingFeature, rowSortingFeature, rowPaginationFeature })
type Features = typeof features

const resolve = <T,>(updater: Updater<T>, previous: T): T => (typeof updater === 'function' ? (updater as (old: T) => T)(previous) : updater)
const MotionRow = motion.create(Table.Row)
const NO_ROWS: never[] = []
/** How often a grid that waits for something on the server (see `pollWhile`) asks again. */
export const GRID_POLL_MS = 10_000

/**
 * A grid whose sorting, paging and column selection are all done by the GraphQL server: the query gets the sort order, the page
 * and one Boolean variable per optional column. TanStack Table coordinates the grid state (manual sorting and pagination); the
 * column choices, order and page size are kept with the account (`UiSettingsProvider`) and in the browser (`useGridSettings`). Data is
 * re-fetched every time the grid is shown and on demand.
 *
 * `columns` must be memoized (stable between renders).
 */
export function DataGrid<TData extends object, TVars extends OperationVariables, Row extends Record<string, unknown>, TSort extends string>({
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
}: {
  gridId: string
  /** Accessible name of the table (read by screen readers, not shown). */
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
}) {
  const { t } = useTranslation()
  const info = useMemo(() => columns.map((c) => ({ id: c.id, hideable: c.hideable ?? true, mobile: c.mobile ?? false, sortable: c.sortField !== undefined })), [columns])
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

  // ---- the table ----
  const columnDefs = useMemo<ColumnDef<Features, Row>[]>(
    () =>
      columns.map((c) => ({
        id: c.id,
        // TanStack only allows sorting columns that have an accessor; the order itself is the server's (manualSorting).
        accessorFn: (row) => row,
        header: () => t(c.label),
        cell: ({ row }) => c.cell(row.original),
        enableSorting: c.sortField !== undefined,
        enableHiding: c.hideable ?? true,
      })),
    [columns, t],
  )

  const table = useTable({
    features,
    columns: columnDefs,
    data: rows,
    getRowId: (row) => rowKey(row),
    manualSorting: true,
    manualPagination: true,
    rowCount: total,
    enableSortingRemoval: false,
    enableMultiSort: false,
    sortDescFirst: false,
    state: { sorting: state.sorting, columnVisibility: state.columnVisibility, columnOrder: state.columnOrder, pagination: state.pagination },
    onSortingChange: (u) => update({ sorting: resolve(u, state.sorting), pagination: { ...state.pagination, pageIndex: 0 } }),
    onColumnVisibilityChange: (u) => update({ columnVisibility: resolve(u, state.columnVisibility) }),
    onColumnOrderChange: (u) => update({ columnOrder: resolve(u, state.columnOrder) }),
    onPaginationChange: (u) => setPagination(resolve(u, state.pagination)),
  })

  // After deleting the last row of the last page, step back instead of showing an empty page.
  const lastPage = Math.max(0, Math.ceil(total / state.pagination.pageSize) - 1)
  if (total > 0 && state.pagination.pageIndex > lastPage) setPagination({ ...state.pagination, pageIndex: lastPage })

  const headers = table.getHeaderGroups()[0]?.headers ?? []

  return (
    <>
      <Flex justify="between" align="center" gap="3" wrap="wrap" mb="3">
        <Box>{toolbar?.({ total, data: shown })}</Box>
        <Flex gap="2" align="center">
          <IconButton
            variant="soft"
            color="gray"
            size={{ initial: '3', md: '2' }}
            disabled={loading}
            aria-label={t('grid.refresh')}
            aria-busy={loading}
            onClick={() => void refetch()}
          >
            <RefreshCw size={18} aria-hidden />
          </IconButton>
          <ColumnsPopover
            columns={table.getAllLeafColumns().map((c) => ({
              id: c.id,
              label: columns.find((x) => x.id === c.id)!.label,
              hideable: c.getCanHide(),
              visible: c.getIsVisible(),
              toggle: (visible: boolean) => c.toggleVisibility(visible),
            }))}
            onMove={(id, offset) => {
              const order = table.getAllLeafColumns().map((c) => c.id)
              const from = order.indexOf(id)
              const to = from + offset
              if (to < 0 || to >= order.length) return
              ;[order[from], order[to]] = [order[to], order[from]]
              table.setColumnOrder(order)
            }}
            onReset={reset}
          />
        </Flex>
      </Flex>

      {error && <ErrorMessage error={error} />}
      {shown && rows.length === 0 && !loading && <Text as="p">{emptyText}</Text>}
      {rows.length > 0 && (
        <Box style={{ overflowX: 'auto', borderRadius: 'var(--radius-3)', boxShadow: 'var(--shadow-3)' }}>
          <Table.Root variant="surface" aria-busy={loading}>
            <VisuallyHidden asChild>
              <caption>{caption}</caption>
            </VisuallyHidden>
            <Table.Header>
              <Table.Row>
                {headers.map((header) => {
                  const column = columns.find((c) => c.id === header.column.id)!
                  const sorted = header.column.getIsSorted()
                  const label = t(column.label)
                  return (
                    <Table.ColumnHeaderCell
                      key={header.id}
                      scope="col"
                      aria-sort={column.sortField ? (sorted === 'asc' ? 'ascending' : sorted === 'desc' ? 'descending' : 'none') : undefined}
                    >
                      {column.sortField ? (
                        <Button
                          variant="ghost"
                          color="gray"
                          highContrast
                          size={{ initial: '3', md: '2' }}
                          aria-label={t('a11y.sortBy', { column: label })}
                          onClick={header.column.getToggleSortingHandler()}
                        >
                          {label}
                          {!sorted && <ChevronsUpDown size={14} aria-hidden />}
                          {sorted === 'asc' && <ChevronUp size={14} aria-label={t('grid.sortedAscending')} />}
                          {sorted === 'desc' && <ChevronDown size={14} aria-label={t('grid.sortedDescending')} />}
                        </Button>
                      ) : (
                        label
                      )}
                    </Table.ColumnHeaderCell>
                  )
                })}
                {actions && (
                  <Table.ColumnHeaderCell scope="col">
                    <VisuallyHidden>{t('grid.actions')}</VisuallyHidden>
                  </Table.ColumnHeaderCell>
                )}
              </Table.Row>
            </Table.Header>
            <Table.Body>
              {table.getRowModel().rows.map((row) => (
                <MotionRow key={row.id} align="center" initial={{ opacity: 0 }} animate={{ opacity: 1 }} transition={{ duration: 0.15 }}>
                  {row.getVisibleCells().map((cell, i) =>
                    i === 0 ? (
                      <Table.RowHeaderCell key={cell.id}>
                        <table.FlexRender cell={cell} />
                      </Table.RowHeaderCell>
                    ) : (
                      <Table.Cell key={cell.id}>
                        <table.FlexRender cell={cell} />
                      </Table.Cell>
                    ),
                  )}
                  {actions && <Table.Cell justify="end">{actions(row.original)}</Table.Cell>}
                </MotionRow>
              ))}
            </Table.Body>
          </Table.Root>
        </Box>
      )}
      <Pager
        page={state.pagination.pageIndex}
        pageSize={state.pagination.pageSize}
        total={total}
        onPage={(pageIndex) => setPagination({ ...state.pagination, pageIndex })}
        onPageSize={(pageSize) => setPagination({ pageIndex: 0, pageSize })}
      />
    </>
  )
}
