import { useQuery } from '@apollo/client/react'
import * as RadixForm from '@radix-ui/react-form'
import { Button, Dialog, Flex, Switch, Text, TextArea, TextField } from '@radix-ui/themes'
import { useId, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { OdometerField } from './components/OdometerField.tsx'
import { PhotoGallery } from './components/PhotoGallery.tsx'
import { usePhotoQueue, type Saved } from './components/usePhotoQueue.ts'
import { Field } from './forms.tsx'
import { LogDefaultsDocument, RefuelingDetailsDocument, type DistanceUnit, type VolumeUnit } from './gql/generated.ts'
import { parseDecimal } from './i18n/format.ts'
import { useErrorText } from './i18n/errors.ts'
import { ErrorMessage } from './messages.tsx'

export interface RefuelingValues {
  date: string
  volume: number
  totalCost: number
  currency: string
  odometer: number
  isFullTank: boolean
  note: string | null
}

interface Initial extends Omit<RefuelingValues, 'volume' | 'totalCost' | 'odometer'> {
  volume?: number
  totalCost?: number
  odometer?: number
}

const today = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/**
 * Add a log (no `refuelingId`) or edit one. A new log starts from sensible values: today, the vehicle's latest odometer
 * reading (as a hint, so the number is never entered by accident) and the currency of the latest log. Volume and odometer are
 * entered in the vehicle's own units; the server checks the reading against the neighbouring ones.
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
  onSubmit: (values: RefuelingValues) => Promise<Saved>
}) {
  const { t } = useTranslation()
  const errorText = useErrorText()
  const [open, setOpen] = useState(false)
  const queue = usePhotoQueue()
  // Set when a new log was saved but some of its photos could not be sent: the dialog then shows that log's photos instead of the form.
  const [savedId, setSavedId] = useState<string>()
  const [uploadFailure, setUploadFailure] = useState<unknown>()
  const editing = refuelingId !== undefined
  const logId = refuelingId ?? savedId
  const details = useQuery(RefuelingDetailsDocument, { variables: { id: logId ?? '' }, skip: logId === undefined || !open, fetchPolicy: 'network-only' })
  const defaults = useQuery(LogDefaultsDocument, { variables: { vehicleId: vehicle.id }, skip: !open, fetchPolicy: 'network-only' })
  const error = details.error ?? defaults.error
  const ready = defaults.data && (!editing || details.data?.refueling)

  const existing = details.data?.refueling
  const logDefaults = defaults.data?.logDefaults
  const lastReading = logDefaults?.lastOdometer != null && logDefaults.lastDate ? { value: logDefaults.lastOdometer, date: logDefaults.lastDate } : null
  const initial: Initial | undefined = existing
    ? { ...existing, note: existing.note ?? null }
    : { date: today(), currency: defaults.data?.logDefaults?.currency ?? '', isFullTank: true, note: null }

  return (
    <Dialog.Root
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) {
          queue.clear()
          setSavedId(undefined)
          setUploadFailure(undefined)
        }
      }}
    >
      <Dialog.Trigger>{trigger}</Dialog.Trigger>
      <Dialog.Content maxWidth="450px">
        <Dialog.Title>{editing ? t('refuelings.dialogEdit') : t('refuelings.dialogAdd')}</Dialog.Title>
        <Dialog.Description size="2" mb="4">
          {editing ? t('refuelings.dialogEditDescription') : t('refuelings.dialogAddDescription')}
        </Dialog.Description>
        {error && <ErrorMessage error={error} />}
        {!error && !ready && <Text as="p" role="status">{t('app.loading')}</Text>}
        {editing && details.data && !existing && <ErrorMessage>{t('errors.refueling.notFound')}</ErrorMessage>}
        {savedId !== undefined && (
          <Flex direction="column" gap="3">
            <ErrorMessage>{`${t('photos.partialFailure')} ${errorText(uploadFailure)}`}</ErrorMessage>
            <PhotoGallery kind="refuelings" logId={savedId} photos={existing?.photos ?? []} queue={queue} onChanged={() => details.refetch()} />
            <Flex justify="end">
              <Dialog.Close>
                <Button type="button">{t('photos.done')}</Button>
              </Dialog.Close>
            </Flex>
          </Flex>
        )}
        {ready && savedId === undefined && (
          <RefuelingForm
            initial={initial}
            units={vehicle.units}
            editing={editing}
            last={lastReading}
            gallery={<PhotoGallery kind="refuelings" logId={refuelingId} photos={existing?.photos ?? []} queue={queue} onChanged={() => details.refetch()} />}
            onSubmit={async (values) => {
              const saved = await onSubmit(values)
              if (!editing && saved && queue.items.length > 0) {
                const failure = await queue.uploadAll('refuelings', saved.id)
                if (failure !== undefined) {
                  setUploadFailure(failure)
                  setSavedId(saved.id)
                  return
                }
              }
              setOpen(false)
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
  onSubmit,
}: {
  initial: Initial
  units: { distance: DistanceUnit; volume: VolumeUnit }
  editing: boolean
  last: { value: number; date: string } | null
  gallery: ReactNode
  onSubmit: (values: RefuelingValues) => Promise<unknown>
}) {
  const { t } = useTranslation()
  const [full, setFull] = useState(initial.isFullTank)
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)
  const switchId = useId()
  const decimalInvalid = { message: t('forms.numberInvalid'), test: (v: string) => v !== '' && parseDecimal(v) === undefined }

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    const note = String(form.get('note') ?? '').trim()
    setBusy(true)
    setError(undefined)
    try {
      await onSubmit({
        date: String(form.get('date')),
        volume: parseDecimal(String(form.get('volume')))!,
        totalCost: parseDecimal(String(form.get('totalCost')))!,
        currency: String(form.get('currency') ?? '').trim().toUpperCase(),
        odometer: Number(String(form.get('odometer')).trim()),
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
      <Flex direction="column" gap="3">
        <Field name="date" label={t('refuelings.fields.date')} required>
          <TextField.Root type="date" required max={today()} defaultValue={initial.date} />
        </Field>
        <Field name="volume" label={t('refuelings.fields.volume', { unit: t(`units.volumeShort.${units.volume}`) })} required invalid={decimalInvalid}>
          <TextField.Root required inputMode="decimal" autoComplete="off" defaultValue={initial.volume?.toString() ?? ''} />
        </Field>
        <Flex gap="3" wrap="wrap">
          <Flex direction="column" style={{ flex: '2 1 8rem' }}>
            <Field name="totalCost" label={t('refuelings.fields.cost')} required invalid={decimalInvalid}>
              <TextField.Root required inputMode="decimal" autoComplete="off" defaultValue={initial.totalCost?.toString() ?? ''} />
            </Field>
          </Flex>
          <Flex direction="column" style={{ flex: '1 1 6rem' }}>
            <Field
              name="currency"
              label={t('refuelings.fields.currency')}
              required
              hint={t('refuelings.hints.currency')}
              invalid={{ message: t('errors.money.currencyInvalid'), test: (v) => v !== '' && !/^[A-Za-z]{3}$/.test(v.trim()) }}
            >
              <TextField.Root required maxLength={3} autoComplete="off" style={{ textTransform: 'uppercase' }} defaultValue={initial.currency} />
            </Field>
          </Flex>
        </Flex>
        <OdometerField unit={units.distance} defaultValue={initial.odometer} last={last} />
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
        {error !== undefined && <ErrorMessage error={error} />}
        <Flex gap="3" justify="end">
          <Dialog.Close>
            <Button type="button" variant="soft" color="gray">
              {t('common.cancel')}
            </Button>
          </Dialog.Close>
          <RadixForm.Submit asChild>
            <Button disabled={busy}>{editing ? t('refuelings.save') : t('refuelings.saveAdd')}</Button>
          </RadixForm.Submit>
        </Flex>
      </Flex>
    </RadixForm.Root>
  )
}
