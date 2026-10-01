import { useQuery } from '@apollo/client/react'
import { Badge, Card, Flex, Heading, Skeleton, Text } from '@radix-ui/themes'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ChartDataDocument, type DistanceUnit, type VolumeUnit } from '../gql/generated.ts'
import { ErrorMessage } from '../messages.tsx'
import { ChartView } from './ChartView.tsx'
import { toConfig, type ChartRecipe } from './chartFormat.ts'

/** One chart in a titled card: loads its own data from the server and draws it. Used for presets, saved charts and the builder's preview. */
export function ChartCard({
  vehicleId,
  title,
  recipe,
  units,
  shared,
  meta,
  actions,
  headingLevel = 3,
}: {
  vehicleId: string
  title: string
  recipe: ChartRecipe
  units: { distance: DistanceUnit; volume: VolumeUnit }
  shared?: boolean
  meta?: ReactNode
  actions?: ReactNode
  headingLevel?: 2 | 3
}) {
  const { t } = useTranslation()
  const { data, error } = useQuery(ChartDataDocument, { variables: { vehicleId, config: toConfig(recipe) }, fetchPolicy: 'cache-and-network' })

  return (
    <Card size="2" style={{ boxShadow: 'var(--shadow-3)' }}>
      <Flex justify="between" align="start" gap="3" mb="3">
        <Flex direction="column" gap="1" style={{ minWidth: 0 }}>
          <Heading as={`h${headingLevel}`} size="3">
            {title}
          </Heading>
          <Flex gap="2" align="center" wrap="wrap">
            {shared && <Badge color="indigo">{t('dashboard.shared')}</Badge>}
            {meta}
          </Flex>
        </Flex>
        {actions}
      </Flex>
      {error && <ErrorMessage error={error} />}
      {!data && !error && (
        <Skeleton>
          <Text as="p" style={{ height: 240 }} role="status" aria-label={t('app.loading')}>
            {t('app.loading')}
          </Text>
        </Skeleton>
      )}
      {data && <ChartView data={data.vehicleChartData} recipe={recipe} units={units} title={title} />}
    </Card>
  )
}
