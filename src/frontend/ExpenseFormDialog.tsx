import { useQuery } from '@apollo/client/react'
import * as RadixForm from '@radix-ui/react-form'
import { Button, Dialog, Flex, Text, TextArea, TextField } from '@radix-ui/themes'
import { useId, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { OdometerField } from './components/OdometerField.tsx'
import { PhotoGallery } from './components/PhotoGallery.tsx'
import { PhotosLeftOut } from './components/PhotosLeftOut.tsx'
import type { Saved } from './components/usePhotoQueue.ts'
import { useOdometerLabel } from './components/useOdometerLabel.ts'
import { usePhotoSession } from './components/usePhotoSession.ts'
import { Field } from './forms.tsx'
import { ExpenseCategoriesDocument, ExpenseDetailsDocument, LogDefaultsDocument, type DistanceUnit, type ReadingFieldName } from './gql/generated.ts'
import { parseDecimal } from './i18n/format.ts'
import { ErrorMessage } from './messages.tsx'
import { ReadNote } from './recognition/ReadNote.tsx'
import { mergeReadings, type ReadValues } from './recognition/readValues.ts'
import { useDraftReadings } from './recognition/useDraftReadings.ts'
import { useReadFill } from './recognition/useReadFill.ts'

export interface ExpenseValues {
  date: string
  title: string
  category: string | null
  amount: number
  currency: string
  odometer: number | null
  note: string | null
}

interface Initial {
  date: string
  title: string
  category: string | null
  amount?: number
  currency: string
  odometer?: number | null
  note: string | null
}

const today = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/** The fields of a new expense that photos can fill in (a receipt's shop becomes the title), and which read value goes into each. */
type FillableField = 'date' | 'title' | 'amount' | 'currency' | 'odometer'
const READ_INTO: Record<FillableField, ReadingFieldName> = { date: 'DATE', title: 'TITLE', amount: 'TOTAL', currency: 'CURRENCY', odometer: 'ODOMETER' }

/**
 * Add an expense (no `expenseId`) or edit one. Starts from today and the currency of the vehicle's latest log; the category suggests
 * what was used before (any text is fine) and the odometer may be left empty. When photo reading is on, the photos of a new expense
 * are read on the server and fill in what the user has not typed yet.
 */
export function ExpenseFormDialog({
  trigger,
  vehicle,
  expenseId,
  onSubmit,
}: {
  trigger: ReactNode
  vehicle: { id: string; units: { distance: DistanceUnit } }
  expenseId?: string
  /** `photoIds`: the drafts uploaded for a new expense (always empty when editing: a saved expense takes its photos right away). */
  onSubmit: (values: ExpenseValues, photoIds: string[]) => Promise<Saved>
}) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const editing = expenseId !== undefined
  const { queue, leftOut, saving, submit, reset } = usePhotoSession(vehicle.id, editing ? undefined : 'expense')
  const drafts = useDraftReadings(queue.uploaded, open && !editing)
  const details = useQuery(ExpenseDetailsDocument, { variables: { id: expenseId ?? '' }, skip: !editing || !open, fetchPolicy: 'network-only' })
  const defaults = useQuery(LogDefaultsDocument, { variables: { vehicleId: vehicle.id }, skip: !open, fetchPolicy: 'network-only' })
  const categories = useQuery(ExpenseCategoriesDocument, { variables: { vehicleId: vehicle.id }, skip: !open, fetchPolicy: 'network-only' })
  const error = details.error ?? defaults.error
  const existing = details.data?.expense
  const ready = defaults.data && (!editing || existing)
  const initial: Initial = existing ?? { date: today(), title: '', category: null, currency: defaults.data?.logDefaults?.currency ?? '', note: null }

  return (
    <Dialog.Root
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) reset()
      }}
    >
      <Dialog.Trigger>{trigger}</Dialog.Trigger>
      <Dialog.Content
        maxWidth="450px"
        // Closing while the expense is being saved would lose track of it.
        onEscapeKeyDown={(e) => saving && e.preventDefault()}
        onInteractOutside={(e) => saving && e.preventDefault()}
      >
        <Dialog.Title>{editing ? t('expenses.dialogEdit') : t('expenses.dialogAdd')}</Dialog.Title>
        <Dialog.Description size="2" mb="4">
          {editing ? t('expenses.dialogEditDescription') : t('expenses.dialogAddDescription')}
        </Dialog.Description>
        {error && <ErrorMessage error={error} />}
        {!error && !ready && (
          <Text as="p" role="status">
            {t('app.loading')}
          </Text>
        )}
        {editing && details.data && !existing && <ErrorMessage>{t('errors.expense.notFound')}</ErrorMessage>}
        {leftOut > 0 && <PhotosLeftOut count={leftOut} />}
        {ready && leftOut === 0 && (
          <ExpenseForm
            initial={initial}
            unit={vehicle.units.distance}
            editing={editing}
            categories={categories.data?.expenseCategories ?? []}
            photosBusy={queue.busy || queue.failed > 0}
            read={mergeReadings(drafts.readings.values())}
            readingDone={drafts.done}
            gallery={
              <PhotoGallery
                kind="expenses"
                logId={expenseId}
                photos={existing?.photos ?? []}
                queue={queue}
                readingIds={drafts.pending}
                disabled={saving}
                onChanged={() => details.refetch()}
              />
            }
            onSubmit={async (values) => {
              if (await submit((photoIds) => onSubmit(values, photoIds), editing)) {
                setOpen(false)
                reset()
              }
            }}
          />
        )}
      </Dialog.Content>
    </Dialog.Root>
  )
}

