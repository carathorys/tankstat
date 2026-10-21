import { useQuery } from '@apollo/client/react'
import Card from '@mui/material/Card'
import Chip from '@mui/material/Chip'
import Skeleton from '@mui/material/Skeleton'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { visuallyHidden } from '../components/visuallyHidden.ts'
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
    <Card sx={{ p: 2 }}>
      <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'flex-start', gap: 1.5, mb: 1.5 }}>
        <Stack sx={{ gap: 0.5, minWidth: 0 }}>
          <Typography component={`h${headingLevel}`} variant="h6">
            {title}
          </Typography>
          <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
            {shared && <Chip color="primary" label={t('dashboard.shared')} />}
            {meta}
          </Stack>
        </Stack>
        {actions}
      </Stack>
      {error && <ErrorMessage error={error} />}
      {!data && !error && (
        <>
          <span role="status" style={visuallyHidden}>
            {t('app.loading')}
          </span>
          <Skeleton variant="rounded" height={240} aria-hidden />
        </>
      )}
      {data && <ChartView data={data.vehicleChartData} recipe={recipe} units={units} title={title} />}
    </Card>
  )
}
