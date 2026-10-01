import { useQuery } from '@apollo/client/react'
import * as RadixForm from '@radix-ui/react-form'
import { Button, Dialog, Flex, Select, Text, TextField } from '@radix-ui/themes'
import { useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { VehicleDetailsDocument, type FuelType } from './gql/generated.ts'
import { ErrorMessage } from './messages.tsx'
import { FUEL_TYPES } from './vehicles.ts'

export interface VehicleValues {
  name: string
  licensePlate: string | null
  fuelType: FuelType
}

/**
 * Create (no `vehicleId`) or edit (with `vehicleId`) a vehicle. Opens from its `trigger` button and closes after a
 * successful save. An edited vehicle is loaded when the dialog opens, because the grid may not show every column.
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
  const details = useQuery(VehicleDetailsDocument, {
    variables: { id: vehicleId ?? '' },
    skip: !editing || !open,
    fetchPolicy: 'network-only',
  })
  const loaded = details.data?.vehicle

  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger>{trigger}</Dialog.Trigger>
      <Dialog.Content maxWidth="450px">
        <Dialog.Title>{editing ? t('vehicles.dialogEdit') : t('vehicles.dialogAdd')}</Dialog.Title>
        <Dialog.Description size="2" mb="4">
          {editing ? t('vehicles.dialogEditDescription') : t('vehicles.dialogAddDescription')}
        </Dialog.Description>
        {editing && details.error && <ErrorMessage error={details.error} />}
        {editing && !details.error && !details.data && <Text as="p">{t('app.loading')}</Text>}
        {editing && details.data && !loaded && <ErrorMessage>{t('errors.vehicle.notFound')}</ErrorMessage>}
        {(!editing || loaded) && (
          <VehicleForm
            initial={loaded ?? undefined}
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

function VehicleForm({
  initial,
  editing,
  onSubmit,
}: {
  initial?: VehicleValues
  editing: boolean
  onSubmit: (values: VehicleValues) => Promise<unknown>
}) {
  const { t } = useTranslation()
  const [fuel, setFuel] = useState<FuelType>(initial?.fuelType ?? 'PETROL')
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    const plate = String(form.get('licensePlate') ?? '').trim()
    setBusy(true)
    setError(undefined)
    try {
      await onSubmit({ name: String(form.get('name') ?? ''), licensePlate: plate === '' ? null : plate, fuelType: fuel })
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <RadixForm.Root onSubmit={submit}>
      <Flex direction="column" gap="3">
        <RadixForm.Field name="name" asChild>
          <Flex direction="column" gap="1">
            <RadixForm.Label asChild>
              <Text as="label" size="2" weight="bold">
                {t('fields.name')}
              </Text>
            </RadixForm.Label>
            <RadixForm.Control asChild>
              <TextField.Root required defaultValue={initial?.name} autoFocus />
            </RadixForm.Control>
            <RadixForm.Message match="valueMissing" asChild>
              <Text size="1" color="red">
                {t('forms.required', { field: t('fields.name') })}
              </Text>
            </RadixForm.Message>
          </Flex>
        </RadixForm.Field>
        <RadixForm.Field name="licensePlate" asChild>
          <Flex direction="column" gap="1">
            <RadixForm.Label asChild>
              <Text as="label" size="2" weight="bold">
                {t('fields.licensePlateOptional')}
              </Text>
            </RadixForm.Label>
            <RadixForm.Control asChild>
              <TextField.Root defaultValue={initial?.licensePlate ?? ''} />
            </RadixForm.Control>
          </Flex>
        </RadixForm.Field>
        <Flex align="center" gap="2">
          <Text size="2" weight="bold">
            {t('fields.fuel')}
          </Text>
          <Select.Root value={fuel} onValueChange={(v) => setFuel(v as FuelType)}>
            <Select.Trigger aria-label={t('fields.fuel')} />
            <Select.Content>
              {FUEL_TYPES.map((f) => (
                <Select.Item key={f} value={f}>
                  {t(`fuel.${f}`)}
                </Select.Item>
              ))}
            </Select.Content>
          </Select.Root>
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
