import { useQuery } from '@apollo/client/react'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { Info } from 'lucide-react'
import { useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Loading } from './components/Loading.tsx'
import { LabeledSelect } from './components/UnitSelect.tsx'
import { DialogButtons, DialogCancel, DialogFrame } from './dialogs/DialogFrame.tsx'
import { DialogTrigger } from './dialogs/DialogTrigger.tsx'
import { useDialogState } from './dialogs/useDialogState.ts'
import { Field } from './forms/Field.tsx'
import { FieldInput } from './forms/FieldInput.tsx'
import { Form } from './forms/Form.tsx'
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
  const [open, setOpen] = useDialogState()
  const editing = vehicleId !== undefined
  const details = useQuery(VehicleDetailsDocument, { variables: { id: vehicleId ?? '' }, skip: !editing || !open, fetchPolicy: 'network-only' })
  const defaults = useQuery(VehicleDefaultsDocument, { skip: editing || !open })
  const loaded = details.data?.vehicle
  const initial: Initial | undefined = loaded ?? (defaults.data ? { name: '', fuelType: 'PETROL', refuelingCount: 0, units: { distance: defaults.data.vehicleDefaults.distanceUnit, volume: defaults.data.vehicleDefaults.volumeUnit } } : undefined)
  const error = details.error ?? defaults.error

  return (
    <>
      <DialogTrigger trigger={trigger} open={open} onOpen={() => setOpen(true)} />
      <DialogFrame
        open={open}
        onClose={() => setOpen(false)}
        title={editing ? t('vehicles.dialogEdit') : t('vehicles.dialogAdd')}
        description={editing ? t('vehicles.dialogEditDescription') : t('vehicles.dialogAddDescription')}
      >
        {error && <ErrorMessage error={error} />}
        {!error && !initial && !(editing && details.data) && <Loading />}
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
      </DialogFrame>
    </>
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
    <Form onSubmit={submit}>
      <Stack sx={{ gap: 1.5 }}>
        <Field name="name" label={t('fields.name')} required>
          <FieldInput defaultValue={initial.name} autoFocus />
        </Field>
        <Field name="licensePlate" label={t('fields.licensePlateOptional')}>
          <FieldInput defaultValue={initial.licensePlate ?? ''} />
        </Field>
        <LabeledSelect label={t('fields.fuel')} value={fuel} onChange={setFuel} options={FUEL_TYPES.map((f) => ({ value: f, label: t(`fuel.${f}`) }))} />
        <Box component="fieldset" sx={{ border: 0, p: 0, m: 0, minWidth: 0 }}>
          <Typography component="legend" variant="body2" sx={{ fontWeight: 700, p: 0, mb: 1 }}>
            {t('units.label')}
          </Typography>
          <Stack sx={{ gap: 1 }}>
            <Stack direction="row" sx={{ gap: 1.5, flexWrap: 'wrap' }}>
              <LabeledSelect label={t('units.distanceLabel')} value={distance} onChange={setDistance} disabled={locked} options={DISTANCE_UNITS.map((u) => ({ value: u, label: t(`units.distance.${u}`) }))} />
              <LabeledSelect label={t('units.volumeLabel')} value={volume} onChange={setVolume} disabled={locked} options={VOLUME_UNITS.map((u) => ({ value: u, label: t(`units.volume.${u}`) }))} />
            </Stack>
            {locked ? (
              <Alert color="neutral" icon={<Info size={16} aria-hidden />}>
                {t('vehicles.unitsInUse')}
              </Alert>
            ) : (
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                {t('units.hint')}
              </Typography>
            )}
          </Stack>
        </Box>
        {error !== undefined && <ErrorMessage error={error} />}
        <DialogButtons>
          <DialogCancel />
          <Button type="submit" disabled={busy}>
            {editing ? t('vehicles.save') : t('vehicles.add')}
          </Button>
        </DialogButtons>
      </Stack>
    </Form>
  )
}
