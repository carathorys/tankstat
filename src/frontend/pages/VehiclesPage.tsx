import { useMutation } from '@apollo/client/react'
import { Button, Flex, Heading, IconButton, Link as RadixLink, Text } from '@radix-ui/themes'
import { Pencil, Trash2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { ConfirmDialog } from '../components/ConfirmDialog.tsx'
import { UserChip } from '../components/UserAvatar.tsx'
import { VehiclePicture } from '../components/VehiclePicture.tsx'
import {
  AddVehicleDocument,
  DeleteVehicleDocument,
  UpdateVehicleDocument,
  VehiclesDocument,
  type VehicleSortField,
  type VehiclesQuery,
  type VehiclesQueryVariables,
} from '../gql/generated.ts'
import { DataGrid, type GridColumn } from '../grid/DataGrid.tsx'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { ErrorMessage } from '../messages.tsx'
import { VehicleFormDialog } from '../VehicleFormDialog.tsx'

type Row = VehiclesQuery['vehicles'][number]

const refetch = { refetchQueries: ['Vehicles', 'Trash', 'Welcome'], awaitRefetchQueries: true }

export function VehiclesPage() {
  const { t } = useTranslation()
  usePageTitle(t('vehicles.title'))
  const [addVehicle] = useMutation(AddVehicleDocument, refetch)
  const [updateVehicle] = useMutation(UpdateVehicleDocument, refetch)
  const [deleteVehicle] = useMutation(DeleteVehicleDocument, refetch)
  const [actionError, setActionError] = useState<unknown>()

  const columns = useMemo<GridColumn<Row, VehiclesQueryVariables, VehicleSortField>[]>(() => {
    const none = t('common.none')
    return [
      {
        id: 'name',
        label: 'columns.name',
        hideable: false,
        mobile: true,
        sortField: 'NAME',
        cell: (r) => (
          <Flex align="center" gap="3">
            <VehiclePicture url={r.pictureUrl} name={r.name} width={40} />
            <RadixLink asChild weight="medium" aria-label={t('vehicles.open', { name: r.name })}>
              <Link to={`/vehicles/${r.id}`}>{r.name}</Link>
            </RadixLink>
          </Flex>
        ),
      },
      { id: 'licensePlate', label: 'columns.licensePlate', include: 'withLicensePlate', sortField: 'LICENSE_PLATE', cell: (r) => r.licensePlate ?? none },
      { id: 'fuelType', label: 'columns.fuelType', include: 'withFuelType', mobile: true, sortField: 'FUEL_TYPE', cell: (r) => (r.fuelType ? t(`fuel.${r.fuelType}`) : none) },
      { id: 'owner', label: 'columns.owner', include: 'withOwner', sortField: 'OWNER', cell: (r) => (r.owner ? <UserChip user={r.owner} /> : none) },
      { id: 'refuelings', label: 'columns.refuelings', include: 'withRefuelings', sortField: 'REFUELING_COUNT', cell: (r) => r.refuelingCount ?? none },
    ]
  }, [t])

  async function moveToTrash(vehicle: Row) {
    setActionError(undefined)
    try {
      await deleteVehicle({ variables: { id: vehicle.id } })
    } catch (e) {
      setActionError(e)
    }
  }

  return (
    <section aria-labelledby="page-title">
      <Heading id="page-title" mb="4">
        {t('vehicles.title')}
      </Heading>
      {actionError !== undefined && <ErrorMessage error={actionError} />}
      <DataGrid
        gridId="vehicles"
        caption={t('vehicles.title')}
        query={VehiclesDocument}
        select={(d) => ({ rows: d.vehicles, total: d.vehicleCount })}
        rowKey={(r) => r.id}
        columns={columns}
        defaultSort={{ column: 'name', direction: 'ASC' }}
        emptyText={t('vehicles.empty')}
        toolbar={() => <VehicleFormDialog trigger={<Button size="3">{t('vehicles.add')}</Button>} onSubmit={(input) => addVehicle({ variables: { input } })} />}
        actions={(v) =>
          v.canEdit ? (
            <Flex gap="2" justify="end">
              <VehicleFormDialog
                vehicleId={v.id}
                trigger={
                  <IconButton size="3" variant="soft" aria-label={t('vehicles.editAria', { name: v.name })}>
                    <Pencil size={16} aria-hidden />
                  </IconButton>
                }
                onSubmit={(input) => updateVehicle({ variables: { input: { ...input, id: v.id } } })}
              />
              <ConfirmDialog
                trigger={
                  <IconButton size="3" variant="soft" color="red" aria-label={t('vehicles.deleteAria', { name: v.name })}>
                    <Trash2 size={16} aria-hidden />
                  </IconButton>
                }
                title={t('vehicles.trashTitle', { name: v.name })}
                description={t('vehicles.trashDescription')}
                confirmLabel={t('vehicles.trashConfirm')}
                onConfirm={() => void moveToTrash(v)}
              />
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
