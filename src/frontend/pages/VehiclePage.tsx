import { useQuery } from '@apollo/client/react'
import { Box, Link as RadixLink, Tabs, Text } from '@radix-ui/themes'
import { ArrowLeft } from 'lucide-react'
import { lazy, Suspense } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams, useSearchParams } from 'react-router'
import { VehicleHero } from '../components/VehicleHero.tsx'
import { VehicleDetailsDocument } from '../gql/generated.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { ErrorMessage } from '../messages.tsx'
import { DetailsPanel } from './vehicle/DetailsPanel.tsx'
import { ExpensesPanel } from './vehicle/ExpensesPanel.tsx'
import { RefuelingsPanel } from './vehicle/RefuelingsPanel.tsx'
import { SharingPanel } from './vehicle/SharingPanel.tsx'

// The dashboard brings the chart library; it is loaded with the first tab instead of with the page shell.
const DashboardPanel = lazy(() => import('../dashboard/DashboardPanel.tsx').then((m) => ({ default: m.DashboardPanel })))

const TABS = ['dashboard', 'refuelings', 'expenses', 'details', 'sharing'] as const
type Tab = (typeof TABS)[number]

/** One vehicle: its logs, its details and picture, and who the logs are shared with (tabs; the choice is in the address). */
export function VehiclePage({ isAdmin }: { isAdmin: boolean }) {
  const { t } = useTranslation()
  const { id = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const { data, error, refetch } = useQuery(VehicleDetailsDocument, { variables: { id }, fetchPolicy: 'cache-and-network' })
  const vehicle = data?.vehicle
  usePageTitle(vehicle?.name)

  const canLog = vehicle ? vehicle.canEdit || vehicle.logAccess === 'EDIT' || vehicle.logAccess === 'DELETE' : false
  const requested = params.get('tab') as Tab | null
  const tab: Tab = requested && TABS.includes(requested) && (requested !== 'sharing' || vehicle?.canEdit) ? requested : 'dashboard'

  return (
    <section aria-labelledby="page-title">
      <RadixLink asChild size="2">
        <Link to={isAdmin ? '/vehicles' : '/'} style={{ display: 'inline-flex', alignItems: 'center', gap: 4, minHeight: 44 }}>
          <ArrowLeft size={16} aria-hidden />
          {isAdmin ? t('vehicles.back') : t('vehicles.backHome')}
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
          <Box my="3">
            <VehicleHero id={vehicle.id} name={vehicle.name} plate={vehicle.licensePlate} pictureUrl={vehicle.pictureUrl} owner={vehicle.owner} />
            {!vehicle.canEdit && (
              <Text as="p" size="1" color="gray" mt="2">
                {t('vehicles.logAccessNote')}
              </Text>
            )}
          </Box>

          <Tabs.Root value={tab} onValueChange={(value) => setParams(value === 'dashboard' ? {} : { tab: value }, { replace: true })}>
            <Tabs.List aria-label={vehicle.name} size={{ initial: '2', md: '2' }} style={{ overflowX: 'auto' }}>
              <Tabs.Trigger value="dashboard">{t('vehicles.tabs.dashboard')}</Tabs.Trigger>
              <Tabs.Trigger value="refuelings">{t('vehicles.tabs.refuelings')}</Tabs.Trigger>
              <Tabs.Trigger value="expenses">{t('vehicles.tabs.expenses')}</Tabs.Trigger>
              <Tabs.Trigger value="details">{t('vehicles.tabs.details')}</Tabs.Trigger>
              {vehicle.canEdit && <Tabs.Trigger value="sharing">{t('vehicles.tabs.sharing')}</Tabs.Trigger>}
            </Tabs.List>
            <Box pt="4">
              <Tabs.Content value="dashboard">
                <Suspense fallback={<Text as="p" role="status">{t('app.loading')}</Text>}>
                  <DashboardPanel vehicle={vehicle} />
                </Suspense>
              </Tabs.Content>
              <Tabs.Content value="refuelings">
                <RefuelingsPanel vehicle={vehicle} canLog={canLog} />
              </Tabs.Content>
              <Tabs.Content value="expenses">
                <ExpensesPanel vehicle={vehicle} canLog={canLog} />
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
