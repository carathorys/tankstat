import { useQuery } from '@apollo/client/react'
import * as RadixForm from '@radix-ui/react-form'
import { Button, Checkbox, Dialog, Flex, Text, TextField } from '@radix-ui/themes'
import { useId, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { OdometerField } from './components/OdometerField.tsx'
import { PhotoGallery } from './components/PhotoGallery.tsx'
import { PhotosLeftOut } from './components/PhotosLeftOut.tsx'
import { RecurringStatusBadge } from './components/RecurringStatus.tsx'
import type { Saved } from './components/usePhotoQueue.ts'
import { useOdometerLabel } from './components/useOdometerLabel.ts'
import { usePhotoSession } from './components/usePhotoSession.ts'
import { Field } from './forms.tsx'
import { ExpenseCategoriesDocument, LogDefaultsDocument, type DistanceUnit, type LogDefaultsQuery, type ReadingFieldName } from './gql/generated.ts'
import { parseDecimal, useFormat } from './i18n/format.ts'
import { ErrorMessage } from './messages.tsx'
import { ReadNote } from './recognition/ReadNote.tsx'
import { ReadingProblems } from './recognition/ReadingProblems.tsx'
import type { ReadingExplanation } from './recognition/readingIssues.ts'
import { mergeReadings, type ReadValues } from './recognition/readValues.ts'
import { useDraftReadings } from './recognition/useDraftReadings.ts'
import { useReadFill } from './recognition/useReadFill.ts'
import { defaultCategory, defaultTitle, MAX_EXPENSE_TITLE, usesDistance, type DoneItem } from './recurringDone.ts'

/** One service visit: the schedules done, and (with an amount) the one expense logged for all of them. */
export interface DoneValues {
  ids: string[]
  date: string
  odometer: number | null
  /** Null: no expense is logged (unless a photo still being read may give it), only the schedules move on. */
  amount: number | null
  currency: string | null
  title: string
  category: string | null
}

/** The fields photos can fill in (a dashboard: the odometer; an invoice: the cost and the day), and which read value goes into each. */
type FillableField = 'date' | 'odometer' | 'amount' | 'currency'
const READ_INTO: Record<FillableField, ReadingFieldName> = { date: 'DATE', odometer: 'ODOMETER', amount: 'TOTAL', currency: 'CURRENCY' }

const today = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/**
 * "Mark as done" for a service visit: the vehicle's schedules are listed, the ones done at this visit ticked (opened from one schedule, that
 * one and everything else that needs attention start ticked). The next interval of each ticked schedule starts from the day and odometer
 * entered. The amount is optional and covers all of them together, never split: with an amount one expense is logged for the visit (its
 * title and category prefilled from the schedules) and remembers each schedule; without one only the schedules move on. Photos (the
 * dashboard, the invoice) go with the logged expense: an expense is logged when an amount is given or a photo is still being read (it may
 * fill the amount in, under the expense rules); otherwise the photos are not kept.
 */
export function RecurringDoneDialog({
  trigger,
  vehicle,
  items,
  selected,
  openedFrom,
  onSubmit,
  onCloseAutoFocus,
}: {
  trigger: ReactNode
  vehicle: { id: string; units: { distance: DistanceUnit } }
  /** The vehicle's schedules, most urgent first. */
  items: DoneItem[]
  /** What starts ticked. */
  selected: string[]
  /** The schedule the dialog was opened from (its title names the dialog); absent for "Mark selected as done". */
  openedFrom?: { title: string }
  onSubmit: (values: DoneValues, photoIds: string[]) => Promise<Saved>
  /** Where the focus goes when the dialog closes (by default back to the trigger); for a trigger that is gone by then, see `VehicleCard`. */
  onCloseAutoFocus?: (event: Event) => void
}) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const { queue, leftOut, saving, submit, reset } = usePhotoSession(vehicle.id, 'expense', open)
  const drafts = useDraftReadings(queue.uploaded, open)

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
        maxWidth="480px"
        // Closing while it is being saved would lose track of it.
        onEscapeKeyDown={(e) => saving && e.preventDefault()}
        onInteractOutside={(e) => saving && e.preventDefault()}
        onCloseAutoFocus={onCloseAutoFocus}
      >
        <Dialog.Title>{openedFrom ? t('recurring.doneTitle', { title: openedFrom.title }) : t('recurring.doneTitleMany')}</Dialog.Title>
        <Dialog.Description size="2" mb="4">
          {t('recurring.doneDescription')}
        </Dialog.Description>
        {leftOut > 0 && <PhotosLeftOut count={leftOut} />}
        {open && leftOut === 0 && (
          <DoneForm
            vehicle={vehicle}
            items={items}
            selected={selected}
            photosBusy={queue.busy || queue.failed > 0}
            photosPicked={queue.items.length > 0}
            read={mergeReadings(drafts.readings.values())}
            readingDone={drafts.done}
            reading={drafts.pending.length > 0}
            explanation={drafts.explanation}
            gallery={<PhotoGallery kind="expenses" photos={[]} queue={queue} readingIds={drafts.pending} disabled={saving} onChanged={() => undefined} />}
            onSubmit={async (values, logsExpense) => {
              // Without an expense the photos have nothing to belong to: they are not sent, and closing deletes them.
              if (await submit((photoIds) => onSubmit(values, photoIds), !logsExpense)) {
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

interface FormProps {
  vehicle: { id: string; units: { distance: DistanceUnit } }
  items: DoneItem[]
  selected: string[]
  photosBusy: boolean
  photosPicked: boolean
  /** What the photos showed so far. */
  read: ReadValues
  readingDone: boolean
  /** A photo is still being read: the amount may be left to it. */
  reading: boolean
  /** Why the photos gave less than they might have. */
  explanation: ReadingExplanation
  gallery: ReactNode
  onSubmit: (values: DoneValues, logsExpense: boolean) => Promise<unknown>
}

function DoneForm(props: FormProps) {
  const { t } = useTranslation()
  const defaults = useQuery(LogDefaultsDocument, { variables: { vehicleId: props.vehicle.id }, fetchPolicy: 'network-only' })
  const categories = useQuery(ExpenseCategoriesDocument, { variables: { vehicleId: props.vehicle.id }, fetchPolicy: 'network-only' })
  const d = defaults.data?.logDefaults
  if (!d) return defaults.error ? <ErrorMessage error={defaults.error} /> : <Text as="p" role="status">{t('app.loading')}</Text>
  return <DoneFields {...props} defaults={d} categories={categories.data?.expenseCategories ?? []} />
}

function DoneFields({
  vehicle,
  items,
  selected,
  photosBusy,
  photosPicked,
  read,
  readingDone,
  reading,
  explanation,
  gallery,
  defaults: d,
  categories,
  onSubmit,
}: FormProps & { defaults: NonNullable<LogDefaultsQuery['logDefaults']>; categories: string[] }) {
  const { t } = useTranslation()
  const [ticked, setTicked] = useState(() => new Set(selected))
  // The expense's title and category follow the ticked schedules until the person types their own (null: not typed).
  const [title, setTitle] = useState<string | null>(null)
  const [category, setCategory] = useState<string | null>(null)
  const [error, setError] = useState<unknown>()
  const [noneTicked, setNoneTicked] = useState(false)
  const [busy, setBusy] = useState(false)
  const listId = useId()
  const idBase = useId()
  const done = items.filter((i) => ticked.has(i.id))
  const distance = usesDistance(done)
  const last = d.lastOdometer != null && d.lastDate ? { value: d.lastOdometer, date: d.lastDate } : null
  const odometerLabel = useOdometerLabel(vehicle.units.distance, !distance)
  const { distance: formatDistance } = useFormat()
  const labels: Record<FillableField, string> = {
    date: t('recurring.doneDate'),
    odometer: odometerLabel,
    amount: t('recurring.amountOptional'),
    currency: t('recurring.currency'),
  }
  const fill = useReadFill({ date: today(), odometer: d.lastOdometer?.toString() ?? '', amount: '', currency: d.currency ?? '' }, READ_INTO, read, labels, readingDone, {
    ...explanation,
    last: last ? formatDistance(last.value, vehicle.units.distance) : undefined,
  })
  const hasAmount = fill.values.amount.trim() !== ''
  const logsExpense = hasAmount || reading
  const titleValue = title ?? defaultTitle(done)
  const categoryValue = category ?? defaultCategory(done)
  const note = (field: FillableField) => (
    <ReadNote
      filled={fill.isFilled(field)}
      offered={fill.offered(field)}
      waiting={reading && field === 'amount' && !hasAmount}
      field={labels[field]}
      onUse={() => fill.use(field)}
    />
  )

  const toggle = (id: string, on: boolean) => {
    setNoneTicked(false)
    setTicked((current) => {
      const next = new Set(current)
      if (on) next.add(id)
      else next.delete(id)
      return next
    })
  }

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (done.length === 0) {
      setNoneTicked(true)
      return
    }
    const form = new FormData(e.currentTarget)
    const text = (name: string) => String(form.get(name) ?? '').trim()
    setBusy(true)
    setError(undefined)
    try {
      await onSubmit(
        {
          ids: done.map((i) => i.id),
          date: text('date'),
          odometer: text('odometer') === '' ? null : Number(text('odometer')),
          amount: hasAmount ? (parseDecimal(text('amount')) ?? null) : null,
          currency: hasAmount ? text('currency').toUpperCase() : null,
          title: titleValue.trim(),
          category: categoryValue.trim() || null,
        },
        logsExpense,
      )
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <RadixForm.Root onSubmit={submit}>
      <Flex direction="column" gap="3">
        <fieldset style={{ border: 0, padding: 0, margin: 0 }}>
          <Text asChild size="2" weight="bold">
            <legend style={{ padding: 0, marginBottom: 'var(--space-1)' }}>{t('recurring.doneItems')}</legend>
          </Text>
          <Flex asChild direction="column">
            <ul style={{ listStyle: 'none', padding: 0, margin: 0 }}>
              {items.map((i) => {
                const id = `${idBase}-${i.id}`
                return (
                  <li key={i.id}>
                    <Flex align="center" gap="3" style={{ minHeight: 44 }}>
                      <Checkbox id={id} size="3" checked={ticked.has(i.id)} onCheckedChange={(c) => toggle(i.id, c === true)} />
                      <Text as="label" size="2" htmlFor={id} style={{ flexGrow: 1 }}>
                        {i.title}
                      </Text>
                      {i.status.state !== 'UPCOMING' && <RecurringStatusBadge state={i.status.state} />}
                    </Flex>
                  </li>
                )
              })}
            </ul>
          </Flex>
          <Text as="p" size="1" color="gray" role="status">
            {t('recurring.selectedCount', { count: done.length })}
          </Text>
        </fieldset>
        {noneTicked && <ErrorMessage>{t('errors.recurring.noneSelected')}</ErrorMessage>}
        <Field name="date" label={labels.date} required extra={note('date')}>
          <TextField.Root type="date" required max={today()} value={fill.values.date} onChange={(e) => fill.change('date', e.target.value)} />
        </Field>
        <OdometerField
          unit={vehicle.units.distance}
          last={last}
          optional={!distance}
          value={fill.values.odometer}
          onChange={(v) => fill.change('odometer', v)}
          extra={note('odometer')}
        />
        <Flex gap="3" wrap="wrap">
          <Flex direction="column" style={{ flex: '2 1 8rem' }}>
            <Field
              name="amount"
              label={labels.amount}
              hint={t('recurring.amountHint')}
              invalid={{ message: t('forms.numberInvalid'), test: (v) => v !== '' && parseDecimal(v) === undefined }}
              extra={note('amount')}
            >
              <TextField.Root inputMode="decimal" autoComplete="off" value={fill.values.amount} onChange={(e) => fill.change('amount', e.target.value)} />
            </Field>
          </Flex>
          <Flex direction="column" style={{ flex: '1 1 6rem' }}>
            <Field
              name="currency"
              label={labels.currency}
              required={hasAmount}
              invalid={{ message: t('errors.money.currencyInvalid'), test: (v) => v !== '' && !/^[A-Za-z]{3}$/.test(v.trim()) }}
              extra={note('currency')}
            >
              <TextField.Root
                required={hasAmount}
                maxLength={3}
                autoComplete="off"
                style={{ textTransform: 'uppercase' }}
                value={fill.values.currency}
                onChange={(e) => fill.change('currency', e.target.value)}
              />
            </Field>
          </Flex>
        </Flex>
        <Field name="title" label={t('recurring.expenseTitle')} required={logsExpense}>
          <TextField.Root required={logsExpense} maxLength={MAX_EXPENSE_TITLE} autoComplete="off" value={titleValue} onChange={(e) => setTitle(e.target.value)} />
        </Field>
        <Field name="category" label={t('recurring.expenseCategory')}>
          <TextField.Root maxLength={60} autoComplete="off" list={listId} value={categoryValue} onChange={(e) => setCategory(e.target.value)} />
        </Field>
        <datalist id={listId}>
          {categories.map((c) => (
            <option key={c} value={c} />
          ))}
        </datalist>
        {gallery}
        {photosPicked && !logsExpense && (
          <Text size="1" color="gray">
            {t('recurring.photosNotKept')}
          </Text>
        )}
        <div role="status" aria-label={t('a11y.readingStatus')}>
          {fill.announcement && <Text size="2">{fill.announcement}</Text>}
          <ReadingProblems problems={fill.problems} />
        </div>
        {error !== undefined && <ErrorMessage error={error} />}
        <Flex gap="3" justify="end">
          <Dialog.Close>
            <Button type="button" variant="soft" color="gray" disabled={busy}>
              {t('common.cancel')}
            </Button>
          </Dialog.Close>
          <RadixForm.Submit asChild>
            <Button disabled={busy || photosBusy}>{t('recurring.doneSave')}</Button>
          </RadixForm.Submit>
        </Flex>
      </Flex>
    </RadixForm.Root>
  )
}
