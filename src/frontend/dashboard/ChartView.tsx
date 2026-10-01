import { Box, Flex, Table, Text, VisuallyHidden } from '@radix-ui/themes'
import { useReducedMotion } from 'motion/react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Area, AreaChart, Bar, BarChart, CartesianGrid, Cell, Legend, Line, LineChart, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
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
      <Text as="p" size="2" color="gray" role="status">
        {t('dashboard.noData')}
      </Text>
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
  const seriesByName = new Map(data.series.map((s) => [name(s), s]))
  const tooltip = (value: unknown, label: unknown) => [show(typeof value === 'number' ? value : null, seriesByName.get(String(label)) ?? data.series[0]), String(label)] as [string, string]

  const cartesian = (children: React.ReactNode, Chart: typeof BarChart | typeof LineChart | typeof AreaChart) => (
    <ResponsiveContainer width="100%" height={height} initialDimension={{ width: 320, height }}>
      <Chart data={rows} margin={{ top: 8, right: 8, bottom: 0, left: 0 }} accessibilityLayer={false}>
        <CartesianGrid stroke="var(--gray-a5)" vertical={false} />
        <XAxis dataKey="label" tick={{ fill: 'var(--gray-11)', fontSize: 12 }} tickLine={false} axisLine={{ stroke: 'var(--gray-a6)' }} minTickGap={12} />
        <YAxis tick={{ fill: 'var(--gray-11)', fontSize: 12 }} tickLine={false} axisLine={false} tickFormatter={axis} width={48} />
        <Tooltip formatter={(value, label) => tooltip(value, label)} contentStyle={{ background: 'var(--color-panel-solid)', border: '1px solid var(--gray-a6)', borderRadius: 'var(--radius-3)', boxShadow: 'var(--shadow-4)' }} />
        {data.series.length > 1 && <Legend />}
        {children}
      </Chart>
    </ResponsiveContainer>
  )

  const picture =
    recipe.kind === 'DONUT' ? (
      <Flex gap="4" wrap="wrap" justify="center">
        {data.series.map((s, i) => (
          <Box key={seriesId(s)} style={{ width: 'min(100%, 18rem)' }}>
            {data.series.length > 1 && (
              <Text as="p" size="2" align="center" color="gray">
                {name(s)}
              </Text>
            )}
            <ResponsiveContainer width="100%" height={height} initialDimension={{ width: 288, height }}>
              <PieChart accessibilityLayer={false}>
                <Pie data={s.points.map((p) => ({ name: keyLabel(p.key), value: p.value ?? 0 }))} dataKey="value" nameKey="name" innerRadius="55%" outerRadius="85%" paddingAngle={2} isAnimationActive={!reduce} stroke="var(--color-panel-solid)">
                  {s.points.map((p, j) => <Cell key={p.key} fill={colour(j + i)} />)}
                </Pie>
                <Tooltip formatter={(value, label) => [show(typeof value === 'number' ? value : null, s), String(label)]} contentStyle={{ background: 'var(--color-panel-solid)', border: '1px solid var(--gray-a6)', borderRadius: 'var(--radius-3)' }} />
                <Legend />
              </PieChart>
            </ResponsiveContainer>
          </Box>
        ))}
      </Flex>
    ) : recipe.kind === 'LINE' ? (
      cartesian(data.series.map((s, i) => <Line key={seriesId(s)} type="monotone" dataKey={seriesId(s)} name={name(s)} stroke={colour(i)} strokeWidth={2} dot={rows.length < 25} connectNulls isAnimationActive={!reduce} />), LineChart)
    ) : recipe.kind === 'AREA' ? (
      cartesian(data.series.map((s, i) => <Area key={seriesId(s)} type="monotone" dataKey={seriesId(s)} name={name(s)} stroke={colour(i)} fill={colour(i)} fillOpacity={0.25} strokeWidth={2} connectNulls stackId={recipe.stacked ? (s.currency ?? 'a') : undefined} isAnimationActive={!reduce} />), AreaChart)
    ) : (
      cartesian(data.series.map((s, i) => <Bar key={seriesId(s)} dataKey={seriesId(s)} name={name(s)} fill={colour(i)} radius={[4, 4, 0, 0]} stackId={recipe.stacked ? (s.currency ?? 'a') : undefined} isAnimationActive={!reduce} />), BarChart)
    )

  const categories = recipe.grouping === 'CATEGORY'
  return (
    <figure style={{ margin: 0 }}>
      <div role="img" aria-label={t('dashboard.chartLabel', { title })}>
        <div aria-hidden>{picture}</div>
      </div>
      <Text asChild size="2">
        <button type="button" aria-expanded={table} onClick={() => setTable(!table)} style={{ all: 'unset', cursor: 'pointer', color: 'var(--accent-11)', textDecoration: 'underline', minHeight: 24, display: 'inline-block', marginTop: 'var(--space-2)' }}>
          {table ? t('dashboard.hideTable') : t('dashboard.showTable')}
        </button>
      </Text>
      {table && (
        <Box mt="2" style={{ overflowX: 'auto' }}>
          <Table.Root size="1" variant="surface">
            <VisuallyHidden asChild>
              <caption>{title}</caption>
            </VisuallyHidden>
            <Table.Header>
              <Table.Row>
                <Table.ColumnHeaderCell scope="col">{categories ? t('dashboard.table.category') : t('dashboard.table.period')}</Table.ColumnHeaderCell>
                {data.series.map((s) => (
                  <Table.ColumnHeaderCell key={seriesId(s)} scope="col" justify="end">
                    {name(s)}
                  </Table.ColumnHeaderCell>
                ))}
              </Table.Row>
            </Table.Header>
            <Table.Body>
              {rows.map((row) => (
                <Table.Row key={String(row.key)}>
                  <Table.RowHeaderCell>{String(row.label)}</Table.RowHeaderCell>
                  {data.series.map((s) => (
                    <Table.Cell key={seriesId(s)} justify="end">
                      {show(typeof row[seriesId(s)] === 'number' ? (row[seriesId(s)] as number) : null, s)}
                    </Table.Cell>
                  ))}
                </Table.Row>
              ))}
            </Table.Body>
          </Table.Root>
        </Box>
      )}
    </figure>
  )
}
