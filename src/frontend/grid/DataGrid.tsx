import type { OperationVariables } from '@apollo/client'
import { useQuery } from '@apollo/client/react'
import { Box, Button, Flex, IconButton, Table, Text } from '@radix-ui/themes'
import type { TypedDocumentNode } from '@graphql-typed-document-node/core'
import type { ParseKeys } from 'i18next'
import { ChevronDown, ChevronUp, ChevronsUpDown, RefreshCw } from 'lucide-react'
import { motion } from 'motion/react'
import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { SortDirection, VehicleSortField } from '../gql/generated.ts'
import { ErrorMessage } from '../messages.tsx'
import { ColumnsPopover } from './ColumnsPopover.tsx'
import { Pager } from './Pager.tsx'
import { useGridSettings } from './useGridSettings.ts'

export interface GridColumn<Row, TVars> {
  id: string
  /** Translation key of the header, e.g. "columns.name". */
  label: ParseKeys
  /** The one column that must always be present (it identifies the row). Defaults to true. */
  hideable?: boolean
  /** Shown by default on phones. */
  mobile?: boolean
  /** Server-side sort key; columns without one are not sortable. */
  sortField?: VehicleSortField
  /** Name of the query's Boolean variable that switches this column's GraphQL fields on (@include). Omit for always-on fields. */
  include?: keyof TVars & string
  cell: (row: Row) => ReactNode
}

const MotionRow = motion.create(Table.Row)

/**
 * A grid whose sorting, paging and column selection are all done by the GraphQL server: the query gets the sort
 * order, page and one Boolean variable per optional column. Column choices and page size are kept in the browser.
 * Data is re-fetched every time the grid is shown and on demand with the refresh button.
 */
export function DataGrid<TData extends object, TVars extends OperationVariables, Row>({
  gridId,
  query,
  select,
  rowKey,
  columns,
  defaultSort,
  actions,
  toolbar,
  emptyText,
}: {
  gridId: string
  query: TypedDocumentNode<TData, TVars>
  select: (data: TData) => { rows: Row[]; total: number }
  rowKey: (row: Row) => string
  columns: GridColumn<Row, TVars>[]
  defaultSort: { field: VehicleSortField; direction: SortDirection }
  actions?: (row: Row) => ReactNode
  toolbar?: (context: { total: number }) => ReactNode
  emptyText: string
}) {
  const { t } = useTranslation()
  const info = columns.map((c) => ({ id: c.id, hideable: c.hideable ?? true, mobile: c.mobile ?? false, sortField: c.sortField }))
  const { settings, update, reset } = useGridSettings(gridId, info, defaultSort)
  const [page, setPage] = useState(0)

  const ordered = settings.order.map((id) => columns.find((c) => c.id === id)!).filter(Boolean)
  const visible = ordered.filter((c) => !settings.hidden.includes(c.id))

  const includes = Object.fromEntries(columns.filter((c) => c.include).map((c) => [c.include!, !settings.hidden.includes(c.id)]))
  const variables = {
    orderBy: settings.sortField,
    direction: settings.sortDirection,
    skip: page * settings.pageSize,
    take: settings.pageSize,
    ...includes,
  } as unknown as TVars

  const { data, previousData, loading, error, refetch } = useQuery(query, {
    variables,
    fetchPolicy: 'cache-and-network',
    notifyOnNetworkStatusChange: true,
  })
  const shown = (data ?? previousData) as TData | undefined
  const { rows, total } = shown ? select(shown) : { rows: [], total: 0 }

  // After deleting the last row of the last page, step back instead of showing an empty page.
  if (total > 0 && page * settings.pageSize >= total) setPage(Math.max(0, Math.ceil(total / settings.pageSize) - 1))

  function sortBy(field: VehicleSortField) {
    const direction: SortDirection = settings.sortField === field && settings.sortDirection === 'ASC' ? 'DESC' : 'ASC'
    update({ sortField: field, sortDirection: direction })
    setPage(0)
  }

  function move(id: string, offset: -1 | 1) {
    const order = [...settings.order]
    const from = order.indexOf(id)
    const to = from + offset
    if (to < 0 || to >= order.length) return
    ;[order[from], order[to]] = [order[to], order[from]]
    update({ order })
  }

  return (
    <>
      <Flex justify="between" align="center" gap="3" wrap="wrap" mb="3">
        <Box>{toolbar?.({ total })}</Box>
        <Flex gap="2" align="center">
          <IconButton variant="soft" color="gray" disabled={loading} aria-label={t('grid.refresh')} aria-busy={loading} onClick={() => void refetch()}>
            <RefreshCw size={18} />
          </IconButton>
          <ColumnsPopover
            columns={ordered.map((c) => ({ id: c.id, label: c.label, hideable: c.hideable ?? true }))}
            hidden={settings.hidden}
            onToggle={(id, show) =>
              update({ hidden: show ? settings.hidden.filter((h) => h !== id) : [...settings.hidden, id] })
            }
            onMove={move}
            onReset={() => {
              reset()
              setPage(0)
            }}
          />
        </Flex>
      </Flex>

      {error && <ErrorMessage error={error} />}
      {shown && rows.length === 0 && !loading && <Text as="p">{emptyText}</Text>}
      {rows.length > 0 && (
        <Box style={{ overflowX: 'auto' }}>
          <Table.Root variant="surface">
            <Table.Header>
              <Table.Row>
                {visible.map((c) => {
                  const sorted = c.sortField !== undefined && c.sortField === settings.sortField
                  const ariaSort = sorted ? (settings.sortDirection === 'ASC' ? 'ascending' : 'descending') : undefined
                  return (
                    <Table.ColumnHeaderCell key={c.id} aria-sort={c.sortField ? (ariaSort ?? 'none') : undefined}>
                      {c.sortField ? (
                        <Button variant="ghost" color="gray" highContrast onClick={() => sortBy(c.sortField!)}>
                          {t(c.label)}
                          {!sorted && <ChevronsUpDown size={14} aria-hidden />}
                          {sorted && settings.sortDirection === 'ASC' && <ChevronUp size={14} aria-label={t('grid.sortedAscending')} />}
                          {sorted && settings.sortDirection === 'DESC' && <ChevronDown size={14} aria-label={t('grid.sortedDescending')} />}
                        </Button>
                      ) : (
                        t(c.label)
                      )}
                    </Table.ColumnHeaderCell>
                  )
                })}
                {actions && <Table.ColumnHeaderCell />}
              </Table.Row>
            </Table.Header>
            <Table.Body>
              {rows.map((row) => (
                <MotionRow key={rowKey(row)} align="center" initial={{ opacity: 0 }} animate={{ opacity: 1 }} transition={{ duration: 0.15 }}>
                  {visible.map((c, i) =>
                    i === 0 ? (
                      <Table.RowHeaderCell key={c.id}>{c.cell(row)}</Table.RowHeaderCell>
                    ) : (
                      <Table.Cell key={c.id}>{c.cell(row)}</Table.Cell>
                    ),
                  )}
                  {actions && <Table.Cell justify="end">{actions(row)}</Table.Cell>}
                </MotionRow>
              ))}
            </Table.Body>
          </Table.Root>
        </Box>
      )}
      <Pager
        page={page}
        pageSize={settings.pageSize}
        total={total}
        onPage={setPage}
        onPageSize={(size) => {
          update({ pageSize: size })
          setPage(0)
        }}
      />
    </>
  )
}
