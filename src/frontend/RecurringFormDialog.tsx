import { useQuery } from '@apollo/client/react'
import * as RadixForm from '@radix-ui/react-form'
import { Button, Dialog, Flex, Text, TextArea, TextField } from '@radix-ui/themes'
import { useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { LabeledSelect } from './components/UnitSelect.tsx'
import { Field } from './forms.tsx'
import { LogDefaultsDocument, type DistanceUnit, type RecurrenceKind } from './gql/generated.ts'
import { ErrorMessage } from './messages.tsx'

export interface RecurringValues {
  title: string
  category: string | null
  note: string | null
  kind: RecurrenceKind
  intervalMonths: number | null
  intervalDistance: number | null
  lastDoneDate: string
  lastDoneOdometer: number | null
  warnDays: number
  warnDistance: number
}

const KINDS: RecurrenceKind[] = ['TIME', 'ODOMETER', 'COMBINED']
const today = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
const whole = (text: string) => (text.trim() === '' ? null : Number(text.trim()))
const wholeInvalid = (message: string) => ({ message, test: (v: string) => v !== '' && !/^\d+$/.test(v.trim()) })

/**
 * Add a recurring expense (no `initial`) or edit one. Time, distance or both ("whichever comes first"); the fields of the kind that is
 * not chosen are hidden. A new one starts counting today, at the vehicle's current odometer (its latest reading, to be corrected if it
 * is off); the day can be changed too, for something that was done earlier.
 */
export function RecurringFormDialog({
  trigger,
  vehicleId,
  unit,
  initial,
  onSubmit,
}: {
  trigger: ReactNode
  vehicleId: string
  unit: DistanceUnit
  initial?: RecurringValues
  onSubmit: (values: RecurringValues) => Promise<unknown>
}) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const editing = initial !== undefined

  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger>{trigger}</Dialog.Trigger>
      <Dialog.Content maxWidth="480px">
        <Dialog.Title>{editing ? t('recurring.dialogEdit') : t('recurring.dialogAdd')}</Dialog.Title>
        <Dialog.Description size="2" mb="4">
          {editing ? t('recurring.dialogEditDescription') : t('recurring.dialogAddDescription')}
        </Dialog.Description>
        {/* Mounted only while open, so every opening starts from the current values. */}
        {open && (
          <WithCurrentOdometer vehicleId={vehicleId} needed={!editing}>
            {(currentOdometer) => (
              <RecurringForm
                unit={unit}
                initial={initial}
                currentOdometer={currentOdometer}
                onSubmit={async (values) => {
                  await onSubmit(values)
                  setOpen(false)
                }}
              />
            )}
          </WithCurrentOdometer>
        )}
      </Dialog.Content>
    </Dialog.Root>
  )
}

/** Loads the vehicle's latest odometer reading (only when adding) before the form is shown, so the field can start from it. */
function WithCurrentOdometer({ vehicleId, needed, children }: { vehicleId: string; needed: boolean; children: (current: number | null) => ReactNode }) {
  const { t } = useTranslation()
  const defaults = useQuery(LogDefaultsDocument, { variables: { vehicleId }, skip: !needed, fetchPolicy: 'network-only' })
  if (!needed) return children(null)
  if (defaults.error) return <ErrorMessage error={defaults.error} />
  if (!defaults.data) {
    return (
      <Text as="p" role="status">
        {t('app.loading')}
      </Text>
    )
  }
  return children(defaults.data.logDefaults?.lastOdometer ?? null)
}

