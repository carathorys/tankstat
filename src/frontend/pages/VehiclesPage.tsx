import { useMutation } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Link from '@mui/material/Link'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { Pencil, Trash2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link as RouterLink } from 'react-router'
import { ConfirmDialog } from '../components/ConfirmDialog.tsx'
import { IconAction } from '../components/IconAction.tsx'
import { UserChip } from '../components/UserAvatar.tsx'
import { VehiclePicture } from '../components/VehiclePicture.tsx'
import {
  AddVehicleDocument,
  DeleteVehicleDocument,
  RestoreVehicleDocument,
  UpdateVehicleDocument,
  VehiclesDocument,
  type VehicleSortField,
  type VehiclesQuery,
  type VehiclesQueryVariables,
} from '../gql/generated.ts'
import { ServerGrid, type GridColumn } from '../grid/ServerGrid.tsx'
import { useLeavingRows } from '../grid/useLeavingRows.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { ErrorMessage } from '../messages.tsx'
import { useToast } from '../toast/toastContext.ts'
import { VehicleFormDialog } from '../VehicleFormDialog.tsx'

type Row = VehiclesQuery['vehicles'][number]

const refetch = { refetchQueries: ['Vehicles', 'Trash', 'Welcome'], awaitRefetchQueries: true }

export function VehiclesPage() {
  const { t } = useTranslation()
  const { undoable } = useToast()
  usePageTitle(t('vehicles.title'))
  const [addVehicle] = useMutation(AddVehicleDocument, refetch)
  const [updateVehicle] = useMutation(UpdateVehicleDocument, refetch)
  const [deleteVehicle] = useMutation(DeleteVehicleDocument, refetch)
  const [restoreVehicle] = useMutation(RestoreVehicleDocument, refetch)
  const { leaving, leave } = useLeavingRows()
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
          <Stack direction="row" sx={{ alignItems: 'center', gap: 1.5 }}>
            <VehiclePicture url={r.pictureUrl} name={r.name} width={40} />
            <Link component={RouterLink} to={`/vehicles/${r.id}`} aria-label={t('vehicles.open', { name: r.name })} sx={{ fontWeight: 'fontWeightMedium' }}>
              {r.name}
            </Link>
          </Stack>
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
      await leave(vehicle.id, () => deleteVehicle({ variables: { id: vehicle.id } }))
      undoable(t('toast.vehicleTrashed', { name: vehicle.name }), () => restoreVehicle({ variables: { id: vehicle.id } }))
    } catch (e) {
      setActionError(e)
    }
  }

  return (
    <section aria-labelledby="page-title">
      <Typography id="page-title" component="h1" variant="h3" sx={{ mb: 2 }}>
        {t('vehicles.title')}
      </Typography>
      {actionError !== undefined && <ErrorMessage error={actionError} />}
      <ServerGrid
        gridId="vehicles"
        caption={t('vehicles.title')}
        query={VehiclesDocument}
        select={(d) => ({ rows: d.vehicles, total: d.vehicleCount })}
        rowKey={(r) => r.id}
        columns={columns}
        defaultSort={{ column: 'name', direction: 'ASC' }}
        emptyText={t('vehicles.empty')}
        leaving={leaving}
        toolbar={() => <VehicleFormDialog trigger={<Button size="large">{t('vehicles.add')}</Button>} onSubmit={(input) => addVehicle({ variables: { input } })} />}
        actions={(v) =>
          v.canEdit ? (
            <Stack direction="row" sx={{ gap: 1, justifyContent: 'flex-end' }}>
              <VehicleFormDialog
                vehicleId={v.id}
                trigger={
                  <IconAction size="large" tone="primary" label={t('vehicles.editAria', { name: v.name })}>
                    <Pencil size={16} aria-hidden />
                  </IconAction>
                }
                onSubmit={(input) => updateVehicle({ variables: { input: { ...input, id: v.id } } })}
              />
              <ConfirmDialog
                trigger={
                  <IconAction size="large" tone="error" label={t('vehicles.deleteAria', { name: v.name })}>
                    <Trash2 size={16} aria-hidden />
                  </IconAction>
                }
                title={t('vehicles.trashTitle', { name: v.name })}
                description={t('vehicles.trashDescription')}
                confirmLabel={t('vehicles.trashConfirm')}
                onConfirm={() => void moveToTrash(v)}
              />
            </Stack>
          ) : (
            <Typography variant="body2" sx={{ color: 'text.secondary' }}>
              {t('vehicles.viewOnly')}
            </Typography>
          )
        }
      />
    </section>
  )
}
