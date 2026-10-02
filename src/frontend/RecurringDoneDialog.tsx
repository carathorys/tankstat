import { useQuery } from '@apollo/client/react'
import * as RadixForm from '@radix-ui/react-form'
import { Button, Dialog, Flex, Switch, Text, TextField } from '@radix-ui/themes'
import { useId, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { OdometerField } from './components/OdometerField.tsx'
import { Field } from './forms.tsx'
import { LogDefaultsDocument, type DistanceUnit, type RecurrenceKind } from './gql/generated.ts'
import { parseDecimal } from './i18n/format.ts'
import { ErrorMessage } from './messages.tsx'

export interface DoneValues {
  date: string
  odometer: number | null
  createExpense: boolean
  amount: number | null
  currency: string | null
}

const today = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/**
 * "Mark as done": starts the next interval from the day and odometer entered and, by default, logs what it cost as a normal expense (so
 * all the expense rules apply). The odometer is required when distance counts for this item.
 */
export function RecurringDoneDialog({
  trigger,
  vehicle,
  item,
  onSubmit,
}: {
  trigger: ReactNode
  vehicle: { id: string; units: { distance: DistanceUnit } }
  item: { title: string; kind: RecurrenceKind }
  onSubmit: (values: DoneValues) => Promise<unknown>
}) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)

  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger>{trigger}</Dialog.Trigger>
      <Dialog.Content maxWidth="450px">
        <Dialog.Title>{t('recurring.doneTitle', { title: item.title })}</Dialog.Title>
        <Dialog.Description size="2" mb="4">
          {t('recurring.doneDescription')}
        </Dialog.Description>
        {open && (
          <DoneForm
            vehicle={vehicle}
            usesDistance={item.kind !== 'TIME'}
            onSubmit={async (values) => {
              await onSubmit(values)
              setOpen(false)
            }}
          />
        )}
      </Dialog.Content>
    </Dialog.Root>
  )
}

function DoneForm({
  vehicle,
  usesDistance,
  onSubmit,
}: {
  vehicle: { id: string; units: { distance: DistanceUnit } }
  usesDistance: boolean
  onSubmit: (values: DoneValues) => Promise<unknown>
}) {
  const { t } = useTranslation()
  const defaults = useQuery(LogDefaultsDocument, { variables: { vehicleId: vehicle.id }, fetchPolicy: 'network-only' })
  const [logExpense, setLogExpense] = useState(true)
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)
  const switchId = useId()
  const d = defaults.data?.logDefaults
  const last = d?.lastOdometer != null && d.lastDate ? { value: d.lastOdometer, date: d.lastDate } : null

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

  if (!d) return defaults.error ? <ErrorMessage error={defaults.error} /> : <Text as="p" role="status">{t('app.loading')}</Text>

  return (
    <RadixForm.Root onSubmit={submit}>
      <Flex direction="column" gap="3">
        <Field name="date" label={t('recurring.doneDate')} required>
          <TextField.Root type="date" required max={today()} defaultValue={today()} />
        </Field>
        <OdometerField unit={vehicle.units.distance} last={last} optional={!usesDistance} defaultValue={d.lastOdometer ?? undefined} />
        <Flex align="center" gap="3">
          <Switch id={switchId} checked={logExpense} onCheckedChange={setLogExpense} size="3" />
          <Text as="label" size="2" weight="bold" htmlFor={switchId}>
            {t('recurring.logExpense')}
          </Text>
        </Flex>
        {logExpense && (
          <Flex gap="3" wrap="wrap">
            <Flex direction="column" style={{ flex: '2 1 8rem' }}>
              <Field name="amount" label={t('recurring.amount')} required invalid={{ message: t('forms.numberInvalid'), test: (v) => v !== '' && parseDecimal(v) === undefined }}>
                <TextField.Root required inputMode="decimal" autoComplete="off" />
              </Field>
            </Flex>
            <Flex direction="column" style={{ flex: '1 1 6rem' }}>
              <Field
                name="currency"
                label={t('recurring.currency')}
                required
                invalid={{ message: t('errors.money.currencyInvalid'), test: (v) => v !== '' && !/^[A-Za-z]{3}$/.test(v.trim()) }}
              >
                <TextField.Root required maxLength={3} autoComplete="off" style={{ textTransform: 'uppercase' }} defaultValue={d.currency ?? ''} />
              </Field>
            </Flex>
          </Flex>
        )}
        {error !== undefined && <ErrorMessage error={error} />}
        <Flex gap="3" justify="end">
          <Dialog.Close>
            <Button type="button" variant="soft" color="gray">
              {t('common.cancel')}
            </Button>
          </Dialog.Close>
          <RadixForm.Submit asChild>
            <Button disabled={busy}>{t('recurring.doneSave')}</Button>
          </RadixForm.Submit>
        </Flex>
      </Flex>
    </RadixForm.Root>
  )
}
