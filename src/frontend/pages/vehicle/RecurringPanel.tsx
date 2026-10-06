import { useMutation, useQuery } from '@apollo/client/react'
import { Box, Button, Checkbox, Flex, IconButton, Table, Text, VisuallyHidden } from '@radix-ui/themes'
import { CheckCheck, Pencil, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { RecurringStatusBadge } from '../../components/RecurringStatus.tsx'
import { savedFrom } from '../../components/usePhotoQueue.ts'
import {
  AddRecurringExpenseDocument,
  DeleteRecurringExpenseDocument,
  MarkRecurringExpensesDoneDocument,
  RecurringExpensesDocument,
  UpdateRecurringExpenseDocument,
  type DistanceUnit,
  type RecurringExpensesQuery,
} from '../../gql/generated.ts'
import { useDueText } from '../../hooks/useDueText.ts'
import { useFormat } from '../../i18n/format.ts'
import { ErrorMessage } from '../../messages.tsx'
import { RecurringDoneDialog, type DoneValues } from '../../RecurringDoneDialog.tsx'
import { preselect } from '../../recurringDone.ts'
import { RecurringFormDialog } from '../../RecurringFormDialog.tsx'

type Item = NonNullable<RecurringExpensesQuery['vehicle']>['recurring'][number]

// The home page shows the same schedules, and marking some done may log an expense: all of them are refreshed.
const refetch = { refetchQueries: ['RecurringExpenses', 'Expenses', 'VehicleDetails', 'LogDefaults', 'ExpenseCategories'], awaitRefetchQueries: true } // never Welcome: the home page asks afresh when it mounts

/**
 * A vehicle's recurring expenses: what repeats, when it was last done, when it is due next, and whether it already is. Whoever may log can
 * tick several and mark them done together (one service visit), or start from one row's Done button.
 */
export function RecurringPanel({ vehicle, canLog }: { vehicle: { id: string; units: { distance: DistanceUnit } }; canLog: boolean }) {
  const { t } = useTranslation()
  const format = useFormat()
  const unit = vehicle.units.distance
  const dueText = useDueText(unit)
  const { data, error } = useQuery(RecurringExpensesDocument, { variables: { vehicleId: vehicle.id }, fetchPolicy: 'cache-and-network' })
  const [addItem] = useMutation(AddRecurringExpenseDocument, refetch)
  const [updateItem] = useMutation(UpdateRecurringExpenseDocument, refetch)
  const [deleteItem] = useMutation(DeleteRecurringExpenseDocument, refetch)
  const [markDone] = useMutation(MarkRecurringExpensesDoneDocument, refetch)
  const [actionError, setActionError] = useState<unknown>()
  const [selection, setSelection] = useState<ReadonlySet<string>>(new Set())
  const items = data?.vehicle?.recurring ?? []
  const chosen = items.filter((i) => selection.has(i.id)).map((i) => i.id) // a deleted schedule drops out by itself
  const allChosen = items.length > 0 && chosen.length === items.length

  const select = (ids: string[], on: boolean) =>
    setSelection((current) => {
      const next = new Set(current)
      for (const id of ids) {
        if (on) next.add(id)
        else next.delete(id)
      }
      return next
    })

  async function done(values: DoneValues, photoIds: string[]) {
    const result = await markDone({ variables: { input: { ...values, photoIds } } })
    return savedFrom(result.data?.markRecurringExpensesDone.expense)
  }

  /** The selection is spent only by the dialog that took it, not by a row's own Done. */
  async function doneSelected(values: DoneValues, photoIds: string[]) {
    const saved = await done(values, photoIds)
    setSelection(new Set())
    return saved
  }

  const schedule = (i: Item) => {
    const time = i.intervalMonths != null ? t('recurring.schedule.months', { count: i.intervalMonths }) : ''
    const distance = i.intervalDistance != null ? format.distance(i.intervalDistance, unit) : ''
    return i.kind === 'COMBINED' ? t('recurring.schedule.combined', { time, distance }) : i.kind === 'TIME' ? time : t('recurring.schedule.distance', { distance })
  }

  const nextDue = (i: Item) => {
    const date = i.status.dueDate ? format.date(i.status.dueDate) : null
    const odometer = i.intervalDistance != null && i.lastDoneOdometer != null ? format.distance(i.lastDoneOdometer + i.intervalDistance, unit) : null
    return date && odometer ? t('recurring.nextDue.both', { date, odometer }) : date ? t('recurring.nextDue.date', { date }) : odometer ? t('recurring.nextDue.odometer', { odometer }) : t('common.none')
  }

  async function remove(item: Item) {
    setActionError(undefined)
    try {
      await deleteItem({ variables: { id: item.id } })
    } catch (e) {
      setActionError(e)
    }
  }

  return (
    <>
      {actionError !== undefined && <ErrorMessage error={actionError} />}
      {error && <ErrorMessage error={error} />}
      <Flex align="center" gap="3" wrap="wrap" mb="3">
        {canLog && (
          <RecurringFormDialog
            vehicleId={vehicle.id}
            unit={unit}
            trigger={<Button size="3">{t('recurring.add')}</Button>}
            onSubmit={(input) => addItem({ variables: { input: { ...input, vehicleId: vehicle.id } } })}
          />
        )}
        {canLog && items.length > 0 && (
          <RecurringDoneDialog
            vehicle={vehicle}
            items={items}
            selected={chosen}
            trigger={
              <Button size="3" variant="soft" disabled={chosen.length === 0}>
                <CheckCheck size={16} aria-hidden />
                {t('recurring.doneSelected', { count: chosen.length })}
              </Button>
            }
            onSubmit={doneSelected}
          />
        )}
        <Text size="2" color="gray" aria-live="polite">
          {t('recurring.countLabel', { count: items.length })}
        </Text>
      </Flex>
      {data && items.length === 0 && <Text as="p">{t('recurring.empty')}</Text>}
      {items.length > 0 && (
        <Box style={{ overflowX: 'auto' }}>
          <Table.Root variant="surface">
            <VisuallyHidden asChild>
              <caption>{t('recurring.title')}</caption>
            </VisuallyHidden>
            <Table.Header>
              <Table.Row>
                {canLog && (
                  <Table.ColumnHeaderCell scope="col">
                    <Flex align="center" justify="center" style={{ minWidth: 44, minHeight: 44 }}>
                      <Checkbox
                        size="3"
                        aria-label={t('recurring.selectAllAria')}
                        checked={allChosen ? true : chosen.length > 0 ? 'indeterminate' : false}
                        onCheckedChange={(c) => select(items.map((i) => i.id), c === true)}
                      />
                    </Flex>
                  </Table.ColumnHeaderCell>
                )}
                <Table.ColumnHeaderCell scope="col">{t('recurring.columns.title')}</Table.ColumnHeaderCell>
                <Table.ColumnHeaderCell scope="col">{t('recurring.columns.schedule')}</Table.ColumnHeaderCell>
                <Table.ColumnHeaderCell scope="col">{t('recurring.columns.lastDone')}</Table.ColumnHeaderCell>
                <Table.ColumnHeaderCell scope="col">{t('recurring.columns.nextDue')}</Table.ColumnHeaderCell>
                <Table.ColumnHeaderCell scope="col">{t('recurring.columns.status')}</Table.ColumnHeaderCell>
                <Table.ColumnHeaderCell scope="col">
                  <VisuallyHidden>{t('grid.actions')}</VisuallyHidden>
                </Table.ColumnHeaderCell>
              </Table.Row>
            </Table.Header>
            <Table.Body>
              {items.map((i) => {
                const due = dueText(i.status) ?? (i.kind === 'ODOMETER' ? t('recurring.due.noOdometer') : undefined)
                return (
                  <Table.Row key={i.id}>
                    {canLog && (
                      <Table.Cell>
                        <Flex align="center" justify="center" style={{ minWidth: 44, minHeight: 44 }}>
                          <Checkbox size="3" aria-label={t('recurring.selectAria', { title: i.title })} checked={selection.has(i.id)} onCheckedChange={(c) => select([i.id], c === true)} />
                        </Flex>
                      </Table.Cell>
                    )}
                    <Table.RowHeaderCell>
                      {i.title}
                      {i.category && (
                        <Text as="p" size="1" color="gray">
                          {i.category}
                        </Text>
                      )}
                    </Table.RowHeaderCell>
                    <Table.Cell>{schedule(i)}</Table.Cell>
                    <Table.Cell>{format.date(i.lastDoneDate)}</Table.Cell>
                    <Table.Cell>{nextDue(i)}</Table.Cell>
                    <Table.Cell>
                      <Flex direction="column" gap="1" align="start">
                        <RecurringStatusBadge state={i.status.state} />
                        {due && (
                          <Text size="1" color="gray">
                            {due}
                          </Text>
                        )}
                      </Flex>
                    </Table.Cell>
                    <Table.Cell>
                      {canLog ? (
                        <Flex gap="2" justify="end">
                          <RecurringDoneDialog
                            vehicle={vehicle}
                            items={items}
                            selected={preselect(items, i.id)}
                            openedFrom={i}
                            trigger={
                              <IconButton size="3" variant="soft" aria-label={t('recurring.doneAria', { title: i.title })}>
                                <CheckCheck size={16} aria-hidden />
                              </IconButton>
                            }
                            onSubmit={done}
                          />
                          <RecurringFormDialog
                            vehicleId={vehicle.id}
                            unit={unit}
                            initial={i}
                            trigger={
                              <IconButton size="3" variant="soft" aria-label={t('recurring.editAria', { title: i.title })}>
                                <Pencil size={16} aria-hidden />
                              </IconButton>
                            }
                            onSubmit={(input) => updateItem({ variables: { input: { ...input, id: i.id } } })}
                          />
                          <ConfirmDialog
                            trigger={
                              <IconButton size="3" variant="soft" color="red" aria-label={t('recurring.deleteAria', { title: i.title })}>
                                <Trash2 size={16} aria-hidden />
                              </IconButton>
                            }
                            title={t('recurring.deleteTitle')}
                            description={t('recurring.deleteDescription', { title: i.title })}
                            confirmLabel={t('recurring.deleteConfirm')}
                            onConfirm={() => void remove(i)}
                          />
                        </Flex>
                      ) : (
                        <Text size="2" color="gray">
                          {t('recurring.viewOnly')}
                        </Text>
                      )}
                    </Table.Cell>
                  </Table.Row>
                )
              })}
            </Table.Body>
          </Table.Root>
        </Box>
      )}
    </>
  )
}
