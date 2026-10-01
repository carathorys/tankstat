import { useMutation } from '@apollo/client/react'
import { Button, DataList, Flex, Heading } from '@radix-ui/themes'
import { useTranslation } from 'react-i18next'
import { ImagePicker } from '../../components/ImagePicker.tsx'
import { UserChip } from '../../components/UserAvatar.tsx'
import { VehiclePicture } from '../../components/VehiclePicture.tsx'
import { UpdateVehicleDocument, type VehicleDetailsQuery } from '../../gql/generated.ts'
import { vehiclePicturePath } from '../../pictures/upload.ts'
import { VehicleFormDialog } from '../../VehicleFormDialog.tsx'

type Vehicle = NonNullable<VehicleDetailsQuery['vehicle']>

export function DetailsPanel({ vehicle, onChanged }: { vehicle: Vehicle; onChanged: () => void | Promise<unknown> }) {
  const { t } = useTranslation()
  const none = t('common.none')
  const [updateVehicle] = useMutation(UpdateVehicleDocument, { refetchQueries: ['VehicleDetails', 'Vehicles'], awaitRefetchQueries: true })

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
        </>
      )}
    </Flex>
  )
}
