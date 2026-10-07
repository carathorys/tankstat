import { useMutation, useQuery } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import Stack from '@mui/material/Stack'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'
import { CheckCheck, Pencil, Trash2 } from 'lucide-react'
import { AnimatePresence } from 'motion/react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { IconAction } from '../../components/IconAction.tsx'
import { MotionTableRow, rowMotion } from '../../components/motion.ts'
import { RecurringStatusBadge } from '../../components/RecurringStatus.tsx'
import { SurfaceTable } from '../../components/SurfaceTable.tsx'
import { visuallyHidden } from '../../components/visuallyHidden.ts'
import { LazyGauges } from '../../dashboard/LazyGauges.tsx'
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
import { recurringProgress } from '../../recurringProgress.ts'

type Item = NonNullable<RecurringExpensesQuery['vehicle']>['recurring'][number]

// The home page shows the same schedules, and marking some done may log an expense: all of them are refreshed.
const refetch = { refetchQueries: ['RecurringExpenses', 'Expenses', 'VehicleDetails', 'LogDefaults', 'ExpenseCategories'], awaitRefetchQueries: true } // never Welcome: the home page asks afresh when it mounts

/**
 * A vehicle's recurring expenses: what repeats, when it was last done, when it is due next, and whether it already is (in words, and as a
 * dial per limit). Whoever may log can tick several and mark them done together (one service visit), or start from one row's Done button.
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

  const box = { minWidth: 44, minHeight: 44 }
  return (
    <>
      {actionError !== undefined && <ErrorMessage error={actionError} />}
      {error && <ErrorMessage error={error} />}
      <Stack direction="row" sx={{ alignItems: 'center', gap: 1.5, flexWrap: 'wrap', mb: 1.5 }}>
        {canLog && (
          <RecurringFormDialog
            vehicleId={vehicle.id}
            unit={unit}
            trigger={<Button size="large">{t('recurring.add')}</Button>}
            onSubmit={(input) => addItem({ variables: { input: { ...input, vehicleId: vehicle.id } } })}
          />
        )}
        {canLog && items.length > 0 && (
          <RecurringDoneDialog
            vehicle={vehicle}
            items={items}
            selected={chosen}
            trigger={
              <Button size="large" variant="soft" disabled={chosen.length === 0}>
                <CheckCheck size={16} aria-hidden />
                {t('recurring.doneSelected', { count: chosen.length })}
              </Button>
            }
            onSubmit={doneSelected}
          />
        )}
        <Typography variant="body2" aria-live="polite" sx={{ color: 'text.secondary' }}>
          {t('recurring.countLabel', { count: items.length })}
        </Typography>
      </Stack>
      {data && items.length === 0 && <Typography>{t('recurring.empty')}</Typography>}
      {items.length > 0 && (
        <SurfaceTable caption={t('recurring.title')}>
          <TableHead>
            <TableRow>
              {canLog && (
                <TableCell padding="checkbox">
                  <Checkbox
                    sx={box}
                    slotProps={{ input: { 'aria-label': t('recurring.selectAllAria') } }}
                    checked={allChosen}
                    indeterminate={!allChosen && chosen.length > 0}
                    onChange={(e) => select(items.map((i) => i.id), e.target.checked)}
                  />
                </TableCell>
              )}
              <TableCell>{t('recurring.columns.title')}</TableCell>
              <TableCell>{t('recurring.columns.schedule')}</TableCell>
              <TableCell>{t('recurring.columns.lastDone')}</TableCell>
              <TableCell>{t('recurring.columns.nextDue')}</TableCell>
              <TableCell>{t('recurring.columns.status')}</TableCell>
              <TableCell>
                <span style={visuallyHidden}>{t('grid.actions')}</span>
              </TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            <AnimatePresence initial={false}>
              {items.map((i) => {
                const due = dueText(i.status) ?? (i.kind === 'ODOMETER' ? t('recurring.due.noOdometer') : undefined)
                return (
                  <MotionTableRow key={i.id} {...rowMotion} sx={{ verticalAlign: 'top' }}>
                    {canLog && (
                      <TableCell padding="checkbox">
                        <Checkbox sx={box} slotProps={{ input: { 'aria-label': t('recurring.selectAria', { title: i.title }) } }} checked={selection.has(i.id)} onChange={(e) => select([i.id], e.target.checked)} />
                      </TableCell>
                    )}
                    <TableCell component="th" scope="row">
                      {i.title}
                      {i.category && (
                        <Typography variant="caption" component="p" sx={{ color: 'text.secondary' }}>
                          {i.category}
                        </Typography>
                      )}
                    </TableCell>
                    <TableCell>{schedule(i)}</TableCell>
                    <TableCell>{format.date(i.lastDoneDate)}</TableCell>
                    <TableCell>{nextDue(i)}</TableCell>
                    <TableCell>
                      <Stack direction="row" sx={{ alignItems: 'center', gap: 1 }}>
                        <LazyGauges progress={recurringProgress(i)} state={i.status.state} />
                        <Stack sx={{ gap: 0.5, alignItems: 'flex-start' }}>
                          <RecurringStatusBadge state={i.status.state} />
                          {due && (
                            <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                              {due}
                            </Typography>
                          )}
                        </Stack>
                      </Stack>
                    </TableCell>
                    <TableCell>
                      {canLog ? (
                        <Stack direction="row" sx={{ gap: 1, justifyContent: 'flex-end' }}>
                          <RecurringDoneDialog
                            vehicle={vehicle}
                            items={items}
                            selected={preselect(items, i.id)}
                            openedFrom={i}
                            trigger={
                              <IconAction size="large" tone="primary" label={t('recurring.doneAria', { title: i.title })}>
                                <CheckCheck size={16} aria-hidden />
                              </IconAction>
                            }
                            onSubmit={done}
                          />
                          <RecurringFormDialog
                            vehicleId={vehicle.id}
                            unit={unit}
                            initial={i}
                            trigger={
                              <IconAction size="large" tone="primary" label={t('recurring.editAria', { title: i.title })}>
                                <Pencil size={16} aria-hidden />
                              </IconAction>
                            }
                            onSubmit={(input) => updateItem({ variables: { input: { ...input, id: i.id } } })}
                          />
                          <ConfirmDialog
                            trigger={
                              <IconAction size="large" tone="error" label={t('recurring.deleteAria', { title: i.title })}>
                                <Trash2 size={16} aria-hidden />
                              </IconAction>
                            }
                            title={t('recurring.deleteTitle')}
                            description={t('recurring.deleteDescription', { title: i.title })}
                            confirmLabel={t('recurring.deleteConfirm')}
                            onConfirm={() => void remove(i)}
                          />
                        </Stack>
                      ) : (
                        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                          {t('recurring.viewOnly')}
                        </Typography>
                      )}
                    </TableCell>
                  </MotionTableRow>
                )
              })}
            </AnimatePresence>
          </TableBody>
        </SurfaceTable>
      )}
    </>
  )
}