function RecurringForm({
  unit,
  initial,
  currentOdometer,
  onSubmit,
}: {
  unit: DistanceUnit
  initial?: RecurringValues
  currentOdometer: number | null
  onSubmit: (values: RecurringValues) => Promise<unknown>
}) {
  const { t } = useTranslation()
  const [kind, setKind] = useState<RecurrenceKind>(initial?.kind ?? 'COMBINED')
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)
  const adding = initial === undefined
  const usesTime = kind !== 'ODOMETER'
  const usesDistance = kind !== 'TIME'
  const unitName = t(`units.distance.${unit}`).toLowerCase()
  const invalidNumber = t('forms.numberInvalid')

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    const text = (name: string) => String(form.get(name) ?? '').trim()
    setBusy(true)
    setError(undefined)
    try {
      await onSubmit({
        title: text('title'),
        category: text('category') || null,
        note: text('note') || null,
        kind,
        intervalMonths: usesTime ? whole(text('intervalMonths')) : null,
        intervalDistance: usesDistance ? whole(text('intervalDistance')) : null,
        lastDoneDate: text('lastDoneDate'),
        lastDoneOdometer: usesDistance ? whole(text('lastDoneOdometer')) : null,
        // a limit the kind does not use is not shown, so what was stored stays
        warnDays: usesTime ? Number(text('warnDays')) : (initial?.warnDays ?? 30),
        warnDistance: usesDistance ? Number(text('warnDistance')) : (initial?.warnDistance ?? 500),
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
        <Field name="title" label={t('recurring.fields.title')} required>
          <TextField.Root required maxLength={120} autoComplete="off" defaultValue={initial?.title ?? ''} />
        </Field>
        <Field name="category" label={t('recurring.fields.category')}>
          <TextField.Root maxLength={60} autoComplete="off" defaultValue={initial?.category ?? ''} />
        </Field>
        <LabeledSelect
          label={t('recurring.fields.kind')}
          value={kind}
          options={KINDS.map((k) => ({ value: k, label: t(`recurring.kind.${k}`) }))}
          onChange={setKind}
        />
        {usesTime && (
          <Field name="intervalMonths" label={t('recurring.fields.months')} required invalid={wholeInvalid(invalidNumber)}>
            <TextField.Root required inputMode="numeric" autoComplete="off" defaultValue={initial?.intervalMonths?.toString() ?? '12'} />
          </Field>
        )}
        {usesDistance && (
          <Field name="intervalDistance" label={t('recurring.fields.distance', { unit: unitName })} required invalid={wholeInvalid(invalidNumber)}>
            <TextField.Root required inputMode="numeric" autoComplete="off" defaultValue={initial?.intervalDistance?.toString() ?? ''} />
          </Field>
        )}
        <Field name="lastDoneDate" label={t('recurring.fields.lastDone')} hint={t(adding ? 'recurring.hints.startToday' : 'recurring.hints.lastDone')} required>
          <TextField.Root type="date" required max={today()} defaultValue={initial?.lastDoneDate ?? today()} />
        </Field>
        {usesDistance && (
          <Field
            name="lastDoneOdometer"
            label={t('recurring.fields.lastOdometer', { unit: unitName })}
            hint={adding ? t(currentOdometer === null ? 'recurring.hints.noReading' : 'recurring.hints.currentOdometer') : t('recurring.hints.lastOdometer')}
            required
            invalid={wholeInvalid(t('errors.odometer.negative'))}
          >
            <TextField.Root required inputMode="numeric" autoComplete="off" defaultValue={(adding ? currentOdometer : initial.lastDoneOdometer)?.toString() ?? ''} />
          </Field>
        )}
        <Flex gap="3" wrap="wrap">
          {usesTime && (
            <Flex direction="column" style={{ flex: '1 1 10rem' }}>
              <Field name="warnDays" label={t('recurring.fields.warnDays')} required invalid={wholeInvalid(invalidNumber)}>
                <TextField.Root required inputMode="numeric" autoComplete="off" defaultValue={(initial?.warnDays ?? 30).toString()} />
              </Field>
            </Flex>
          )}
          {usesDistance && (
            <Flex direction="column" style={{ flex: '1 1 10rem' }}>
              <Field name="warnDistance" label={t('recurring.fields.warnDistance', { unit: unitName })} required invalid={wholeInvalid(invalidNumber)}>
                <TextField.Root required inputMode="numeric" autoComplete="off" defaultValue={(initial?.warnDistance ?? 500).toString()} />
              </Field>
            </Flex>
          )}
        </Flex>
        <Field name="note" label={t('recurring.fields.note')}>
          <TextArea maxLength={500} rows={2} defaultValue={initial?.note ?? ''} />
        </Field>
        {error !== undefined && <ErrorMessage error={error} />}
        <Flex gap="3" justify="end">
          <Dialog.Close>
            <Button type="button" variant="soft" color="gray">
              {t('common.cancel')}
            </Button>
          </Dialog.Close>
          <RadixForm.Submit asChild>
            <Button disabled={busy}>{initial ? t('recurring.save') : t('recurring.saveAdd')}</Button>
          </RadixForm.Submit>
        </Flex>
      </Flex>
    </RadixForm.Root>
  )
}
