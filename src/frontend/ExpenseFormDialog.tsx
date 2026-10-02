import { useQuery } from '@apollo/client/react'
import * as RadixForm from '@radix-ui/react-form'
import { Button, Dialog, Flex, Text, TextArea, TextField } from '@radix-ui/themes'
import { useId, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { OdometerField } from './components/OdometerField.tsx'
import { PhotoGallery } from './components/PhotoGallery.tsx'
import { PhotosAfterSave } from './components/PhotosAfterSave.tsx'
import type { Saved } from './components/usePhotoQueue.ts'
import { usePhotoSession } from './components/usePhotoSession.ts'
import { Field } from './forms.tsx'
import { ExpenseCategoriesDocument, ExpenseDetailsDocument, LogDefaultsDocument, type DistanceUnit } from './gql/generated.ts'
import { parseDecimal } from './i18n/format.ts'
import { ErrorMessage } from './messages.tsx'

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

/**
 * Add an expense (no `expenseId`) or edit one. Starts from today and the currency of the vehicle's latest log; the category suggests
 * what was used before (any text is fine) and the odometer may be left empty.
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
  onSubmit: (values: ExpenseValues) => Promise<Saved>
}) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const { queue, savedId, failure: uploadFailure, saving, submit, reset } = usePhotoSession('expenses')
  const editing = expenseId !== undefined
  const logId = expenseId ?? savedId
  const details = useQuery(ExpenseDetailsDocument, { variables: { id: logId ?? '' }, skip: logId === undefined || !open, fetchPolicy: 'network-only' })
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
        // Closing while the expense and its photos are on their way would lose track of them.
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
        {savedId !== undefined && (
          <PhotosAfterSave kind="expenses" logId={savedId} failure={uploadFailure} photos={existing?.photos ?? []} queue={queue} onChanged={() => details.refetch()} />
        )}
        {ready && savedId === undefined && (
          <ExpenseForm
            initial={initial}
            unit={vehicle.units.distance}
            editing={editing}
            categories={categories.data?.expenseCategories ?? []}
            photosBusy={queue.adding}
            gallery={<PhotoGallery kind="expenses" logId={expenseId} photos={existing?.photos ?? []} queue={queue} disabled={saving} onChanged={() => details.refetch()} />}
            onSubmit={async (values) => {
              if (await submit(() => onSubmit(values), editing)) {
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
  onSubmit,
}: {
  initial: Initial
  unit: DistanceUnit
  editing: boolean
  categories: string[]
  gallery: ReactNode
  /** Chosen photos are still being prepared: saving now would leave them out. */
  photosBusy: boolean
  onSubmit: (values: ExpenseValues) => Promise<unknown>
}) {
  const { t } = useTranslation()
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
        <Field name="date" label={t('expenses.fields.date')} required>
          <TextField.Root type="date" required max={today()} defaultValue={initial.date} />
        </Field>
        <Field name="title" label={t('expenses.fields.title')} required>
          <TextField.Root required maxLength={120} autoComplete="off" defaultValue={initial.title} />
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
              label={t('expenses.fields.amount')}
              required
              invalid={{ message: t('forms.numberInvalid'), test: (v) => v !== '' && parseDecimal(v) === undefined }}
            >
              <TextField.Root required inputMode="decimal" autoComplete="off" defaultValue={initial.amount?.toString() ?? ''} />
            </Field>
          </Flex>
          <Flex direction="column" style={{ flex: '1 1 6rem' }}>
            <Field
              name="currency"
              label={t('expenses.fields.currency')}
              required
              invalid={{ message: t('errors.money.currencyInvalid'), test: (v) => v !== '' && !/^[A-Za-z]{3}$/.test(v.trim()) }}
            >
              <TextField.Root required maxLength={3} autoComplete="off" style={{ textTransform: 'uppercase' }} defaultValue={initial.currency} />
            </Field>
          </Flex>
        </Flex>
        <OdometerField unit={unit} defaultValue={initial.odometer ?? undefined} optional />
        <Field name="note" label={t('expenses.fields.note')}>
          <TextArea maxLength={500} rows={2} defaultValue={initial.note ?? ''} />
        </Field>
        {gallery}
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
