import { useApolloClient, useMutation } from '@apollo/client/react'
import Alert from '@mui/material/Alert'
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
import { outbox, type ChangeDraft } from '../offline/outbox.ts'
import { undoTrash } from '../offline/submitChange.ts'
import { KeepButton } from '../components/KeepButton.tsx'
import { useConnectivity } from '../offline/useConnectivity.ts'
import { useSubmitChange } from '../offline/useLogChange.ts'
import { uuidV4 } from '../offline/uuid.ts'
import { useToast } from '../toast/toastContext.ts'
import { VehicleFormDialog } from '../VehicleFormDialog.tsx'

type Row = VehiclesQuery['vehicles'][number]

const refetch = { refetchQueries: ['Vehicles', 'Trash', 'Welcome'], awaitRefetchQueries: true }

export function VehiclesPage() {
  const { t } = useTranslation()
  const { toast, undoable } = useToast()
  usePageTitle(t('vehicles.title'))
  const [addVehicle] = useMutation(AddVehicleDocument, refetch)
  const [updateVehicle] = useMutation(UpdateVehicleDocument, refetch)
  const [deleteVehicle] = useMutation(DeleteVehicleDocument, refetch)
  const [restoreVehicle] = useMutation(RestoreVehicleDocument, refetch)
  // Like the home page and the Details tab: kept on the device while the server is out of reach, sent as always otherwise.
  const submit = useSubmitChange()
  const client = useApolloClient()
  const change = (vehicleId: string, draft: Omit<ChangeDraft, 'entity' | 'vehicleId' | 'id'>) => ({ id: uuidV4(), entity: 'vehicles' as const, vehicleId, ...draft })
  const { reachable } = useConnectivity()
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
      // Kept on the device while the server is out of reach: the row stays, marked "to be removed", and Undo takes the change back. One
      // added on this device and never sent is simply gone, with everything made to it.
      const neverSent = outbox.markOf('vehicles', vehicle.id) === 'new'
      const done = await submit(change(vehicle.id, { action: 'trash', targetId: vehicle.id, expectedVersion: vehicle.version }), () =>
        leave(vehicle.id, () => deleteVehicle({ variables: { id: vehicle.id } })),
      )
      if (neverSent) toast(t('offline.discarded'))
      else
        undoable(t('toast.vehicleTrashed', { name: vehicle.name }), () =>
          undoTrash(client, 'vehicles', vehicle.id, done.queued, () =>
            submit(change(vehicle.id, { action: 'restore', targetId: vehicle.id, expectedVersion: vehicle.version + 1 }), () => restoreVehicle({ variables: { id: vehicle.id } })),
          ),
        )
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
      {!reachable && (
        <Alert severity="info" sx={{ mb: 2 }}>
          {t('vehicles.offlineList')}
        </Alert>
      )}
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
        syncState={{ entity: 'vehicles', name: (v) => v.name }}
        toolbar={() => (
          <VehicleFormDialog
            trigger={<Button size="large">{t('vehicles.add')}</Button>}
            onSubmit={(values) => {
              const input = { ...values, id: values.id ?? uuidV4() }
              return submit({ id: input.id, entity: 'vehicles', action: 'add', vehicleId: input.id, targetId: input.id, input }, () => addVehicle({ variables: { input } }))
            }}
          />
        )}
        actions={(v) =>
          // Waiting on this device to be trashed: Keep takes that back; editing it meanwhile would change nothing.
          outbox.markOf('vehicles', v.id) === 'deleted' ? (
            <KeepButton entity="vehicles" id={v.id} name={v.name} />
          ) : v.canEdit ? (
            <Stack direction="row" sx={{ gap: 1, justifyContent: 'flex-end' }}>
              <VehicleFormDialog
                vehicleId={v.id}
                trigger={
                  <IconAction size="large" tone="primary" label={t('vehicles.editAria', { name: v.name })}>
                    <Pencil size={16} aria-hidden />
                  </IconAction>
                }
                onSubmit={(input) =>
                  submit(change(v.id, { action: 'update', targetId: v.id, input: { ...input, id: v.id }, expectedVersion: v.version }), () =>
                    updateVehicle({ variables: { input: { ...input, id: v.id } } }),
                  )
                }
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
