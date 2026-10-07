import Box from '@mui/material/Box'
import Link from '@mui/material/Link'
import Stack from '@mui/material/Stack'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'
import { BarChart } from '@mui/x-charts/BarChart'
import { LineChart } from '@mui/x-charts/LineChart'
import { PieChart } from '@mui/x-charts/PieChart'
import { useReducedMotion } from 'motion/react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { SurfaceTable } from '../components/SurfaceTable.tsx'
import type { DistanceUnit, VolumeUnit } from '../gql/generated.ts'
import { useFormat } from '../i18n/format.ts'
import { PALETTE, periodLabel, seriesId, type ChartData, type ChartRecipe, type Series } from './chartFormat.ts'

type Units = { distance: DistanceUnit; volume: VolumeUnit }

const noData = (data: ChartData) => data.series.every((s) => s.points.every((p) => !p.value))

/**
 * A chart of server-calculated data. The picture is for sighted users (hidden from assistive technology); the same numbers are always
 * available as a table next to it, which is what a screen reader reads.
 */
export function ChartView({ data, recipe, units, title, height = 240 }: { data: ChartData; recipe: Pick<ChartRecipe, 'kind' | 'metric' | 'grouping' | 'stacked'>; units: Units; title: string; height?: number }) {
  const { t, i18n } = useTranslation()
  const format = useFormat()
  const reduce = useReducedMotion() ?? false
  const [table, setTable] = useState(false)

  if (noData(data)) {
    return (
      <Typography variant="body2" role="status" sx={{ color: 'text.secondary' }}>
        {t('dashboard.noData')}
      </Typography>
    )
  }

  const keyLabel = (key: string) => (key === '' ? t('dashboard.uncategorised') : periodLabel(key, i18n.language))
  const name = (s: Series) => `${t(`dashboard.series.${s.kind as 'total' | 'fuel' | 'expenses'}`, { defaultValue: t('dashboard.series.value') })}${s.currency ? ` · ${s.currency}` : ''}`
  const show = (value: number | null | undefined, s: Series) => {
    if (value == null) return t('common.none')
    switch (data.unit) {
      case 'CURRENCY': return format.money(value, s.currency ?? '')
      case 'VOLUME': return format.volume(value, units.volume)
      case 'DISTANCE': return format.distance(value, units.distance)
      case 'CONSUMPTION': return format.consumption(value, units)
      default: return format.number(value)
    }
  }
  const axis = (value: number) => (data.unit === 'CURRENCY' || data.unit === 'COUNT' || data.unit === 'DISTANCE' ? format.compact(value) : format.number(value, { maximumFractionDigits: 1 }))

  const keys = [...new Set(data.series.flatMap((s) => s.points.map((p) => p.key)))]
  const rows: Record<string, string | number | null>[] = keys.map((key) => ({ key, label: keyLabel(key), ...Object.fromEntries(data.series.map((s) => [seriesId(s), s.points.find((p) => p.key === key)?.value ?? null])) }))
  const colour = (i: number) => PALETTE[i % PALETTE.length]
  const several = data.series.length > 1
  // The parts every cartesian chart shares: the periods along the bottom, compact values up the side, light horizontal lines.
  const cartesian = {
    dataset: rows,
    height,
    margin: { top: 8, right: 8, bottom: 0, left: 0 },
    yAxis: [{ valueFormatter: axis, width: 48 }],
    grid: { horizontal: true },
    hideLegend: !several,
    skipAnimation: reduce,
    disableKeyboardNavigation: true,
  }
  const lines = (area: boolean) =>
    data.series.map((s, i) => ({
      dataKey: seriesId(s),
      label: name(s),
      color: colour(i),
      curve: 'monotoneX' as const,
      showMark: rows.length < 25,
      connectNulls: true,
      area,
      stack: area && recipe.stacked ? (s.currency ?? 'a') : undefined,
      valueFormatter: (value: number | null) => show(value, s),
    }))

  const picture =
    recipe.kind === 'DONUT' ? (
      <Stack direction="row" sx={{ gap: 2, flexWrap: 'wrap', justifyContent: 'center' }}>
        {data.series.map((s, i) => (
          <Box key={seriesId(s)} sx={{ width: 'min(100%, 18rem)' }}>
            {several && (
              <Typography variant="body2" sx={{ color: 'text.secondary', textAlign: 'center' }}>
                {name(s)}
              </Typography>
            )}
            <PieChart
              height={height}
              skipAnimation={reduce}
              disableKeyboardNavigation
              series={[
                {
                  data: s.points.map((p, j) => ({ id: p.key, value: p.value ?? 0, label: keyLabel(p.key), color: colour(j + i) })),
                  innerRadius: '55%',
                  outerRadius: '85%',
                  paddingAngle: 2,
                  cornerRadius: 2,
                  valueFormatter: (item) => show(item.value, s),
                },
              ]}
            />
          </Box>
        ))}
      </Stack>
    ) : recipe.kind === 'LINE' ? (
      <LineChart {...cartesian} xAxis={[{ scaleType: 'point', dataKey: 'label' }]} series={lines(false)} />
    ) : recipe.kind === 'AREA' ? (
      <LineChart {...cartesian} xAxis={[{ scaleType: 'point', dataKey: 'label' }]} series={lines(true)} />
    ) : (
      <BarChart
        {...cartesian}
        borderRadius={4}
        xAxis={[{ scaleType: 'band', dataKey: 'label' }]}
        series={data.series.map((s, i) => ({ dataKey: seriesId(s), label: name(s), color: colour(i), stack: recipe.stacked ? (s.currency ?? 'a') : undefined, valueFormatter: (value: number | null) => show(value, s) }))}
      />
    )

  const categories = recipe.grouping === 'CATEGORY'
  return (
    <Box component="figure" sx={{ m: 0 }}>
      <div role="img" aria-label={t('dashboard.chartLabel', { title })}>
        <div aria-hidden>{picture}</div>
      </div>
      <Link component="button" type="button" variant="body2" underline="always" aria-expanded={table} onClick={() => setTable(!table)} sx={{ minHeight: 24, mt: 1 }}>
        {table ? t('dashboard.hideTable') : t('dashboard.showTable')}
      </Link>
      {table && (
        <Box sx={{ mt: 1 }}>
          <SurfaceTable caption={title}>
            <TableHead>
              <TableRow>
                <TableCell>{categories ? t('dashboard.table.category') : t('dashboard.table.period')}</TableCell>
                {data.series.map((s) => (
                  <TableCell key={seriesId(s)} align="right">
                    {name(s)}
                  </TableCell>
                ))}
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((row) => (
                <TableRow key={String(row.key)}>
                  <TableCell component="th" scope="row">
                    {String(row.label)}
                  </TableCell>
                  {data.series.map((s) => (
                    <TableCell key={seriesId(s)} align="right">
                      {show(typeof row[seriesId(s)] === 'number' ? (row[seriesId(s)] as number) : null, s)}
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </SurfaceTable>
        </Box>
      )}
    </Box>
  )
}
