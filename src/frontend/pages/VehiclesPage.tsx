import { useMutation } from '@apollo/client/react'
import { AlertDialog, Button, Flex, Heading, Text } from '@radix-ui/themes'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  AddVehicleDocument,
  DeleteVehicleDocument,
  UpdateVehicleDocument,
  VehiclesDocument,
  type VehiclesQuery,
  type VehiclesQueryVariables,
} from '../gql/generated.ts'
import { DataGrid, type GridColumn } from '../grid/DataGrid.tsx'
import { ErrorMessage } from '../messages.tsx'
import { VehicleFormDialog } from '../VehicleFormDialog.tsx'

type Row = VehiclesQuery['vehicles'][number]

const refetch = { refetchQueries: ['Vehicles', 'Trash'], awaitRefetchQueries: true }

export function VehiclesPage() {
  const { t } = useTranslation()
  const [addVehicle] = useMutation(AddVehicleDocument, refetch)
  const [updateVehicle] = useMutation(UpdateVehicleDocument, refetch)
  const [deleteVehicle] = useMutation(DeleteVehicleDocument, refetch)
  const [actionError, setActionError] = useState<unknown>()
  const none = t('common.none')

  const columns: GridColumn<Row, VehiclesQueryVariables>[] = [
    { id: 'name', label: 'columns.name', hideable: false, mobile: true, sortField: 'NAME', cell: (r) => r.name },
    { id: 'licensePlate', label: 'columns.licensePlate', include: 'withLicensePlate', sortField: 'LICENSE_PLATE', cell: (r) => r.licensePlate ?? none },
    { id: 'fuelType', label: 'columns.fuelType', include: 'withFuelType', mobile: true, sortField: 'FUEL_TYPE', cell: (r) => (r.fuelType ? t(`fuel.${r.fuelType}`) : none) },
    { id: 'owner', label: 'columns.owner', include: 'withOwner', sortField: 'OWNER', cell: (r) => r.ownerName ?? none },
    { id: 'refuelings', label: 'columns.refuelings', include: 'withRefuelings', sortField: 'REFUELING_COUNT', cell: (r) => r.refuelingCount ?? none },
  ]

  async function moveToTrash(vehicle: Row) {
    setActionError(undefined)
    try {
      await deleteVehicle({ variables: { id: vehicle.id } })
    } catch (e) {
      setActionError(e)
    }
  }

  return (
    <section>
      <Heading mb="4">{t('vehicles.title')}</Heading>
      {actionError !== undefined && <ErrorMessage error={actionError} />}
      <DataGrid
        gridId="vehicles"
        query={VehiclesDocument}
        select={(d) => ({ rows: d.vehicles, total: d.vehicleCount })}
        rowKey={(r) => r.id}
        columns={columns}
        defaultSort={{ field: 'NAME', direction: 'ASC' }}
        emptyText={t('vehicles.empty')}
        toolbar={() => (
          <VehicleFormDialog trigger={<Button>{t('vehicles.add')}</Button>} onSubmit={(input) => addVehicle({ variables: { input } })} />
        )}
        actions={(v) =>
          v.canEdit ? (
            <Flex gap="2" justify="end">
              <VehicleFormDialog
                vehicleId={v.id}
                trigger={
                  <Button size="1" variant="soft" aria-label={t('vehicles.editAria', { name: v.name })}>
                    {t('vehicles.edit')}
                  </Button>
                }
                onSubmit={(input) => updateVehicle({ variables: { input: { ...input, id: v.id } } })}
              />
              <AlertDialog.Root>
                <AlertDialog.Trigger>
                  <Button size="1" variant="soft" color="red" aria-label={t('vehicles.deleteAria', { name: v.name })}>
                    {t('vehicles.delete')}
                  </Button>
                </AlertDialog.Trigger>
                <AlertDialog.Content maxWidth="450px">
                  <AlertDialog.Title>{t('vehicles.trashTitle', { name: v.name })}</AlertDialog.Title>
                  <AlertDialog.Description size="2">{t('vehicles.trashDescription')}</AlertDialog.Description>
                  <Flex gap="3" mt="4" justify="end">
                    <AlertDialog.Cancel>
                      <Button variant="soft" color="gray">
                        {t('common.cancel')}
                      </Button>
                    </AlertDialog.Cancel>
                    <AlertDialog.Action>
                      <Button color="red" onClick={() => moveToTrash(v)}>
                        {t('vehicles.trashConfirm')}
                      </Button>
                    </AlertDialog.Action>
                  </Flex>
                </AlertDialog.Content>
              </AlertDialog.Root>
            </Flex>
          ) : (
            <Text size="2" color="gray">
              {t('vehicles.viewOnly')}
            </Text>
          )
        }
      />
    </section>
  )
}
