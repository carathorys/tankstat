import { useMutation } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { Pencil, Trash2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { IconAction } from '../../components/IconAction.tsx'
import { UserChip } from '../../components/UserAvatar.tsx'
import { ExpenseFormDialog } from '../../ExpenseFormDialog.tsx'
import { anyAwaiting } from '../../recognition/review.ts'
import { ReviewBadge } from '../../recognition/ReviewState.tsx'
import {
  DeleteExpenseDocument,
  ExpensesDocument,
  UpdateExpenseDocument,
  type DistanceUnit,
  type ExpenseSortField,
  type ExpensesQuery,
  type ExpensesQueryVariables,
} from '../../gql/generated.ts'
import { DataGrid, type GridColumn } from '../../grid/DataGrid.tsx'
import { useFormat } from '../../i18n/format.ts'
import { ErrorMessage } from '../../messages.tsx'
import { EXPENSE_QUERIES, useLogMutations } from './useLogMutations.ts'

type Row = ExpensesQuery['expenses'][number]

const refetch = { refetchQueries: EXPENSE_QUERIES, awaitRefetchQueries: true }

export function ExpensesPanel({ vehicle, canLog }: { vehicle: { id: string; units: { distance: DistanceUnit } }; canLog: boolean }) {
  const { t } = useTranslation()
  const format = useFormat()
  const add = useLogMutations(vehicle.id)
  const [updateExpense] = useMutation(UpdateExpenseDocument, refetch)
  const [deleteExpense] = useMutation(DeleteExpenseDocument, { ...refetch, refetchQueries: [...refetch.refetchQueries, 'ExpenseTrash'] })
  const [actionError, setActionError] = useState<unknown>()
  const { units } = vehicle

  const columns = useMemo<GridColumn<Row, ExpensesQueryVariables, ExpenseSortField>[]>(() => {
    const none = t('common.none')
    return [
      {
        id: 'date',
        label: 'columns.date',
        hideable: false,
        mobile: true,
        sortField: 'DATE',
        // Always shown, so it also says when an expense waits for its photos or for someone to check what they showed.
        cell: (r) => (
          <Stack sx={{ alignItems: 'flex-start', gap: 0.5 }}>
            {format.date(r.date)}
            <ReviewBadge state={r.reviewState} />
          </Stack>
        ),
      },
      { id: 'title', label: 'columns.title', hideable: false, mobile: true, sortField: 'TITLE', cell: (r) => r.title },
      { id: 'amount', label: 'columns.amount', include: 'withAmount', mobile: true, sortField: 'AMOUNT', cell: (r) => (r.amount == null || !r.currency ? none : format.money(r.amount, r.currency)) },
      { id: 'category', label: 'columns.category', include: 'withCategory', sortField: 'CATEGORY', cell: (r) => r.category ?? none },
      { id: 'odometer', label: 'columns.odometer', include: 'withOdometer', sortField: 'ODOMETER', cell: (r) => (r.odometer == null ? none : format.distance(r.odometer, units.distance)) },
      { id: 'note', label: 'columns.note', include: 'withNote', cell: (r) => r.note || none },
      { id: 'createdBy', label: 'columns.createdBy', include: 'withCreatedBy', sortField: 'CREATED_BY', cell: (r) => (r.createdBy ? <UserChip user={r.createdBy} /> : none) },
      // The recurring expenses a service visit covered when they were marked done together.
      { id: 'schedules', label: 'columns.schedules', include: 'withSchedules', defaultHidden: true, cell: (r) => (r.schedules?.length ? r.schedules.map((s) => s.title).join(', ') : none) },
    ]
  }, [t, format, units.distance])

  async function moveToTrash(row: Row) {
    setActionError(undefined)
    try {
      await deleteExpense({ variables: { id: row.id } })
    } catch (e) {
      setActionError(e)
    }
  }

  return (
    <>
      {actionError !== undefined && <ErrorMessage error={actionError} />}
      <DataGrid
        gridId="expenses"
        caption={t('expenses.title')}
        query={ExpensesDocument}
        variables={{ vehicleId: vehicle.id }}
        select={(d) => ({ rows: d.expenses, total: d.expenseCount })}
        rowKey={(r) => r.id}
        columns={columns}
        defaultSort={{ column: 'date', direction: 'DESC' }}
        emptyText={t('expenses.empty')}
        pollWhile={anyAwaiting}
        toolbar={({ total }) => (
          <Stack direction="row" sx={{ alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
            {canLog && <ExpenseFormDialog vehicle={vehicle} trigger={<Button size="large">{t('expenses.add')}</Button>} onSubmit={add.expense} />}
            <Typography variant="body2" aria-live="polite" sx={{ color: 'text.secondary' }}>
              {t('expenses.countLabel', { count: total })}
            </Typography>
          </Stack>
        )}
        actions={(r) =>
          r.canEdit ? (
            <Stack direction="row" sx={{ gap: 1, justifyContent: 'flex-end' }}>
              <ExpenseFormDialog
                vehicle={vehicle}
                expenseId={r.id}
                trigger={
                  <IconAction size="large" tone="primary" label={t('expenses.editAria', { title: r.title })}>
                    <Pencil size={16} aria-hidden />
                  </IconAction>
                }
                onSubmit={async (input) => void (await updateExpense({ variables: { input: { ...input, id: r.id } } }))}
              />
              <ConfirmDialog
                trigger={
                  <IconAction size="large" tone="error" label={t('expenses.deleteAria', { title: r.title })}>
                    <Trash2 size={16} aria-hidden />
                  </IconAction>
                }
                title={t('expenses.trashTitle')}
                description={t('expenses.trashDescription', { title: r.title })}
                confirmLabel={t('expenses.trashConfirm')}
                onConfirm={() => void moveToTrash(r)}
              />
            </Stack>
          ) : (
            <Typography variant="body2" sx={{ color: 'text.secondary' }}>
              {t('expenses.viewOnly')}
            </Typography>
          )
        }
      />
    </>
  )
}
