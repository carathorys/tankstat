import { useQuery } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import { useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Loading } from './components/Loading.tsx'
import { LabeledSelect } from './components/UnitSelect.tsx'
import { DialogButtons, DialogCancel, DialogFrame } from './dialogs/DialogFrame.tsx'
import { DialogTrigger } from './dialogs/DialogTrigger.tsx'
import { useClientId } from './dialogs/useClientId.ts'
import { useDialogState } from './dialogs/useDialogState.ts'
import { todayIso } from './forms/dates.ts'
import { Field } from './forms/Field.tsx'
import { FieldDate } from './forms/FieldDate.tsx'
import { FieldInput } from './forms/FieldInput.tsx'
import { Form } from './forms/Form.tsx'
import { LogDefaultsDocument, VehicleDefaultsDocument, type DistanceUnit, type RecurrenceKind } from './gql/generated.ts'
import { ErrorMessage } from './messages.tsx'
import { useToast } from './toast/toastContext.ts'
import type { ChangeEdit, MergeInfo } from './dialogs/changeEdit.ts'
import { useMergeFields } from './dialogs/useMergeFields.tsx'
import { outbox } from './offline/outbox.ts'

export interface RecurringValues {
  /** A new schedule only: the id it gets on the server, the same for every Save of one opening of the dialog (see `useClientId`). */
  id?: string
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
  initial: saved,
  change,
  onSubmit,
}: {
  trigger: ReactNode
  vehicleId: string
  unit: DistanceUnit
  initial?: RecurringValues
  /** Edits a change instead of a schedule (Waiting to sync; see `ChangeEdit`): its values stand for `initial`. */
  change?: ChangeEdit<RecurringValues>
  onSubmit: (values: RecurringValues) => Promise<unknown>
}) {
  const { t } = useTranslation()
  const { toast } = useToast()
  const [open, setOpen] = useDialogState()
  const initial = change ? (change.initial as RecurringValues) : saved
  const editing = initial !== undefined
  const clientId = useClientId(open)

  return (
    <>
      <DialogTrigger trigger={trigger} open={open} onOpen={() => setOpen(true)} />
      <DialogFrame
        open={open}
        onClose={() => setOpen(false)}
        maxWidth={480}
        title={change?.title ?? (editing ? t('recurring.dialogEdit') : t('recurring.dialogAdd'))}
        description={editing ? t('recurring.dialogEditDescription') : t('recurring.dialogAddDescription')}
      >
        {/* Mounted only while open, so every opening starts from the current values. */}
        {open && (
          <WithStartValues vehicleId={vehicleId} needed={!editing}>
            {(start) => (
              <RecurringForm
                unit={unit}
                initial={initial}
                start={start}
                submitLabel={change?.submitLabel}
                merge={change?.merge}
                onSubmit={async (values) => {
                  await onSubmit(editing ? values : { ...values, id: clientId })
                  setOpen(false)
                  if (change) return // the caller tells what came of it
                  // Kept on the device for the server (the server was out of reach): the toast says so.
                  toast(outbox.markOf('recurring', (editing ? initial?.id : clientId) ?? '') ? t('toast.savedOnDevice') : t('toast.saved'))
                }}
              />
            )}
          </WithStartValues>
        )}
      </DialogFrame>
    </>
  )
}

/** What a new schedule starts from: the vehicle's current odometer and the instance's default warnings. */
interface StartValues {
  odometer: number | null
  warnDays: number
  warnDistance: number
}

/**
 * Loads the vehicle's latest odometer reading and the instance defaults (only when adding) before the form is shown, so the fields can
 * start from them.
 */
function WithStartValues({ vehicleId, needed, children }: { vehicleId: string; needed: boolean; children: (start: StartValues | null) => ReactNode }) {
  const log = useQuery(LogDefaultsDocument, { variables: { vehicleId }, skip: !needed, fetchPolicy: 'network-only' })
  const instance = useQuery(VehicleDefaultsDocument, { skip: !needed })
  if (!needed) return children(null)
  const error = log.error ?? instance.error
  if (error) return <ErrorMessage error={error} />
  if (!log.data || !instance.data) return <Loading />
  const { recurringWarnDays, recurringWarnDistance } = instance.data.vehicleDefaults
  return children({ odometer: log.data.logDefaults?.lastOdometer ?? null, warnDays: recurringWarnDays, warnDistance: recurringWarnDistance })
}

/** What a text field shows of a value: nothing for none. */
const text = (value: unknown) => (value == null ? '' : String(value))

