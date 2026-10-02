import { useMutation } from '@apollo/client/react'
import { Button, DataList, Flex, Heading, Text } from '@radix-ui/themes'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { ImagePicker } from '../../components/ImagePicker.tsx'
import { UserChip } from '../../components/UserAvatar.tsx'
import { VehiclePicture } from '../../components/VehiclePicture.tsx'
import { DeleteVehicleDocument, UpdateVehicleDocument, type VehicleDetailsQuery } from '../../gql/generated.ts'
import { ErrorMessage } from '../../messages.tsx'
import { vehiclePicturePath } from '../../pictures/upload.ts'
import { VehicleFormDialog } from '../../VehicleFormDialog.tsx'

type Vehicle = NonNullable<VehicleDetailsQuery['vehicle']>

export function DetailsPanel({ vehicle, onChanged }: { vehicle: Vehicle; onChanged: () => void | Promise<unknown> }) {
  const { t } = useTranslation()
  const none = t('common.none')
  const [updateVehicle] = useMutation(UpdateVehicleDocument, { refetchQueries: ['VehicleDetails', 'Vehicles'], awaitRefetchQueries: true })
  const [deleteVehicle] = useMutation(DeleteVehicleDocument, { refetchQueries: ['Welcome', 'Vehicles', 'Trash'] })
  const navigate = useNavigate()
  const [deleteError, setDeleteError] = useState<unknown>()

  async function moveToTrash() {
    setDeleteError(undefined)
    try {
      await deleteVehicle({ variables: { id: vehicle.id } })
      void navigate('/') // the vehicle is in the trash now; its page would only say it does not exist
    } catch (e) {
      setDeleteError(e)
    }
  }

  return (
    <Flex direction="column" gap="5">
      <DataList.Root>
        <DataList.Item>
          <DataList.Label>{t('vehicles.owner')}</DataList.Label>
          <DataList.Value>{vehicle.owner ? <UserChip user={vehicle.owner} /> : none}</DataList.Value>
        </DataList.Item>
        <DataList.Item>
          <DataList.Label>{t('vehicles.fuel')}</DataList.Label>
          <DataList.Value>{t(`fuel.${vehicle.fuelType}`)}</DataList.Value>
        </DataList.Item>
        <DataList.Item>
          <DataList.Label>{t('vehicles.plate')}</DataList.Label>
          <DataList.Value>{vehicle.licensePlate ?? none}</DataList.Value>
        </DataList.Item>
        <DataList.Item>
          <DataList.Label>{t('units.distanceLabel')}</DataList.Label>
          <DataList.Value>{t(`units.distance.${vehicle.units.distance}`)}</DataList.Value>
        </DataList.Item>
        <DataList.Item>
          <DataList.Label>{t('units.volumeLabel')}</DataList.Label>
          <DataList.Value>{t(`units.volume.${vehicle.units.volume}`)}</DataList.Value>
        </DataList.Item>
      </DataList.Root>

      {vehicle.canEdit && (
        <>
          <div>
            <VehicleFormDialog
              vehicleId={vehicle.id}
              trigger={<Button size="3" variant="soft">{t('vehicles.editVehicle')}</Button>}
              onSubmit={(input) => updateVehicle({ variables: { input: { ...input, id: vehicle.id } } })}
            />
          </div>
          <section aria-labelledby="picture-heading">
            <Heading as="h2" size="4" id="picture-heading" mb="3">
              {t('columns.picture')}
            </Heading>
            <ImagePicker
              preview={<VehiclePicture url={vehicle.pictureUrl} name={vehicle.name} width="min(16rem, 100%)" ratio="16 / 10" />}
              hasImage={Boolean(vehicle.pictureUrl)}
              path={vehiclePicturePath(vehicle.id)}
              maxEdge={1280}
              onChanged={onChanged}
            />
          </section>
          <section aria-labelledby="trash-heading">
            <Heading as="h2" size="4" id="trash-heading" mb="2">
              {t('vehicles.dangerZone')}
            </Heading>
            <Text as="p" size="2" color="gray" mb="3">
              {t('vehicles.trashDescription')}
            </Text>
            {deleteError !== undefined && <ErrorMessage error={deleteError} />}
            <ConfirmDialog
              trigger={
                <Button size="3" variant="soft" color="red">
                  {t('vehicles.moveToTrash')}
                </Button>
              }
              title={t('vehicles.trashTitle', { name: vehicle.name })}
              description={t('vehicles.trashDescription')}
              confirmLabel={t('vehicles.trashConfirm')}
              onConfirm={() => void moveToTrash()}
            />
          </section>
        </>
      )}
    </Flex>
  )
}
