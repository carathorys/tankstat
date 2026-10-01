import { useQuery } from '@apollo/client/react'
import { Box, Flex, Heading, Link as RadixLink, Tabs, Text } from '@radix-ui/themes'
import { ArrowLeft } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link, useParams, useSearchParams } from 'react-router'
import { VehiclePicture } from '../components/VehiclePicture.tsx'
import { VehicleDetailsDocument } from '../gql/generated.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { ErrorMessage } from '../messages.tsx'
import { DetailsPanel } from './vehicle/DetailsPanel.tsx'
import { RefuelingsPanel } from './vehicle/RefuelingsPanel.tsx'
import { SharingPanel } from './vehicle/SharingPanel.tsx'

const TABS = ['refuelings', 'details', 'sharing'] as const
type Tab = (typeof TABS)[number]

/** One vehicle: its logs, its details and picture, and who the logs are shared with (tabs; the choice is in the address). */
export function VehiclePage() {
  const { t } = useTranslation()
  const { id = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const { data, error, refetch } = useQuery(VehicleDetailsDocument, { variables: { id }, fetchPolicy: 'cache-and-network' })
  const vehicle = data?.vehicle
  usePageTitle(vehicle?.name)

  const canLog = vehicle ? vehicle.canEdit || vehicle.logAccess === 'EDIT' || vehicle.logAccess === 'DELETE' : false
  const requested = params.get('tab') as Tab | null
  const tab: Tab = requested && TABS.includes(requested) && (requested !== 'sharing' || vehicle?.canEdit) ? requested : 'refuelings'

  return (
    <section aria-labelledby="page-title">
      <RadixLink asChild size="2">
        <Link to="/vehicles" style={{ display: 'inline-flex', alignItems: 'center', gap: 4, minHeight: 44 }}>
          <ArrowLeft size={16} aria-hidden />
          {t('vehicles.back')}
        </Link>
      </RadixLink>
      {error && <ErrorMessage error={error} />}
      {!data && !error && (
        <Text as="p" role="status">
          {t('app.loading')}
        </Text>
      )}
      {data && !vehicle && <ErrorMessage>{t('vehicles.notFound')}</ErrorMessage>}
      {vehicle && (
        <>
          <Flex align="center" gap="4" my="3">
            <VehiclePicture url={vehicle.pictureUrl} name={vehicle.name} width={72} />
            <Box>
              <Heading id="page-title">{vehicle.name}</Heading>
              {vehicle.licensePlate && (
                <Text size="2" color="gray">
                  {vehicle.licensePlate}
                </Text>
              )}
              {!vehicle.canEdit && (
                <Text as="p" size="1" color="gray">
                  {t('vehicles.logAccessNote')}
                </Text>
              )}
            </Box>
          </Flex>

          <Tabs.Root value={tab} onValueChange={(value) => setParams(value === 'refuelings' ? {} : { tab: value }, { replace: true })}>
            <Tabs.List aria-label={vehicle.name} size={{ initial: '2', md: '2' }} style={{ overflowX: 'auto' }}>
              <Tabs.Trigger value="refuelings">{t('vehicles.tabs.refuelings')}</Tabs.Trigger>
              <Tabs.Trigger value="details">{t('vehicles.tabs.details')}</Tabs.Trigger>
              {vehicle.canEdit && <Tabs.Trigger value="sharing">{t('vehicles.tabs.sharing')}</Tabs.Trigger>}
            </Tabs.List>
            <Box pt="4">
              <Tabs.Content value="refuelings">
                <RefuelingsPanel vehicle={vehicle} canLog={canLog} />
              </Tabs.Content>
              <Tabs.Content value="details">
                <DetailsPanel vehicle={vehicle} onChanged={() => refetch()} />
              </Tabs.Content>
              {vehicle.canEdit && (
                <Tabs.Content value="sharing">
                  <SharingPanel vehicleId={vehicle.id} />
                </Tabs.Content>
              )}
            </Box>
          </Tabs.Root>
        </>
      )}
    </section>
  )
}
