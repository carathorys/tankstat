import { useMutation } from '@apollo/client/react'
import * as RadixForm from '@radix-ui/react-form'
import { Button, Dialog, Flex, Switch, Text, TextField } from '@radix-ui/themes'
import { useId, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { LabeledSelect } from '../components/UnitSelect.tsx'
import { Field } from '../forms.tsx'
import { SaveChartDocument, type ChartGrouping, type ChartKind, type ChartMetric, type ChartRange, type DistanceUnit, type VolumeUnit } from '../gql/generated.ts'
import { ErrorMessage } from '../messages.tsx'
import { ChartCard } from './ChartCard.tsx'
import { ADDITIVE, isReady, NEW_RECIPE, normalise, type ChartRecipe } from './chartFormat.ts'

const METRICS: ChartMetric[] = ['TOTAL_SPEND', 'FUEL_COST', 'EXPENSE_COST', 'FUEL_VOLUME', 'DISTANCE', 'AVERAGE_CONSUMPTION', 'AVERAGE_PRICE_PER_UNIT', 'FILL_UPS']
const KINDS: ChartKind[] = ['BAR', 'LINE', 'AREA', 'DONUT']
const RANGES: ChartRange[] = ['LAST1_MONTH', 'LAST3_MONTHS', 'LAST6_MONTHS', 'LAST12_MONTHS', 'THIS_YEAR', 'LAST_YEAR', 'ALL', 'CUSTOM']

export interface EditableChart {
  id: string
  title: string
  isShared: boolean
  recipe: ChartRecipe
}

/**
 * Compose a chart from the data the app can provide: what to measure, how to group it, the chart type and the period (a preset or a
 * custom from-to range). Only valid combinations are offered, and the preview shows the user's own data as the choices change.
 */
export function ChartBuilderDialog({
  trigger,
  vehicle,
  chart,
  canShare,
  onSaved,
}: {
  trigger: ReactNode
  vehicle: { id: string; units: { distance: DistanceUnit; volume: VolumeUnit } }
  chart?: EditableChart
  /** Sharing with everyone who sees the vehicle needs Edit access to its logs. */
  canShare: boolean
  onSaved: () => void | Promise<unknown>
}) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)

  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger>{trigger}</Dialog.Trigger>
      <Dialog.Content maxWidth="640px">
        <Dialog.Title>{chart ? t('charts.dialogEdit') : t('charts.dialogAdd')}</Dialog.Title>
        <Dialog.Description size="2" mb="4">
          {t('charts.dialogDescription')}
        </Dialog.Description>
        {open && (
          <BuilderForm
            vehicle={vehicle}
            chart={chart}
            canShare={canShare}
            onSaved={async () => {
              await onSaved()
              setOpen(false)
            }}
          />
        )}
      </Dialog.Content>
    </Dialog.Root>
  )
}

