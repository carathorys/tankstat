import { useQuery } from '@apollo/client/react'
import * as RadixForm from '@radix-ui/react-form'
import { Button, Callout, Dialog, Flex, Text, TextField } from '@radix-ui/themes'
import { Info } from 'lucide-react'
import { useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { LabeledSelect } from './components/UnitSelect.tsx'
import { Field } from './forms.tsx'
import {
  VehicleDefaultsDocument,
  VehicleDetailsDocument,
  type DistanceUnit,
  type FuelType,
  type VolumeUnit,
} from './gql/generated.ts'
import { ErrorMessage } from './messages.tsx'
import { DISTANCE_UNITS, FUEL_TYPES, VOLUME_UNITS } from './vehicles.ts'

export interface VehicleValues {
  name: string
  licensePlate: string | null
  fuelType: FuelType
  units: { distance: DistanceUnit; volume: VolumeUnit }
}

interface Initial {
  name: string
  licensePlate?: string | null
  fuelType: FuelType
  units: { distance: DistanceUnit; volume: VolumeUnit }
  refuelingCount: number
}

/**
 * Create (no `vehicleId`) or edit (with `vehicleId`) a vehicle. Opens from its `trigger` button and closes after a
 * successful save. An edited vehicle is loaded when the dialog opens, because the grid may not show every column.
 * The units start from the installation's defaults and are locked once the vehicle has logs (numbers are never converted).
 */
export function VehicleFormDialog({
  trigger,
  vehicleId,
  onSubmit,
}: {
  trigger: ReactNode
  vehicleId?: string
  onSubmit: (values: VehicleValues) => Promise<unknown>
}) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const editing = vehicleId !== undefined
  const details = useQuery(VehicleDetailsDocument, { variables: { id: vehicleId ?? '' }, skip: !editing || !open, fetchPolicy: 'network-only' })
  const defaults = useQuery(VehicleDefaultsDocument, { skip: editing || !open })
  const loaded = details.data?.vehicle
  const initial: Initial | undefined = loaded ?? (defaults.data ? { name: '', fuelType: 'PETROL', refuelingCount: 0, units: { distance: defaults.data.vehicleDefaults.distanceUnit, volume: defaults.data.vehicleDefaults.volumeUnit } } : undefined)
  const error = details.error ?? defaults.error

  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger>{trigger}</Dialog.Trigger>
      <Dialog.Content maxWidth="450px">
        <Dialog.Title>{editing ? t('vehicles.dialogEdit') : t('vehicles.dialogAdd')}</Dialog.Title>
        <Dialog.Description size="2" mb="4">
          {editing ? t('vehicles.dialogEditDescription') : t('vehicles.dialogAddDescription')}
        </Dialog.Description>
        {error && <ErrorMessage error={error} />}
        {!error && !initial && !(editing && details.data) && <Text as="p" role="status">{t('app.loading')}</Text>}
        {editing && details.data && !loaded && <ErrorMessage>{t('errors.vehicle.notFound')}</ErrorMessage>}
        {initial && (
          <VehicleForm
            initial={initial}
            editing={editing}
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

function VehicleForm({ initial, editing, onSubmit }: { initial: Initial; editing: boolean; onSubmit: (values: VehicleValues) => Promise<unknown> }) {
  const { t } = useTranslation()
  const [fuel, setFuel] = useState<FuelType>(initial.fuelType)
  const [distance, setDistance] = useState(initial.units.distance)
  const [volume, setVolume] = useState(initial.units.volume)
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)
  const locked = initial.refuelingCount > 0

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    const plate = String(form.get('licensePlate') ?? '').trim()
    setBusy(true)
    setError(undefined)
    try {
      await onSubmit({ name: String(form.get('name') ?? ''), licensePlate: plate === '' ? null : plate, fuelType: fuel, units: { distance, volume } })
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <RadixForm.Root onSubmit={submit}>
      <Flex direction="column" gap="3">
        <Field name="name" label={t('fields.name')} required>
          <TextField.Root required defaultValue={initial.name} autoFocus />
        </Field>
        <Field name="licensePlate" label={t('fields.licensePlateOptional')}>
          <TextField.Root defaultValue={initial.licensePlate ?? ''} />
        </Field>
        <LabeledSelect label={t('fields.fuel')} value={fuel} onChange={setFuel} options={FUEL_TYPES.map((f) => ({ value: f, label: t(`fuel.${f}`) }))} />
        <Flex asChild direction="column" gap="2">
          <fieldset style={{ border: 0, padding: 0, margin: 0 }}>
            <legend>
              <Text size="2" weight="bold">
                {t('units.label')}
              </Text>
            </legend>
            <Flex gap="3" wrap="wrap">
              <LabeledSelect label={t('units.distanceLabel')} value={distance} onChange={setDistance} disabled={locked} options={DISTANCE_UNITS.map((u) => ({ value: u, label: t(`units.distance.${u}`) }))} />
              <LabeledSelect label={t('units.volumeLabel')} value={volume} onChange={setVolume} disabled={locked} options={VOLUME_UNITS.map((u) => ({ value: u, label: t(`units.volume.${u}`) }))} />
            </Flex>
            {locked ? (
              <Callout.Root size="1" color="gray">
                <Callout.Icon>
                  <Info size={16} aria-hidden />
                </Callout.Icon>
                <Callout.Text>{t('vehicles.unitsInUse')}</Callout.Text>
              </Callout.Root>
            ) : (
              <Text size="1" color="gray">
                {t('units.hint')}
              </Text>
            )}
          </fieldset>
        </Flex>
        {error !== undefined && <ErrorMessage error={error} />}
        <Flex gap="3" justify="end">
          <Dialog.Close>
            <Button type="button" variant="soft" color="gray">
              {t('common.cancel')}
            </Button>
          </Dialog.Close>
          <RadixForm.Submit asChild>
            <Button disabled={busy}>{editing ? t('vehicles.save') : t('vehicles.add')}</Button>
          </RadixForm.Submit>
        </Flex>
      </Flex>
    </RadixForm.Root>
  )
}
