import { useQuery } from '@apollo/client/react'
import { Button, Card, Flex, Grid, Heading, IconButton, Text } from '@radix-ui/themes'
import { Pencil, Plus, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ConfirmDialog } from '../components/ConfirmDialog.tsx'
import { UserChip } from '../components/UserAvatar.tsx'
import { DeleteChartDocument, VehicleDashboardDocument, type AccessLevel, type DistanceUnit, type VolumeUnit } from '../gql/generated.ts'
import { useFormat } from '../i18n/format.ts'
import { ErrorMessage } from '../messages.tsx'
import { useMutation } from '@apollo/client/react'
import { ChartBuilderDialog } from './ChartBuilderDialog.tsx'
import { ChartCard } from './ChartCard.tsx'
import { Sparkline } from './Sparkline.tsx'
import { NEW_RECIPE, type ChartRecipe } from './chartFormat.ts'

type Units = { distance: DistanceUnit; volume: VolumeUnit }

const recipe = (over: Partial<ChartRecipe>): ChartRecipe => ({ ...NEW_RECIPE, ...over })

/** Ready-made charts every vehicle gets (they are not stored; the user's own charts come below them). */
const PRESETS: { id: string; title: 'monthlyCosts' | 'consumption' | 'fuelPrice' | 'categories' | 'distance'; recipe: ChartRecipe }[] = [
  { id: 'monthlyCosts', title: 'monthlyCosts', recipe: recipe({ metric: 'TOTAL_SPEND', kind: 'BAR', stacked: true }) },
  { id: 'consumption', title: 'consumption', recipe: recipe({ metric: 'AVERAGE_CONSUMPTION', kind: 'LINE' }) },
  { id: 'fuelPrice', title: 'fuelPrice', recipe: recipe({ metric: 'AVERAGE_PRICE_PER_UNIT', kind: 'LINE' }) },
  { id: 'categories', title: 'categories', recipe: recipe({ metric: 'EXPENSE_COST', grouping: 'CATEGORY', kind: 'DONUT' }) },
  { id: 'distance', title: 'distance', recipe: recipe({ metric: 'DISTANCE', kind: 'BAR' }) },
]

function Kpi({ label, value, hint, children }: { label: string; value: string; hint?: string; children?: React.ReactNode }) {
  return (
    <Card size="2" style={{ boxShadow: 'var(--shadow-3)' }}>
      <Text as="p" size="2" color="gray">
        {label}
      </Text>
      <Text as="p" size="6" weight="bold" my="1">
        {value}
      </Text>
      {hint && (
        <Text as="p" size="1" color="gray">
          {hint}
        </Text>
      )}
      {children}
    </Card>
  )
}