function BuilderForm({ vehicle, chart, canShare, onSaved }: { vehicle: { id: string; units: { distance: DistanceUnit; volume: VolumeUnit } }; chart?: EditableChart; canShare: boolean; onSaved: () => Promise<unknown> }) {
  const { t } = useTranslation()
  const [title, setTitle] = useState(chart?.title ?? '')
  const [recipe, setRecipe] = useState<ChartRecipe>(chart?.recipe ?? NEW_RECIPE)
  const [shared, setShared] = useState(chart?.isShared ?? false)
  const [save, { loading, error }] = useMutation(SaveChartDocument)
  const stackId = useId()
  const sharedId = useId()

  const change = (patch: Partial<ChartRecipe>) => setRecipe((r) => normalise({ ...r, ...patch }))
  const groupings: ChartGrouping[] = recipe.metric === 'EXPENSE_COST' ? ['MONTH', 'QUARTER', 'YEAR', 'CATEGORY'] : ['MONTH', 'QUARTER', 'YEAR']
  const kinds = KINDS.filter((k) => k !== 'DONUT' || ADDITIVE.includes(recipe.metric))
  const canStack = recipe.metric === 'TOTAL_SPEND' && recipe.grouping !== 'CATEGORY'
  const ready = isReady(recipe)

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!ready) return
    try {
      await save({ variables: { input: { id: chart?.id ?? null, vehicleId: vehicle.id, title, shared: canShare && shared, config: { metric: recipe.metric, grouping: recipe.grouping, kind: recipe.kind, range: recipe.range, stacked: recipe.stacked, from: recipe.from, to: recipe.to } } } })
      await onSaved()
    } catch {
      // shown through `error`
    }
  }

  return (
    <RadixForm.Root onSubmit={submit}>
      <Flex direction="column" gap="3">
        <Field name="title" label={t('charts.fields.title')} required>
          <TextField.Root required maxLength={80} autoComplete="off" value={title} onChange={(e) => setTitle(e.target.value)} />
        </Field>
        <Flex gap="3" wrap="wrap">
          <LabeledSelect label={t('charts.fields.metric')} value={recipe.metric} onChange={(metric) => change({ metric })} options={METRICS.map((m) => ({ value: m, label: t(`charts.metrics.${m}`) }))} />
          <LabeledSelect label={t('charts.fields.grouping')} value={recipe.grouping} onChange={(grouping) => change({ grouping })} options={groupings.map((g) => ({ value: g, label: t(`charts.groupings.${g}`) }))} />
          <LabeledSelect label={t('charts.fields.kind')} value={recipe.kind} onChange={(kind) => change({ kind })} options={kinds.map((k) => ({ value: k, label: t(`charts.kinds.${k}`) }))} />
          <LabeledSelect label={t('charts.fields.range')} value={recipe.range} onChange={(range) => change({ range })} options={RANGES.map((r) => ({ value: r, label: t(`charts.ranges.${r}`) }))} />
        </Flex>

        {recipe.range === 'CUSTOM' && (
          <Flex gap="3" wrap="wrap">
            <Field name="from" label={t('charts.fields.from')} required invalid={{ message: t('errors.chart.datesReversed'), test: () => recipe.from !== null && recipe.to !== null && recipe.from > recipe.to }}>
              <TextField.Root type="date" required value={recipe.from ?? ''} onChange={(e) => change({ from: e.target.value || null })} />
            </Field>
            <Field name="to" label={t('charts.fields.to')} required>
              <TextField.Root type="date" required value={recipe.to ?? ''} onChange={(e) => change({ to: e.target.value || null })} />
            </Field>
          </Flex>
        )}

        {canStack && (
          <Flex align="center" gap="3">
            <Switch id={stackId} size="3" checked={recipe.stacked} onCheckedChange={(stacked) => change({ stacked })} />
            <Flex direction="column">
              <Text as="label" size="2" weight="bold" htmlFor={stackId}>
                {t('charts.fields.stacked')}
              </Text>
              <Text size="1" color="gray">
                {t('charts.hints.stacked')}
              </Text>
            </Flex>
          </Flex>
        )}
        {canShare && (
          <Flex align="center" gap="3">
            <Switch id={sharedId} size="3" checked={shared} onCheckedChange={setShared} />
            <Flex direction="column">
              <Text as="label" size="2" weight="bold" htmlFor={sharedId}>
                {t('charts.fields.shared')}
              </Text>
              <Text size="1" color="gray">
                {t('charts.hints.shared')}
              </Text>
            </Flex>
          </Flex>
        )}

        <section aria-labelledby="preview-label">
          <Text as="p" size="3" weight="bold" id="preview-label" mb="2">
            {t('charts.preview')}
          </Text>
          {ready ? (
            <ChartCard vehicleId={vehicle.id} title={title.trim() || t('charts.preview')} recipe={recipe} units={vehicle.units} headingLevel={2} />
          ) : (
            <Text as="p" size="2" color="gray">
              {t('errors.chart.datesRequired')}
            </Text>
          )}
        </section>

        {error && <ErrorMessage error={error} />}
        <Flex gap="3" justify="end">
          <Dialog.Close>
            <Button type="button" variant="soft" color="gray">
              {t('common.cancel')}
            </Button>
          </Dialog.Close>
          <RadixForm.Submit asChild>
            <Button disabled={loading || !ready}>{t('charts.save')}</Button>
          </RadixForm.Submit>
        </Flex>
      </Flex>
    </RadixForm.Root>
  )
}