function RecurringForm({
  unit,
  initial,
  start,
  submitLabel,
  merge,
  onSubmit,
}: {
  unit: DistanceUnit
  initial?: RecurringValues
  start: StartValues | null
  /** The Save button's text, when it is not the add or edit one. */
  submitLabel?: string
  /** Merging a parked edit with what is on the server now: what to say (and offer) under each field. */
  merge?: MergeInfo
  onSubmit: (values: RecurringValues) => Promise<unknown>
}) {
  const { t } = useTranslation()
  const merging = useMergeFields(merge)
  /** Where an uncontrolled field starts: a value taken for it while merging, else its own (`merging.key` starts it again). */
  const own = (field: string, value: unknown) => text(merging.value(field, value))
  const [kind, setKind] = useState<RecurrenceKind>(initial?.kind ?? 'COMBINED')
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)
  const adding = initial === undefined
  const usesTime = kind !== 'ODOMETER'
  const usesDistance = kind !== 'TIME'
  const unitName = t(`units.distance.${unit}`).toLowerCase()
  const invalidNumber = t('forms.numberInvalid')
  const currentOdometer = start?.odometer ?? null
  // editing keeps the schedule's own warnings; a new one starts from the instance defaults
  const warnDays = initial?.warnDays ?? start?.warnDays ?? 0
  const warnDistance = initial?.warnDistance ?? start?.warnDistance ?? 0

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
        warnDays: usesTime ? Number(text('warnDays')) : warnDays,
        warnDistance: usesDistance ? Number(text('warnDistance')) : warnDistance,
      })
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Form onSubmit={submit}>
      <Stack sx={{ gap: 1.5 }}>
        <Field name="title" label={t('recurring.fields.title')} required extra={merging.note('title', t('recurring.fields.title'))}>
          <FieldInput maxLength={120} autoComplete="off" key={merging.key('title')} defaultValue={own('title', initial?.title)} />
        </Field>
        <Field name="category" label={t('recurring.fields.category')} extra={merging.note('category', t('recurring.fields.category'))}>
          <FieldInput maxLength={60} autoComplete="off" key={merging.key('category')} defaultValue={own('category', initial?.category)} />
        </Field>
        <LabeledSelect label={t('recurring.fields.kind')} value={kind} options={KINDS.map((k) => ({ value: k, label: t(`recurring.kind.${k}`) }))} onChange={setKind} />
        {merging.note('kind', t('recurring.fields.kind'), (value) => setKind(value as RecurrenceKind))}
        {usesTime && (
          <Field name="intervalMonths" label={t('recurring.fields.months')} required invalid={wholeInvalid(invalidNumber)} extra={merging.note('intervalMonths', t('recurring.fields.months'))}>
            <FieldInput inputMode="numeric" autoComplete="off" key={merging.key('intervalMonths')} defaultValue={own('intervalMonths', initial?.intervalMonths ?? 12)} />
          </Field>
        )}
        {usesDistance && (
          <Field
            name="intervalDistance"
            label={t('recurring.fields.distance', { unit: unitName })}
            required
            invalid={wholeInvalid(invalidNumber)}
            extra={merging.note('intervalDistance', t('recurring.fields.distance', { unit: unitName }))}
          >
            <FieldInput inputMode="numeric" autoComplete="off" key={merging.key('intervalDistance')} defaultValue={own('intervalDistance', initial?.intervalDistance)} />
          </Field>
        )}
        <Field
          name="lastDoneDate"
          label={t('recurring.fields.lastDone')}
          hint={t(adding ? 'recurring.hints.startToday' : 'recurring.hints.lastDone')}
          required
          extra={merging.note('lastDoneDate', t('recurring.fields.lastDone'))}
        >
          <FieldDate disableFuture key={merging.key('lastDoneDate')} defaultValue={own('lastDoneDate', initial?.lastDoneDate ?? todayIso())} />
        </Field>
        {usesDistance && (
          <Field
            name="lastDoneOdometer"
            label={t('recurring.fields.lastOdometer', { unit: unitName })}
            hint={adding ? t(currentOdometer === null ? 'recurring.hints.noReading' : 'recurring.hints.currentOdometer') : t('recurring.hints.lastOdometer')}
            required
            invalid={wholeInvalid(t('errors.odometer.negative'))}
            extra={merging.note('lastDoneOdometer', t('recurring.fields.lastOdometer', { unit: unitName }))}
          >
            <FieldInput inputMode="numeric" autoComplete="off" key={merging.key('lastDoneOdometer')} defaultValue={own('lastDoneOdometer', adding ? currentOdometer : initial.lastDoneOdometer)} />
          </Field>
        )}
        <Stack direction="row" sx={{ gap: 1.5, flexWrap: 'wrap', '& > *': { flex: '1 1 10rem' } }}>
          {usesTime && (
            <Box>
              <Field name="warnDays" label={t('recurring.fields.warnDays')} required invalid={wholeInvalid(invalidNumber)} extra={merging.note('warnDays', t('recurring.fields.warnDays'))}>
                <FieldInput inputMode="numeric" autoComplete="off" key={merging.key('warnDays')} defaultValue={own('warnDays', warnDays)} />
              </Field>
            </Box>
          )}
          {usesDistance && (
            <Box>
              <Field
                name="warnDistance"
                label={t('recurring.fields.warnDistance', { unit: unitName })}
                required
                invalid={wholeInvalid(invalidNumber)}
                extra={merging.note('warnDistance', t('recurring.fields.warnDistance', { unit: unitName }))}
              >
                <FieldInput inputMode="numeric" autoComplete="off" key={merging.key('warnDistance')} defaultValue={own('warnDistance', warnDistance)} />
              </Field>
            </Box>
          )}
        </Stack>
        <Field name="note" label={t('recurring.fields.note')} extra={merging.note('note', t('recurring.fields.note'))}>
          <FieldInput multiline minRows={2} maxLength={500} key={merging.key('note')} defaultValue={own('note', initial?.note)} />
        </Field>
        {error !== undefined && <ErrorMessage error={error} />}
        <DialogButtons>
          <DialogCancel />
          <Button type="submit" disabled={busy}>
            {submitLabel ?? (initial ? t('recurring.save') : t('recurring.saveAdd'))}
          </Button>
        </DialogButtons>
      </Stack>
    </Form>
  )
}
