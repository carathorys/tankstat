import { Badge, Box, Flex, Grid, Heading, Text } from '@radix-ui/themes'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { Sparkline } from '../dashboard/Sparkline.tsx'
import type { WelcomeQuery } from '../gql/generated.ts'
import { useFormat } from '../i18n/format.ts'
import { CoverLayers } from './CoverLayers.tsx'
import { UserChip } from './UserAvatar.tsx'

type Vehicle = WelcomeQuery['vehicles'][number]

/** A vehicle on the welcome screen: its picture as the background, and the key figures. The whole card opens the vehicle. */
export function VehicleCard({ vehicle: v }: { vehicle: Vehicle }) {
  const { t } = useTranslation()
  const format = useFormat()
  const s = v.summary
  const none = t('welcome.card.none')

  return (
    <Box className="vehicle-card">
      <CoverLayers pictureUrl={v.pictureUrl} id={v.id} />
      <Flex direction="column" gap="3" p="4" className="card-content">
        <Box>
          <Heading as="h2" size="5" style={{ textShadow: '0 1px 6px rgba(0,0,0,0.6)' }}>
            <Link className="vehicle-card-link" to={`/vehicles/${v.id}`} aria-label={t('welcome.card.openAria', { name: v.name })}>
              {v.name}
            </Link>
          </Heading>
          <Flex gap="2" align="center" wrap="wrap" mt="1">
            {v.licensePlate && (
              <Badge color="gray" variant="solid" highContrast>
                {v.licensePlate}
              </Badge>
            )}
            <Badge color="gray" variant="soft" highContrast>
              {t(`fuel.${v.fuelType}`)}
            </Badge>
            {!v.canEdit && (
              <Badge color="amber" variant="solid">
                {t('welcome.card.logAccess', { level: t(`level.${v.logAccess}`) })}
              </Badge>
            )}
          </Flex>
          {!v.canEdit && v.owner && (
            <Text as="p" size="1" mt="1" style={{ color: 'white' }}>
              <UserChip user={v.owner} />
            </Text>
          )}
        </Box>

        <Grid className="vehicle-card-stats" columns="2" gap="2" p="3">
          <Box>
            <Text as="p" size="1" style={{ opacity: 0.8 }}>
              {t('welcome.card.odometer')}
            </Text>
            <Text as="p" size="2" weight="bold">
              {s?.latestOdometer != null ? format.distance(s.latestOdometer, v.units.distance) : none}
            </Text>
          </Box>
          <Box>
            <Text as="p" size="1" style={{ opacity: 0.8 }}>
              {t('welcome.card.consumption')}
            </Text>
            <Text as="p" size="2" weight="bold">
              {s?.averageConsumption != null ? format.consumption(s.averageConsumption, v.units) : none}
            </Text>
          </Box>
          <Box>
            <Text as="p" size="1" style={{ opacity: 0.8 }}>
              {t('welcome.card.lastFillUp')}
            </Text>
            <Text as="p" size="2" weight="bold">
              {s?.lastFillUpDate ? format.date(s.lastFillUpDate) : t('welcome.card.noFillUps')}
            </Text>
          </Box>
          <Box>
            <Text as="p" size="1" style={{ opacity: 0.8 }}>
              {t('welcome.card.thisMonth')}
            </Text>
            <Text as="p" size="2" weight="bold">
              {s?.currency ? format.money(s.thisMonthSpend, s.currency) : none}
            </Text>
          </Box>
          {s?.currency && s.fillUpCount + s.expenseCount > 0 && (
            <Box style={{ gridColumn: '1 / -1' }}>
              <Sparkline points={s.spendTrend} currency={s.currency} height={32} />
            </Box>
          )}
        </Grid>
      </Flex>
    </Box>
  )
}
