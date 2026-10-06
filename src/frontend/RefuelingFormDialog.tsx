import { useQuery } from '@apollo/client/react'
import * as RadixForm from '@radix-ui/react-form'
import { Button, Dialog, Flex, Switch, Text, TextArea, TextField } from '@radix-ui/themes'
import { useId, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { OdometerField } from './components/OdometerField.tsx'
import { PhotoGallery } from './components/PhotoGallery.tsx'
import { PhotosLeftOut } from './components/PhotosLeftOut.tsx'
import type { Saved } from './components/usePhotoQueue.ts'
import { useOdometerLabel } from './components/useOdometerLabel.ts'
import { usePhotoSession } from './components/usePhotoSession.ts'
import { Field } from './forms.tsx'
import { LogDefaultsDocument, RefuelingDetailsDocument, type DistanceUnit, type LogValue, type ReadingFieldName, type ReviewState, type VolumeUnit } from './gql/generated.ts'
import { parseDecimal, useFormat } from './i18n/format.ts'
import { ErrorMessage } from './messages.tsx'
import { ReadNote } from './recognition/ReadNote.tsx'
import { ReadingProblems } from './recognition/ReadingProblems.tsx'
import type { ReadingExplanation } from './recognition/readingIssues.ts'
import { mergeReadings, type ReadValues } from './recognition/readValues.ts'
import { filledFields } from './recognition/review.ts'
import { ReviewCallout } from './recognition/ReviewState.tsx'
import { useDraftReadings } from './recognition/useDraftReadings.ts'
import { useReadFill } from './recognition/useReadFill.ts'
import { keepAmountsInStep } from './refuelingAmounts.ts'

/** Volume, total cost and odometer are null only when they were left for a photo that is still being read. */
export interface RefuelingValues {
  date: string
  volume: number | null
  totalCost: number | null
  currency: string
  odometer: number | null
  isFullTank: boolean
  note: string | null
}

interface Initial extends Omit<RefuelingValues, 'volume' | 'totalCost' | 'odometer' | 'currency'> {
  volume?: number | null
  totalCost?: number | null
  currency: string | null
  odometer?: number | null
  reviewState?: ReviewState
  filledFromPhoto?: LogValue[]
}

const today = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/**
 * The fields of a new log that photos can fill in, and which read value goes into each. The unit price is only a help for entering the
 * amounts (it is never saved): volume, unit price and total follow each other (`keepAmountsInStep`).
 */
type FillableField = 'date' | 'volume' | 'unitPrice' | 'totalCost' | 'currency' | 'odometer'
const READ_INTO: Record<FillableField, ReadingFieldName> = {
  date: 'DATE',
  volume: 'VOLUME',
  unitPrice: 'UNIT_PRICE',
  totalCost: 'TOTAL',
  currency: 'CURRENCY',
  odometer: 'ODOMETER',
}
/** The values the server fills in from photos after saving, and their fields. */
const WAITS_FOR: Record<LogValue, FillableField> = { ODOMETER: 'odometer', VOLUME: 'volume', TOTAL: 'totalCost' }

/**
 * Add a log (no `refuelingId`) or edit one. A new log starts from sensible values: today, the vehicle's latest odometer
 * reading (as a hint, so the number is never entered by accident) and the currency of the latest log. Volume and odometer are
 * entered in the vehicle's own units; the server checks the reading against the neighbouring ones. Of volume, unit price and total,
 * any two give the third (the one typed longest ago gives way when all three are filled). When photo reading is on, the
 * photos of a new log are read on the server and fill in what the user has not typed yet; while one is still being read, volume,
 * total and odometer may be left empty: the server fills them in later and marks the log for review (editing it then checks it).
 */
export function RefuelingFormDialog({
  trigger,
  vehicle,
  refuelingId,
  onSubmit,
}: {
  trigger: ReactNode
  vehicle: { id: string; units: { distance: DistanceUnit; volume: VolumeUnit } }
  refuelingId?: string
  /** `photoIds`: the drafts uploaded for a new log (always empty when editing: a saved log takes its photos right away). */
  onSubmit: (values: RefuelingValues, photoIds: string[]) => Promise<Saved>
}) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const editing = refuelingId !== undefined
  const { queue, leftOut, saving, submit, reset } = usePhotoSession(vehicle.id, editing ? undefined : 'refueling')
  const drafts = useDraftReadings(queue.uploaded, open && !editing)
  const details = useQuery(RefuelingDetailsDocument, { variables: { id: refuelingId ?? '' }, skip: !editing || !open, fetchPolicy: 'network-only' })
  const defaults = useQuery(LogDefaultsDocument, { variables: { vehicleId: vehicle.id }, skip: !open, fetchPolicy: 'network-only' })
  const error = details.error ?? defaults.error
  const ready = defaults.data && (!editing || details.data?.refueling)

  const existing = details.data?.refueling
  const logDefaults = defaults.data?.logDefaults
  const lastReading = logDefaults?.lastOdometer != null && logDefaults.lastDate ? { value: logDefaults.lastOdometer, date: logDefaults.lastDate } : null
  const initial: Initial = existing
    ? { ...existing, currency: existing.currency ?? defaults.data?.logDefaults?.currency ?? '', note: existing.note ?? null }
    : { date: today(), currency: defaults.data?.logDefaults?.currency ?? '', isFullTank: true, note: null }

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
        // Closing while the log is being saved would lose track of it.
        onEscapeKeyDown={(e) => saving && e.preventDefault()}
        onInteractOutside={(e) => saving && e.preventDefault()}
      >
        <Dialog.Title>{editing ? t('refuelings.dialogEdit') : t('refuelings.dialogAdd')}</Dialog.Title>
        <Dialog.Description size="2" mb="4">
          {editing ? t('refuelings.dialogEditDescription') : t('refuelings.dialogAddDescription')}
        </Dialog.Description>
        {error && <ErrorMessage error={error} />}
        {!error && !ready && <Text as="p" role="status">{t('app.loading')}</Text>}
        {editing && details.data && !existing && <ErrorMessage>{t('errors.refueling.notFound')}</ErrorMessage>}
        {leftOut > 0 && <PhotosLeftOut count={leftOut} />}
        {ready && leftOut === 0 && (
          <RefuelingForm
            initial={initial}
            units={vehicle.units}
            editing={editing}
            last={lastReading}
            photosBusy={queue.busy || queue.failed > 0}
            read={mergeReadings(drafts.readings.values())}
            readingDone={drafts.done}
            explanation={drafts.explanation}
            // A new log may leave values to a photo that is being read; a saved one still waiting for its photos may stay so.
            mayWait={editing ? existing?.reviewState === 'AWAITING_PHOTOS' : drafts.pending.length > 0}
            gallery={
              <PhotoGallery
                kind="refuelings"
                logId={refuelingId}
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

function RefuelingForm({
  initial,
  units,
  editing,
  last,
  gallery,
  photosBusy,
  read,
  readingDone,
  explanation,
  mayWait,
  onSubmit,
}: {
  initial: Initial
  units: { distance: DistanceUnit; volume: VolumeUnit }
  editing: boolean
  last: { value: number; date: string } | null
  gallery: ReactNode
  /** Chosen photos are still being prepared: saving now would leave them out. */
  photosBusy: boolean
  /** What the photos showed so far. */
  read: ReadValues
  readingDone: boolean
  /** Why the photos gave less than they might have. */
  explanation: ReadingExplanation
  /** A photo is still being read: values it can provide may be left empty. */
  mayWait: boolean
  onSubmit: (values: RefuelingValues) => Promise<unknown>
}) {
  const { t } = useTranslation()
  const odometerLabel = useOdometerLabel(units.distance)
  const { distance } = useFormat()
  const labels: Record<FillableField, string> = {
    date: t('refuelings.fields.date'),
    volume: t('refuelings.fields.volume', { unit: t(`units.volumeShort.${units.volume}`) }),
    unitPrice: t('refuelings.fields.unitPrice', { unit: t(`units.volumeShort.${units.volume}`) }),
    totalCost: t('refuelings.fields.cost'),
    currency: t('refuelings.fields.currency'),
    odometer: odometerLabel,
  }
  const fill = useReadFill(
    {
      date: initial.date,
      volume: initial.volume?.toString() ?? '',
      unitPrice: '', // worked out from the other two (keepAmountsInStep)
      totalCost: initial.totalCost?.toString() ?? '',
      currency: initial.currency ?? '',
      odometer: initial.odometer?.toString() ?? '',
    },
    READ_INTO,
    read,
    labels,
    readingDone,
    { ...explanation, last: last ? distance(last.value, units.distance) : undefined },
    keepAmountsInStep,
  )
  const fromPhoto = filledFields(initial.filledFromPhoto, WAITS_FOR)
  const waits = new Set(Object.values(WAITS_FOR))
  const optional = (field: FillableField) => mayWait && waits.has(field)
  const note = (field: FillableField) => (
    <ReadNote
      filled={fill.isFilled(field) || fromPhoto.has(field)}
      offered={fill.offered(field)}
      waiting={optional(field) && fill.values[field].trim() === ''}
      field={labels[field]}
      onUse={() => fill.use(field)}
    />
  )
  const calculated = (field: FillableField) => (fill.isCalculated(field) ? t('refuelings.hints.calculated') : undefined)
  const [full, setFull] = useState(initial.isFullTank)
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)
  const switchId = useId()
  const decimalInvalid = { message: t('forms.numberInvalid'), test: (v: string) => v !== '' && parseDecimal(v) === undefined }

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    const note = String(form.get('note') ?? '').trim()
    const text = (name: string) => String(form.get(name) ?? '').trim()
    setBusy(true)
    setError(undefined)
    try {
      await onSubmit({
        date: text('date'),
        volume: text('volume') === '' ? null : parseDecimal(text('volume'))!,
        totalCost: text('totalCost') === '' ? null : parseDecimal(text('totalCost'))!,
        currency: text('currency').toUpperCase(),
        odometer: text('odometer') === '' ? null : Number(text('odometer')),
        isFullTank: full,
        note: note === '' ? null : note,
      })
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <RadixForm.Root onSubmit={submit}>
      <ReviewCallout state={initial.reviewState} />
      <Flex direction="column" gap="3">
        <Field name="date" label={labels.date} required extra={note('date')}>
          <TextField.Root type="date" required max={today()} value={fill.values.date} onChange={(e) => fill.change('date', e.target.value)} />
        </Field>
        <Flex gap="3" wrap="wrap">
          <Flex direction="column" style={{ flex: '1 1 8rem' }}>
            <Field
              name="volume"
              label={labels.volume}
              hint={calculated('volume')}
              required={!optional('volume')}
              invalid={decimalInvalid}
              extra={note('volume')}
            >
              <TextField.Root required={!optional('volume')} inputMode="decimal" autoComplete="off" value={fill.values.volume} onChange={(e) => fill.change('volume', e.target.value)} />
            </Field>
          </Flex>
          <Flex direction="column" style={{ flex: '1 1 8rem' }}>
            <Field name="unitPrice" label={labels.unitPrice} hint={calculated('unitPrice')} invalid={decimalInvalid} extra={note('unitPrice')}>
              <TextField.Root inputMode="decimal" autoComplete="off" value={fill.values.unitPrice} onChange={(e) => fill.change('unitPrice', e.target.value)} />
            </Field>
          </Flex>
        </Flex>
        <Flex gap="3" wrap="wrap">
          <Flex direction="column" style={{ flex: '2 1 8rem' }}>
            <Field
              name="totalCost"
              label={labels.totalCost}
              hint={calculated('totalCost')}
              required={!optional('totalCost')}
              invalid={decimalInvalid}
              extra={note('totalCost')}
            >
              <TextField.Root required={!optional('totalCost')} inputMode="decimal" autoComplete="off" value={fill.values.totalCost} onChange={(e) => fill.change('totalCost', e.target.value)} />
            </Field>
          </Flex>
          <Flex direction="column" style={{ flex: '1 1 6rem' }}>
            <Field
              name="currency"
              label={labels.currency}
              required
              hint={t('refuelings.hints.currency')}
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
        <OdometerField
          unit={units.distance}
          last={last}
          required={!optional('odometer')}
          value={fill.values.odometer}
          onChange={(v) => fill.change('odometer', v)}
          extra={note('odometer')}
        />
        <Flex align="center" gap="3">
          <Switch id={switchId} checked={full} onCheckedChange={setFull} size="3" />
          <Flex direction="column">
            <Text as="label" size="2" weight="bold" htmlFor={switchId}>
              {t('refuelings.fields.fullTank')}
            </Text>
            <Text size="1" color="gray">
              {t('refuelings.hints.fullTankHelp')}
            </Text>
          </Flex>
        </Flex>
        <Field name="note" label={t('refuelings.fields.note')}>
          <TextArea maxLength={500} rows={2} defaultValue={initial.note ?? ''} />
        </Field>
        {gallery}
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
            <Button disabled={busy || photosBusy}>{editing ? t('refuelings.save') : t('refuelings.saveAdd')}</Button>
          </RadixForm.Submit>
        </Flex>
      </Flex>
    </RadixForm.Root>
  )
}
