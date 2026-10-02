import { Badge, Box, Flex, Grid, Heading, Text } from '@radix-ui/themes'
import { useLayoutEffect, useRef, useState, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { LazySparkline } from '../dashboard/LazySparkline.tsx'
import type { WelcomeQuery } from '../gql/generated.ts'
import { useMediaQuery } from '../hooks/useMediaQuery.ts'
import { useFormat } from '../i18n/format.ts'
import { CoverLayers } from './CoverLayers.tsx'
import { useDueText } from '../hooks/useDueText.ts'
import { RecurringStatusBadge } from './RecurringStatus.tsx'
import { UserChip } from './UserAvatar.tsx'

type Vehicle = WelcomeQuery['myVehicles'][number]

/**
 * A vehicle on the welcome screen: its picture as the background, with the name on it. The key figures and the spending trend flow in on
 * hover or keyboard focus; on a touch screen a tap on the card shows them (tapping the name opens the vehicle). With a mouse the whole
 * card opens the vehicle.
 */
export function VehicleCard({ vehicle: v }: { vehicle: Vehicle }) {
  const { t } = useTranslation()
  const format = useFormat()
  const s = v.summary
  const none = t('welcome.card.none')
  const dueText = useDueText(v.units.distance)
  // Only what needs attention is on the card (the vehicle's Recurring tab has the rest); overdue first, the server already sorts by urgency.
  const attention = v.recurring.filter((r) => r.status.state !== 'UPCOMING')
  const touch = useMediaQuery('(hover: none)', false)
  const [open, setOpen] = useState(false)
  // The figures slide up from below the card: the card needs to know how tall that panel is.
  const panel = useRef<HTMLDivElement>(null)
  const [panelHeight, setPanelHeight] = useState(0)
  useLayoutEffect(() => {
    const el = panel.current
    if (!el) return
    const measure = () => setPanelHeight(el.offsetHeight)
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(el)
    return () => observer.disconnect()
  }, [])

  return (
    <Box
      className="vehicle-card"
      data-open={open ? '' : undefined}
      style={{ '--panel-h': `${panelHeight}px` } as CSSProperties}
      // Touch only: a tap that is not on the name link shows or hides the figures. Mouse and keyboard users have hover and focus.
      onClick={(e) => {
        if (touch && !(e.target as HTMLElement).closest('a')) setOpen((o) => !o)
      }}
    >
      <CoverLayers pictureUrl={v.pictureUrl} id={v.id} />
      <Flex direction="column" gap="3" className="card-content">
        <Box p="3">
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
          {attention.length > 0 && (
            <Flex asChild direction="column" gap="1" mt="2">
              <ul aria-label={t('welcome.card.recurringTitle')} style={{ listStyle: 'none', padding: 0, margin: 0 }}>
                {attention.slice(0, 3).map((r) => {
                  const due = dueText(r.status)
                  return (
                    <li key={r.id}>
                      <Flex align="center" gap="2" wrap="wrap">
                        <RecurringStatusBadge state={r.status.state} solid />
                        <Text size="1" style={{ color: 'white', textShadow: '0 1px 4px rgba(0,0,0,0.7)' }}>
                          {due ? `${r.title} · ${due}` : r.title}
                        </Text>
                      </Flex>
                    </li>
                  )
                })}
              </ul>
            </Flex>
          )}
          {!v.canEdit && v.owner && (
            <Text as="p" size="1" mt="1" style={{ color: 'white' }}>
              <UserChip user={v.owner} />
            </Text>
          )}
        </Box>

        <div ref={panel}>
        <Grid className="vehicle-card-stats-container">
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
          </Grid>
          {s?.currency && s.fillUpCount + s.expenseCount > 0 && (
            <Grid className="vehicle-card-sparkline" columns="1" gap="0" p="0">
              <Box style={{ gridColumn: '1 / -1' }}>
                <LazySparkline points={s.spendTrend} currency={s.currency} height={32} />
              </Box>
            </Grid>
          )}
        </Grid>
        </div>
      </Flex>
    </Box>
  )
}