/** The first screen of a vehicle: key figures, ready-made charts, and the charts the user (or others) composed. */
export function DashboardPanel({ vehicle }: { vehicle: { id: string; units: Units; logAccess: AccessLevel } }) {
  const { t } = useTranslation()
  const format = useFormat()
  const { data, error, refetch } = useQuery(VehicleDashboardDocument, { variables: { id: vehicle.id }, fetchPolicy: 'cache-and-network' })
  const [remove] = useMutation(DeleteChartDocument)
  const [actionError, setActionError] = useState<unknown>()
  const none = t('dashboard.kpi.none')
  const canShare = vehicle.logAccess === 'EDIT' || vehicle.logAccess === 'DELETE'

  if (error) return <ErrorMessage error={error} />
  if (!data) {
    return (
      <Text as="p" role="status">
        {t('app.loading')}
      </Text>
    )
  }

  const s = data.vehicle?.summary
  async function deleteChart(id: string) {
    setActionError(undefined)
    try {
      await remove({ variables: { id } })
      await refetch()
    } catch (e) {
      setActionError(e)
    }
  }

  return (
    <Flex direction="column" gap="5">
      <section aria-label={t('dashboard.kpi.title')}>
        <Grid columns={{ initial: '1', xs: '2', md: '4' }} gap="3">
          <Kpi
            label={t('dashboard.kpi.thisMonth')}
            value={s?.currency ? format.money(s.thisMonthSpend, s.currency) : none}
            hint={s?.currency ? t('dashboard.kpi.lastMonth', { amount: format.money(s.lastMonthSpend, s.currency) }) : undefined}
          >
            {s && s.currency && <Sparkline points={s.spendTrend} currency={s.currency} />}
          </Kpi>
          <Kpi label={t('dashboard.kpi.consumption')} value={s?.averageConsumption != null ? format.consumption(s.averageConsumption, vehicle.units) : none} hint={t('dashboard.kpi.consumptionHint')} />
          <Kpi label={t('dashboard.kpi.odometer')} value={s?.latestOdometer != null ? format.distance(s.latestOdometer, vehicle.units.distance) : none} />
          <Kpi
            label={t('dashboard.kpi.lastFillUp')}
            value={s?.lastFillUpDate ? format.date(s.lastFillUpDate) : none}
            hint={s ? t('dashboard.kpi.counts', { fuel: s.fillUpCount, expenses: s.expenseCount }) : undefined}
          />
        </Grid>
      </section>

      <section aria-labelledby="overview-charts">
        <Heading as="h2" id="overview-charts" size="4" mb="3">
          {t('dashboard.overview')}
        </Heading>
        <Grid columns={{ initial: '1', md: '2' }} gap="4">
          {PRESETS.map((p) => (
            <ChartCard key={p.id} vehicleId={vehicle.id} title={t(`dashboard.presets.${p.title}`)} recipe={p.recipe} units={vehicle.units} />
          ))}
        </Grid>
      </section>

      <section aria-labelledby="your-charts">
        <Flex justify="between" align="center" gap="3" wrap="wrap" mb="3">
          <Heading as="h2" id="your-charts" size="4">
            {t('dashboard.yourCharts')}
          </Heading>
          <ChartBuilderDialog
            vehicle={vehicle}
            canShare={canShare}
            onSaved={() => refetch()}
            trigger={
              <Button size="3">
                <Plus size={16} aria-hidden />
                {t('dashboard.addChart')}
              </Button>
            }
          />
        </Flex>
        {actionError !== undefined && <ErrorMessage error={actionError} />}
        {data.vehicleCharts.length === 0 ? (
          <Text as="p" size="2" color="gray">
            {t('dashboard.noCharts')}
          </Text>
        ) : (
          <Grid columns={{ initial: '1', md: '2' }} gap="4">
            {data.vehicleCharts.map((c) => {
              const r: ChartRecipe = { metric: c.metric, grouping: c.grouping, kind: c.kind, range: c.range, stacked: c.stacked, from: c.rangeFrom ?? null, to: c.rangeTo ?? null }
              return (
                <ChartCard
                  key={c.id}
                  vehicleId={vehicle.id}
                  title={c.title}
                  recipe={r}
                  units={vehicle.units}
                  shared={c.isShared}
                  meta={c.createdBy && c.isShared ? <UserChip user={c.createdBy} /> : undefined}
                  actions={
                    c.canEdit ? (
                      <Flex gap="2">
                        <ChartBuilderDialog
                          vehicle={vehicle}
                          canShare={canShare}
                          chart={{ id: c.id, title: c.title, isShared: c.isShared, recipe: r }}
                          onSaved={() => refetch()}
                          trigger={
                            <IconButton size="3" variant="soft" aria-label={t('dashboard.editChart', { title: c.title })}>
                              <Pencil size={16} aria-hidden />
                            </IconButton>
                          }
                        />
                        <ConfirmDialog
                          trigger={
                            <IconButton size="3" variant="soft" color="red" aria-label={t('dashboard.deleteChart', { title: c.title })}>
                              <Trash2 size={16} aria-hidden />
                            </IconButton>
                          }
                          title={t('dashboard.deleteTitle')}
                          description={t('dashboard.deleteDescription', { title: c.title })}
                          confirmLabel={t('dashboard.deleteConfirm')}
                          onConfirm={() => void deleteChart(c.id)}
                        />
                      </Flex>
                    ) : undefined
                  }
                />
              )
            })}
          </Grid>
        )}
      </section>
    </Flex>
  )
}
