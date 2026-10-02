import { useMutation } from '@apollo/client/react'
import { Button, Flex, IconButton, Text } from '@radix-ui/themes'
import { Pencil, Trash2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { UserChip } from '../../components/UserAvatar.tsx'
import { ExpenseFormDialog } from '../../ExpenseFormDialog.tsx'
import {
  AddExpenseDocument,
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

type Row = ExpensesQuery['expenses'][number]

const refetch = { refetchQueries: ['Expenses', 'VehicleDetails', 'LogDefaults', 'ExpenseCategories'], awaitRefetchQueries: true }

export function ExpensesPanel({ vehicle, canLog }: { vehicle: { id: string; units: { distance: DistanceUnit } }; canLog: boolean }) {
  const { t } = useTranslation()
  const format = useFormat()
  const [addExpense] = useMutation(AddExpenseDocument, refetch)
  const [updateExpense] = useMutation(UpdateExpenseDocument, refetch)
  const [deleteExpense] = useMutation(DeleteExpenseDocument, { ...refetch, refetchQueries: [...refetch.refetchQueries, 'ExpenseTrash'] })
  const [actionError, setActionError] = useState<unknown>()
  const { units } = vehicle

  const columns = useMemo<GridColumn<Row, ExpensesQueryVariables, ExpenseSortField>[]>(() => {
    const none = t('common.none')
    return [
      { id: 'date', label: 'columns.date', hideable: false, mobile: true, sortField: 'DATE', cell: (r) => format.date(r.date) },
      { id: 'title', label: 'columns.title', hideable: false, mobile: true, sortField: 'TITLE', cell: (r) => r.title },
      { id: 'amount', label: 'columns.amount', include: 'withAmount', mobile: true, sortField: 'AMOUNT', cell: (r) => (r.amount == null || !r.currency ? none : format.money(r.amount, r.currency)) },
      { id: 'category', label: 'columns.category', include: 'withCategory', sortField: 'CATEGORY', cell: (r) => r.category ?? none },
      { id: 'odometer', label: 'columns.odometer', include: 'withOdometer', sortField: 'ODOMETER', cell: (r) => (r.odometer == null ? none : format.distance(r.odometer, units.distance)) },
      { id: 'note', label: 'columns.note', include: 'withNote', cell: (r) => r.note || none },
      { id: 'createdBy', label: 'columns.createdBy', include: 'withCreatedBy', sortField: 'CREATED_BY', cell: (r) => (r.createdBy ? <UserChip user={r.createdBy} /> : none) },
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
        toolbar={({ total }) => (
          <Flex align="center" gap="3" wrap="wrap">
            {canLog && (
              <ExpenseFormDialog
                vehicle={vehicle}
                trigger={<Button size="3">{t('expenses.add')}</Button>}
                onSubmit={async (input) => {
                  const added = await addExpense({ variables: { input: { ...input, vehicleId: vehicle.id } } })
                  return added.data ? { id: added.data.addExpense.id } : undefined
                }}
              />
            )}
            <Text size="2" color="gray" aria-live="polite">
              {t('expenses.countLabel', { count: total })}
            </Text>
          </Flex>
        )}
        actions={(r) =>
          r.canEdit ? (
            <Flex gap="2" justify="end">
              <ExpenseFormDialog
                vehicle={vehicle}
                expenseId={r.id}
                trigger={
                  <IconButton size="3" variant="soft" aria-label={t('expenses.editAria', { title: r.title })}>
                    <Pencil size={16} aria-hidden />
                  </IconButton>
                }
                onSubmit={async (input) => void (await updateExpense({ variables: { input: { ...input, id: r.id } } }))}
              />
              <ConfirmDialog
                trigger={
                  <IconButton size="3" variant="soft" color="red" aria-label={t('expenses.deleteAria', { title: r.title })}>
                    <Trash2 size={16} aria-hidden />
                  </IconButton>
                }
                title={t('expenses.trashTitle')}
                description={t('expenses.trashDescription', { title: r.title })}
                confirmLabel={t('expenses.trashConfirm')}
                onConfirm={() => void moveToTrash(r)}
              />
            </Flex>
          ) : (
            <Text size="2" color="gray">
              {t('expenses.viewOnly')}
            </Text>
          )
        }
      />
    </>
  )
}
