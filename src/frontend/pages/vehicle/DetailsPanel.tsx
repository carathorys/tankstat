import { useApolloClient, useMutation } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { DefinitionList } from '../../components/DefinitionList.tsx'
import { ImagePicker } from '../../components/ImagePicker.tsx'
import { UserChip } from '../../components/UserAvatar.tsx'
import { VehiclePicture } from '../../components/VehiclePicture.tsx'
import { DeleteVehicleDocument, RestoreVehicleDocument, UpdateVehicleDocument, type VehicleDetailsQuery } from '../../gql/generated.ts'
import { ErrorMessage } from '../../messages.tsx'
import { vehiclePicturePath } from '../../pictures/upload.ts'
import { outbox } from '../../offline/outbox.ts'
import { undoTrash } from '../../offline/submitChange.ts'
import { useLogChange } from '../../offline/useLogChange.ts'
import { useToast } from '../../toast/toastContext.ts'
import { VehicleFormDialog } from '../../VehicleFormDialog.tsx'

type Vehicle = NonNullable<VehicleDetailsQuery['vehicle']>

// Where a trashed (or restored) vehicle shows: the home page, the full list and the trash.
const LISTS = ['Welcome', 'Vehicles', 'Trash']

export function DetailsPanel({ vehicle, onChanged }: { vehicle: Vehicle; onChanged: () => void | Promise<unknown> }) {
  const { t } = useTranslation()
  const { toast, undoable } = useToast()
  const changes = useLogChange('vehicles', vehicle.id)
  const client = useApolloClient()
  const none = t('common.none')
  const [updateVehicle] = useMutation(UpdateVehicleDocument, { refetchQueries: ['VehicleDetails', 'Vehicles'], awaitRefetchQueries: true })
  const [deleteVehicle] = useMutation(DeleteVehicleDocument, { refetchQueries: LISTS })
  const [restoreVehicle] = useMutation(RestoreVehicleDocument, { refetchQueries: LISTS })
  const navigate = useNavigate()
  const [deleteError, setDeleteError] = useState<unknown>()

  async function moveToTrash() {
    setDeleteError(undefined)
    try {
      // Kept on the device while the server is out of reach; one added here and never sent goes, with everything made to it.
      const neverSent = outbox.markOf('vehicles', vehicle.id) === 'new'
      const done = await changes.trash(vehicle.id, vehicle.version, () => deleteVehicle({ variables: { id: vehicle.id } }))
      void navigate('/') // the vehicle is in the trash now; its page would only say it does not exist
      if (neverSent) toast(t('offline.discarded'))
      else
        undoable(t('toast.vehicleTrashed', { name: vehicle.name }), () =>
          undoTrash(client, 'vehicles', vehicle.id, done.queued, () => changes.restore(vehicle.id, vehicle.version + 1, () => restoreVehicle({ variables: { id: vehicle.id } }))),
        )
    } catch (e) {
      setDeleteError(e)
    }
  }

  return (
    <Stack sx={{ gap: 3 }}>
      <DefinitionList
        items={[
          { label: t('vehicles.owner'), value: vehicle.owner ? <UserChip user={vehicle.owner} /> : none },
          { label: t('vehicles.fuel'), value: t(`fuel.${vehicle.fuelType}`) },
          { label: t('vehicles.plate'), value: vehicle.licensePlate ?? none },
          { label: t('units.distanceLabel'), value: t(`units.distance.${vehicle.units.distance}`) },
          { label: t('units.volumeLabel'), value: t(`units.volume.${vehicle.units.volume}`) },
        ]}
      />

      {vehicle.canEdit && (
        <>
          <div>
            <VehicleFormDialog
              vehicleId={vehicle.id}
              trigger={
                <Button size="large" variant="soft">
                  {t('vehicles.editVehicle')}
                </Button>
              }
              onSubmit={(input) => changes.update(vehicle.id, vehicle.version, { ...input, id: vehicle.id }, () => updateVehicle({ variables: { input: { ...input, id: vehicle.id } } }))}
            />
          </div>
          <section aria-labelledby="picture-heading">
            <Typography component="h2" variant="h5" id="picture-heading" sx={{ mb: 1.5 }}>
              {t('columns.picture')}
            </Typography>
            <ImagePicker
              preview={<VehiclePicture url={vehicle.pictureUrl} name={vehicle.name} width="min(16rem, 100%)" ratio="16 / 10" />}
              hasImage={Boolean(vehicle.pictureUrl)}
              path={vehiclePicturePath(vehicle.id)}
              maxEdge={1280}
              keep={{ vehicleId: vehicle.id }}
              onChanged={onChanged}
            />
          </section>
          <section aria-labelledby="trash-heading">
            <Typography component="h2" variant="h5" id="trash-heading" sx={{ mb: 1 }}>
              {t('vehicles.dangerZone')}
            </Typography>
            <Typography variant="body2" sx={{ color: 'text.secondary', mb: 1.5 }}>
              {t('vehicles.trashDescription')}
            </Typography>
            {deleteError !== undefined && <ErrorMessage error={deleteError} />}
            <Box>
              <ConfirmDialog
                trigger={
                  <Button size="large" variant="soft" color="error">
                    {t('vehicles.moveToTrash')}
                  </Button>
                }
                title={t('vehicles.trashTitle', { name: vehicle.name })}
                description={t('vehicles.trashDescription')}
                confirmLabel={t('vehicles.trashConfirm')}
                onConfirm={() => void moveToTrash()}
              />
            </Box>
          </section>
        </>
      )}
    </Stack>
  )
}