function ExpenseForm({
  initial,
  unit,
  editing,
  categories,
  gallery,
  photosBusy,
  read,
  readingDone,
  onSubmit,
}: {
  initial: Initial
  unit: DistanceUnit
  editing: boolean
  categories: string[]
  gallery: ReactNode
  /** Chosen photos are still being prepared: saving now would leave them out. */
  photosBusy: boolean
  /** What the photos showed so far. */
  read: ReadValues
  readingDone: boolean
  onSubmit: (values: ExpenseValues) => Promise<unknown>
}) {
  const { t } = useTranslation()
  const odometerLabel = useOdometerLabel(unit, true)
  const labels: Record<FillableField, string> = {
    date: t('expenses.fields.date'),
    title: t('expenses.fields.title'),
    amount: t('expenses.fields.amount'),
    currency: t('expenses.fields.currency'),
    odometer: odometerLabel,
  }
  const fill = useReadFill(
    { date: initial.date, title: initial.title, amount: initial.amount?.toString() ?? '', currency: initial.currency, odometer: initial.odometer?.toString() ?? '' },
    READ_INTO,
    read,
    labels,
    readingDone,
  )
  const note = (field: FillableField) => (
    <ReadNote filled={fill.isFilled(field)} offered={fill.offered(field)} field={labels[field]} onUse={() => fill.use(field)} />
  )
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)
  const listId = useId()

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    const text = (name: string) => String(form.get(name) ?? '').trim()
    setBusy(true)
    setError(undefined)
    try {
      await onSubmit({
        date: text('date'),
        title: text('title'),
        category: text('category') || null,
        amount: parseDecimal(text('amount'))!,
        currency: text('currency').toUpperCase(),
        odometer: text('odometer') === '' ? null : Number(text('odometer')),
        note: text('note') || null,
      })
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <RadixForm.Root onSubmit={submit}>
      <Flex direction="column" gap="3">
        <Field name="date" label={labels.date} required extra={note('date')}>
          <TextField.Root type="date" required max={today()} value={fill.values.date} onChange={(e) => fill.change('date', e.target.value)} />
        </Field>
        <Field name="title" label={labels.title} required extra={note('title')}>
          <TextField.Root required maxLength={120} autoComplete="off" value={fill.values.title} onChange={(e) => fill.change('title', e.target.value)} />
        </Field>
        <Field
          name="category"
          label={t('expenses.fields.category')}
          hint={categories.length > 0 ? t('expenses.hints.categories', { list: categories.join(', ') }) : undefined}
        >
          <TextField.Root maxLength={60} autoComplete="off" list={listId} defaultValue={initial.category ?? ''} />
        </Field>
        <datalist id={listId}>
          {categories.map((c) => (
            <option key={c} value={c} />
          ))}
        </datalist>
        <Flex gap="3" wrap="wrap">
          <Flex direction="column" style={{ flex: '2 1 8rem' }}>
            <Field
              name="amount"
              label={labels.amount}
              required
              invalid={{ message: t('forms.numberInvalid'), test: (v) => v !== '' && parseDecimal(v) === undefined }}
              extra={note('amount')}
            >
              <TextField.Root required inputMode="decimal" autoComplete="off" value={fill.values.amount} onChange={(e) => fill.change('amount', e.target.value)} />
            </Field>
          </Flex>
          <Flex direction="column" style={{ flex: '1 1 6rem' }}>
            <Field
              name="currency"
              label={labels.currency}
              required
              invalid={{ message: t('errors.money.currencyInvalid'), test: (v) => v !== '' && !/^[A-Za-z]{3}$/.test(v.trim()) }}
              extra={note('currency')}
            >
              <TextField.Root
                required
                maxLength={3}
                autoComplete="off"
                style={{ textTransform: 'uppercase' }}
                value={fill.values.currency}
                onChange={(e) => fill.change('currency', e.target.value)}
              />
            </Field>
          </Flex>
        </Flex>
        <OdometerField unit={unit} optional value={fill.values.odometer} onChange={(v) => fill.change('odometer', v)} extra={note('odometer')} />
        <Field name="note" label={t('expenses.fields.note')}>
          <TextArea maxLength={500} rows={2} defaultValue={initial.note ?? ''} />
        </Field>
        {gallery}
        <div role="status" aria-label={t('a11y.readingStatus')}>
          {fill.announcement && <Text size="2">{fill.announcement}</Text>}
        </div>
        {error !== undefined && <ErrorMessage error={error} />}
        <Flex gap="3" justify="end">
          <Dialog.Close>
            <Button type="button" variant="soft" color="gray" disabled={busy}>
              {t('common.cancel')}
            </Button>
          </Dialog.Close>
          <RadixForm.Submit asChild>
            <Button disabled={busy || photosBusy}>{editing ? t('expenses.save') : t('expenses.saveAdd')}</Button>
          </RadixForm.Submit>
        </Flex>
      </Flex>
    </RadixForm.Root>
  )
}
