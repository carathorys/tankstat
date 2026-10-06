import { useQuery } from '@apollo/client/react'
import * as RadixForm from '@radix-ui/react-form'
import { Button, Dialog, Flex, Switch, Text, TextField } from '@radix-ui/themes'
import { useId, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { OdometerField } from './components/OdometerField.tsx'
import { PhotoGallery } from './components/PhotoGallery.tsx'
import { useOdometerLabel } from './components/useOdometerLabel.ts'
import { usePhotoSession } from './components/usePhotoSession.ts'
import { Field } from './forms.tsx'
import { LogDefaultsDocument, type DistanceUnit, type LogDefaultsQuery, type ReadingFieldName, type RecurrenceKind } from './gql/generated.ts'
import { parseDecimal, useFormat } from './i18n/format.ts'
import { ErrorMessage } from './messages.tsx'
import { ReadNote } from './recognition/ReadNote.tsx'
import { ReadingProblems } from './recognition/ReadingProblems.tsx'
import type { ReadingExplanation } from './recognition/readingIssues.ts'
import { mergeReadings, type ReadValues } from './recognition/readValues.ts'
import { useDraftReadings } from './recognition/useDraftReadings.ts'
import { useReadFill } from './recognition/useReadFill.ts'

export interface DoneValues {
  date: string
  odometer: number | null
  createExpense: boolean
  amount: number | null
  currency: string | null
  /** Photos uploaded in the dialog: the logged expense's photos (none when no expense is logged). */
  photoIds: string[]
}

/** The fields photos can fill in (a dashboard: the odometer; an invoice: the cost and the day), and which read value goes into each. */
type FillableField = 'date' | 'odometer' | 'amount' | 'currency'
const READ_INTO: Record<FillableField, ReadingFieldName> = { date: 'DATE', odometer: 'ODOMETER', amount: 'TOTAL', currency: 'CURRENCY' }

const today = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/**
 * "Mark as done": starts the next interval from the day and odometer entered and, by default, logs what it cost as a normal expense (so
 * all the expense rules apply). The odometer is required when distance counts for this item. Photos (the dashboard, the invoice) go
 * with the logged expense, and when photo reading is on they fill in what the user has not typed yet.
 */
export function RecurringDoneDialog({
  trigger,
  vehicle,
  item,
  onSubmit,
  onCloseAutoFocus,
}: {
  trigger: ReactNode
  vehicle: { id: string; units: { distance: DistanceUnit } }
  item: { title: string; kind: RecurrenceKind }
  onSubmit: (values: DoneValues) => Promise<unknown>
  /** Where the focus goes when the dialog closes (by default back to the trigger); for a trigger that is gone by then, see `VehicleCard`. */
  onCloseAutoFocus?: (event: Event) => void
}) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const { queue, saving, submit, reset } = usePhotoSession(vehicle.id, 'expense', open)
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
        maxWidth="450px"
        // Closing while it is being saved would lose track of it.
        onEscapeKeyDown={(e) => saving && e.preventDefault()}
        onInteractOutside={(e) => saving && e.preventDefault()}
        onCloseAutoFocus={onCloseAutoFocus}
      >
        <Dialog.Title>{t('recurring.doneTitle', { title: item.title })}</Dialog.Title>
        <Dialog.Description size="2" mb="4">
          {t('recurring.doneDescription')}
        </Dialog.Description>
        {open && (
          <DoneForm
            vehicle={vehicle}
            usesDistance={item.kind !== 'TIME'}
            photosBusy={queue.busy || queue.failed > 0}
            read={mergeReadings(drafts.readings.values())}
            readingDone={drafts.done}
            explanation={drafts.explanation}
            gallery={<PhotoGallery kind="expenses" photos={[]} queue={queue} readingIds={drafts.pending} disabled={saving} onChanged={() => undefined} />}
            onSubmit={async (values) => {
              // Without an expense the photos have nothing to belong to: they are not sent, and closing deletes them.
              const saved = await submit(async (photoIds) => {
                await onSubmit({ ...values, photoIds })
              }, !values.createExpense)
              if (saved) {
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

function DoneForm(props: {
  vehicle: { id: string; units: { distance: DistanceUnit } }
  usesDistance: boolean
  photosBusy: boolean
  read: ReadValues
  readingDone: boolean
  explanation: ReadingExplanation
  gallery: ReactNode
  onSubmit: (values: Omit<DoneValues, 'photoIds'>) => Promise<unknown>
}) {
  const { t } = useTranslation()
  const defaults = useQuery(LogDefaultsDocument, { variables: { vehicleId: props.vehicle.id }, fetchPolicy: 'network-only' })
  const d = defaults.data?.logDefaults
  if (!d) return defaults.error ? <ErrorMessage error={defaults.error} /> : <Text as="p" role="status">{t('app.loading')}</Text>
  return <DoneFields {...props} defaults={d} />
}

function DoneFields({
  vehicle,
  usesDistance,
  photosBusy,
  read,
  readingDone,
  explanation,
  gallery,
  defaults: d,
  onSubmit,
}: {
  vehicle: { id: string; units: { distance: DistanceUnit } }
  usesDistance: boolean
  photosBusy: boolean
  /** What the photos showed so far. */
  read: ReadValues
  readingDone: boolean
  /** Why the photos gave less than they might have. */
  explanation: ReadingExplanation
  gallery: ReactNode
  defaults: NonNullable<LogDefaultsQuery['logDefaults']>
  onSubmit: (values: Omit<DoneValues, 'photoIds'>) => Promise<unknown>
}) {
  const { t } = useTranslation()
  const [logExpense, setLogExpense] = useState(true)
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)
  const switchId = useId()
  const last = d.lastOdometer != null && d.lastDate ? { value: d.lastOdometer, date: d.lastDate } : null
  const odometerLabel = useOdometerLabel(vehicle.units.distance, !usesDistance)
  const { distance } = useFormat()
  const labels: Record<FillableField, string> = {
    date: t('recurring.doneDate'),
    odometer: odometerLabel,
    amount: t('recurring.amount'),
    currency: t('recurring.currency'),
  }
  const fill = useReadFill({ date: today(), odometer: d.lastOdometer?.toString() ?? '', amount: '', currency: d.currency ?? '' }, READ_INTO, read, labels, readingDone, {
    ...explanation,
    last: last ? distance(last.value, vehicle.units.distance) : undefined,
  })
  const note = (field: FillableField) => (
    <ReadNote filled={fill.isFilled(field)} offered={fill.offered(field)} field={labels[field]} onUse={() => fill.use(field)} />
  )

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    const text = (name: string) => String(form.get(name) ?? '').trim()
    setBusy(true)
    setError(undefined)
    try {
      await onSubmit({
        date: text('date'),
        odometer: text('odometer') === '' ? null : Number(text('odometer')),
        createExpense: logExpense,
        amount: logExpense ? (parseDecimal(text('amount')) ?? null) : null,
        currency: logExpense ? text('currency').toUpperCase() : null,
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
        <OdometerField
          unit={vehicle.units.distance}
          last={last}
          optional={!usesDistance}
          value={fill.values.odometer}
          onChange={(v) => fill.change('odometer', v)}
          extra={note('odometer')}
        />
        <Flex align="center" gap="3">
          <Switch id={switchId} checked={logExpense} onCheckedChange={setLogExpense} size="3" />
          <Text as="label" size="2" weight="bold" htmlFor={switchId}>
            {t('recurring.logExpense')}
          </Text>
        </Flex>
        {logExpense && (
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
        )}
        {gallery}
        {!logExpense && (
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
