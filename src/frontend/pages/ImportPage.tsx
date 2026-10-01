import { useApolloClient, useMutation, useQuery } from '@apollo/client/react'
import * as RadixForm from '@radix-ui/react-form'
import { Badge, Box, Button, Callout, Card, Flex, Heading, RadioGroup, Text, TextField } from '@radix-ui/themes'
import { AlertTriangle, FileUp } from 'lucide-react'
import { useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { LabeledSelect } from '../components/UnitSelect.tsx'
import { Field } from '../forms.tsx'
import {
  ConfirmImportDocument,
  ImportPreviewDocument,
  ImportTargetsDocument,
  VehicleDefaultsDocument,
  type ConfirmImportMutation,
  type DistanceUnit,
  type FuelType,
  type ImportPreviewQuery,
  type VolumeUnit,
} from '../gql/generated.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { useKeyText } from '../i18n/errors.ts'
import { useFormat } from '../i18n/format.ts'
import { ErrorMessage, SuccessMessage } from '../messages.tsx'
import { uploadImportFile } from '../pictures/uploadFile.ts'
import { FUEL_TYPES } from '../vehicles.ts'

const FORMATS = ['fuelio'] as const
type Format = (typeof FORMATS)[number]
type Preview = ImportPreviewQuery['importPreview']
type Issue = Preview['issues'][number]
type Result = ConfirmImportMutation['confirmImport']
type Step = 'file' | 'target' | 'review' | 'done'

const STEPS: Step[] = ['file', 'target', 'review', 'done']
const MAX_ISSUES_SHOWN = 20

/** Import fuel logs and expenses from another app: choose a file, choose the target, review what will happen, then confirm. */
export function ImportPage() {
  const { t } = useTranslation()
  usePageTitle(t('import.title'))
  const [step, setStep] = useState<Step>('file')
  const [token, setToken] = useState<string>()
  const [result, setResult] = useState<Result>()

  return (
    <section aria-labelledby="page-title">
      <Heading id="page-title" mb="2">
        {t('import.title')}
      </Heading>
      <Text as="p" size="2" color="gray" mb="3">
        {t('import.intro')}
      </Text>
      <Flex asChild gap="2" mb="4" wrap="wrap">
        <ol aria-label={t('import.title')} style={{ listStyle: 'none', padding: 0, margin: 0 }}>
          {STEPS.map((s, i) => (
            <li key={s} aria-current={s === step ? 'step' : undefined}>
              <Badge size="2" color={s === step ? 'indigo' : 'gray'} variant={s === step ? 'solid' : 'soft'}>
                {i + 1}. {t(`import.steps.${s}`)}
              </Badge>
            </li>
          ))}
        </ol>
      </Flex>

      {step === 'file' && (
        <FileStep
          onRead={(next) => {
            setToken(next)
            setStep('target')
          }}
        />
      )}
      {step !== 'file' && step !== 'done' && token && (
        <TargetAndReview
          token={token}
          step={step}
          onStep={setStep}
          onDone={(r) => {
            setResult(r)
            setStep('done')
          }}
        />
      )}
      {step === 'done' && result && (
        <DoneStep
          result={result}
          onAnother={() => {
            setToken(undefined)
            setResult(undefined)
            setStep('file')
          }}
        />
      )}
    </section>
  )
}

// ---- 1. the file ---------------------------------------------------------------------------------------------

function FileStep({ onRead }: { onRead: (token: string) => void }) {
  const { t } = useTranslation()
  const input = useRef<HTMLInputElement>(null)
  const [format, setFormat] = useState<Format>('fuelio')
  const [file, setFile] = useState<File>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>()

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!file) return
    setBusy(true)
    setError(undefined)
    try {
      onRead((await uploadImportFile(format, file)).token)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit}>
      <Flex direction="column" gap="4" align="start">
        <LabeledSelect label={t('import.file.format')} value={format} onChange={setFormat} options={FORMATS.map((f) => ({ value: f, label: t(`import.file.formats.${f}`) }))} />
        <Text as="p" size="2" color="gray">
          {t('import.file.fuelioHint')}
        </Text>
        <Flex align="center" gap="3" wrap="wrap">
          <input ref={input} type="file" accept=".csv,text/csv" hidden tabIndex={-1} aria-hidden onChange={(e) => setFile(e.target.files?.[0])} />
          <Button type="button" size="3" variant="soft" onClick={() => input.current?.click()}>
            <FileUp size={16} aria-hidden />
            {t('import.file.chooseFile')}
          </Button>
          <Text size="2" role="status">
            {file ? t('import.file.chosen', { name: file.name }) : t('import.file.none')}
          </Text>
        </Flex>
        {error !== undefined && <ErrorMessage error={error} />}
        <Button size="3" type="submit" disabled={!file || busy}>
          {busy ? t('import.file.reading') : t('import.file.upload')}
        </Button>
      </Flex>
    </form>
  )
}

// ---- 2 + 3. target and review -------------------------------------------------------------------------------------

type Target = 'existing' | 'new'

function TargetAndReview({ token, step, onStep, onDone }: { token: string; step: Step; onStep: (s: Step) => void; onDone: (r: Result) => void }) {
  const { t } = useTranslation()
  const [target, setTarget] = useState<Target>('existing')
  const [vehicleId, setVehicleId] = useState('')
  const [newVehicle, setNewVehicle] = useState<{ name: string; licensePlate: string; fuelType: FuelType; distance: DistanceUnit; volume: VolumeUnit }>()
  const [currency, setCurrency] = useState<string>() // undefined until the user types: then the instance default shows
  const [importDuplicates, setImportDuplicates] = useState(false)
  const client = useApolloClient()
  const [confirm, confirmState] = useMutation(ConfirmImportDocument)

  const targets = useQuery(ImportTargetsDocument, { fetchPolicy: 'network-only' })
  const defaults = useQuery(VehicleDefaultsDocument)
  const usable = (targets.data?.vehicles ?? []).filter((v) => v.canEdit || v.logAccess === 'EDIT' || v.logAccess === 'DELETE')
  const preview = useQuery(ImportPreviewDocument, { variables: { token, vehicleId: target === 'existing' && vehicleId ? vehicleId : null }, fetchPolicy: 'network-only' })
  const p = preview.data?.importPreview

  const ready = targets.data && defaults.data && p
  if (preview.error || targets.error) return <ErrorMessage error={preview.error ?? targets.error} />
  if (!ready) {
    return (
      <Text as="p" role="status">
        {t('app.loading')}
      </Text>
    )
  }

  const effectiveCurrency = currency ?? defaults.data!.vehicleDefaults.currency
  const effectiveNew = newVehicle ?? {
    name: p.sourceVehicle?.name ?? '',
    licensePlate: p.sourceVehicle?.licensePlate ?? '',
    fuelType: p.sourceVehicle?.fuelType ?? ('PETROL' as FuelType),
    distance: p.sourceVehicle?.distanceUnit ?? defaults.data!.vehicleDefaults.distanceUnit,
    volume: p.sourceVehicle?.volumeUnit ?? defaults.data!.vehicleDefaults.volumeUnit,
  }
  const canContinue = target === 'existing' ? vehicleId !== '' : effectiveNew.name.trim() !== ''

  async function runImport() {
    const input = {
      token,
      currency: effectiveCurrency,
      importDuplicates,
      ...(target === 'existing'
        ? { vehicleId }
        : { newVehicle: { name: effectiveNew.name, licensePlate: effectiveNew.licensePlate || null, fuelType: effectiveNew.fuelType, units: { distance: effectiveNew.distance, volume: effectiveNew.volume } } }),
    }
    try {
      const { data } = await confirm({ variables: { input } })
      await client.refetchQueries({ include: ['Vehicles', 'Refuelings', 'Expenses', 'VehicleDetails'] })
      if (data) onDone(data.confirmImport)
    } catch {
      // shown through confirmState.error
    }
  }

  if (step === 'target') {
    return (
      <RadixForm.Root
        onSubmit={(e) => {
          e.preventDefault()
          if (canContinue) onStep('review')
        }}
      >
        <Flex direction="column" gap="4" align="start">
          <Heading as="h2" size="4">
            {t('import.target.title')}
          </Heading>
          <RadioGroup.Root value={target} onValueChange={(v) => setTarget(v as Target)} aria-label={t('import.target.title')}>
            <Flex direction="column" gap="2">
              <Text as="label" size="3">
                <Flex gap="2" align="center">
                  <RadioGroup.Item value="existing" /> {t('import.target.existing')}
                </Flex>
              </Text>
              <Text as="label" size="3">
                <Flex gap="2" align="center">
                  <RadioGroup.Item value="new" /> {t('import.target.new')}
                </Flex>
              </Text>
            </Flex>
          </RadioGroup.Root>

          {target === 'existing' &&
            (usable.length === 0 ? (
              <Text as="p" size="2" color="gray">
                {t('import.target.noVehicles')}
              </Text>
            ) : (
              <LabeledSelect label={t('import.target.vehicle')} value={vehicleId} onChange={setVehicleId} placeholder={t('import.target.chooseVehicle')} options={usable.map((v) => ({ value: v.id, label: v.licensePlate ? `${v.name} (${v.licensePlate})` : v.name }))} />
            ))}

          {target === 'new' && (
            <Flex direction="column" gap="3" asChild>
              <fieldset style={{ border: 0, padding: 0, margin: 0, width: '100%', maxWidth: '28rem' }}>
                <legend>
                  <Text size="2" weight="bold">
                    {t('import.target.newDetails')}
                  </Text>
                </legend>
                <Field name="name" label={t('import.target.name')} required>
                  <TextField.Root required value={effectiveNew.name} onChange={(e) => setNewVehicle({ ...effectiveNew, name: e.target.value })} />
                </Field>
                <Field name="plate" label={t('import.target.plate')}>
                  <TextField.Root value={effectiveNew.licensePlate} onChange={(e) => setNewVehicle({ ...effectiveNew, licensePlate: e.target.value })} />
                </Field>
                <LabeledSelect label={t('fields.fuel')} value={effectiveNew.fuelType} onChange={(fuelType) => setNewVehicle({ ...effectiveNew, fuelType })} options={FUEL_TYPES.map((f) => ({ value: f, label: t(`fuel.${f}`) }))} />
                <Text size="2" color="gray">
                  {t('import.review.unitsFromFile', { distance: t(`units.distance.${effectiveNew.distance}`), volume: t(`units.volume.${effectiveNew.volume}`) })}
                </Text>
              </fieldset>
            </Flex>
          )}

          <Box style={{ maxWidth: '12rem' }}>
            <Field
              name="currency"
              label={t('import.target.currency')}
              hint={t('import.target.currencyHint')}
              required
              invalid={{ message: t('errors.money.currencyInvalid'), test: (v) => v !== '' && !/^[A-Za-z]{3}$/.test(v.trim()) }}
            >
              <TextField.Root required maxLength={3} autoComplete="off" style={{ textTransform: 'uppercase' }} value={effectiveCurrency} onChange={(e) => setCurrency(e.target.value.toUpperCase())} />
            </Field>
          </Box>

          <RadixForm.Submit asChild>
            <Button size="3" disabled={!canContinue}>
              {t('import.review.title')}
            </Button>
          </RadixForm.Submit>
        </Flex>
      </RadixForm.Root>
    )
  }

  return (
    <ReviewStep
      preview={p}
      existing={target === 'existing'}
      importDuplicates={importDuplicates}
      onImportDuplicates={setImportDuplicates}
      busy={confirmState.loading}
      error={confirmState.error}
      onBack={() => onStep('target')}
      onConfirm={() => void runImport()}
    />
  )
}

function ReviewStep({
  preview: p,
  existing,
  importDuplicates,
  onImportDuplicates,
  busy,
  error,
  onBack,
  onConfirm,
}: {
  preview: Preview
  existing: boolean
  importDuplicates: boolean
  onImportDuplicates: (value: boolean) => void
  busy: boolean
  error: unknown
  onBack: () => void
  onConfirm: () => void
}) {
  const { t } = useTranslation()
  const { date } = useFormat()
  const hasDuplicates = existing && p.duplicateFuelRows + p.duplicateExpenseRows > 0
  const nothing = p.fuelRows + p.expenseRows === 0

  return (
    <Flex direction="column" gap="4" align="start">
      <Heading as="h2" size="4">
        {t('import.review.title')}
      </Heading>
      <Card>
        <Text as="p">
          {t('import.review.summary', { fuel: p.fuelRows, expenses: p.expenseRows, from: p.firstDate ? date(p.firstDate) : '–', to: p.lastDate ? date(p.lastDate) : '–' })}
        </Text>
        {p.categories.length > 0 && (
          <Text as="p" size="2" color="gray" mt="2">
            {t('import.review.categories', { list: p.categories.join(', ') })}
          </Text>
        )}
      </Card>

      {hasDuplicates && (
        <fieldset style={{ border: 0, padding: 0, margin: 0 }}>
          <legend>
            <Text size="2" weight="bold">
              {t('import.review.duplicateChoice')}
            </Text>
          </legend>
          <Text as="p" size="2" mb="2">
            {t('import.review.duplicates', { fuel: p.duplicateFuelRows, expenses: p.duplicateExpenseRows })}
          </Text>
          <RadioGroup.Root value={importDuplicates ? 'import' : 'skip'} onValueChange={(v) => onImportDuplicates(v === 'import')}>
            <Flex direction="column" gap="2">
              <Text as="label" size="2">
                <Flex gap="2" align="center">
                  <RadioGroup.Item value="skip" /> {t('import.review.skipDuplicates')}
                </Flex>
              </Text>
              <Text as="label" size="2">
                <Flex gap="2" align="center">
                  <RadioGroup.Item value="import" /> {t('import.review.importDuplicates')}
                </Flex>
              </Text>
            </Flex>
          </RadioGroup.Root>
        </fieldset>
      )}

      <Issues issues={p.issues} title={t('import.review.issuesTitle', { count: p.issues.length })} hint={t('import.review.issuesHint')} />
      {error !== undefined && <ErrorMessage error={error} />}

      <Flex gap="3" wrap="wrap">
        <Button size="3" variant="soft" color="gray" type="button" onClick={onBack} disabled={busy}>
          {t('import.review.back')}
        </Button>
        <Button size="3" onClick={onConfirm} disabled={busy || nothing} aria-busy={busy}>
          {busy ? t('import.review.importing') : t('import.review.confirm')}
        </Button>
      </Flex>
    </Flex>
  )
}

/** Rows that were left out or failed, each with its translated reason and where it was in the file. */
function Issues({ issues, title, hint }: { issues: readonly Issue[]; title: string; hint?: string }) {
  const { t } = useTranslation()
  const keyText = useKeyText()
  if (issues.length === 0) return null
  const shown = issues.slice(0, MAX_ISSUES_SHOWN)

  return (
    <Callout.Root color="amber" size="1">
      <Callout.Icon>
        <AlertTriangle size={16} aria-hidden />
      </Callout.Icon>
      <Callout.Text>
        <Text weight="bold">{title}</Text>
        {hint && (
          <Text as="p" size="1">
            {hint}
          </Text>
        )}
        <ul style={{ margin: 'var(--space-2) 0 0', paddingLeft: 'var(--space-4)' }}>
          {shown.map((issue, i) => (
            <li key={`${issue.section}-${issue.row}-${i}`}>
              {issue.row > 0 && <Text color="gray">{t('import.row', { section: t(`import.sections.${issue.section as 'log' | 'costs' | 'vehicle'}`, { defaultValue: issue.section }), row: issue.row })}: </Text>}
              {keyText(issue.key, issue.args) ?? issue.key}
            </li>
          ))}
        </ul>
        {issues.length > shown.length && <Text as="p" size="1">{t('import.review.moreIssues', { count: issues.length - shown.length })}</Text>}
      </Callout.Text>
    </Callout.Root>
  )
}

// ---- 4. done ----------------------------------------------------------------------------------------------------

function DoneStep({ result, onAnother }: { result: Result; onAnother: () => void }) {
  const { t } = useTranslation()
  const skipped = result.fuelSkippedDuplicates + result.expensesSkippedDuplicates

  return (
    <Flex direction="column" gap="3" align="start">
      <Heading as="h2" size="4">
        {t('import.done.title')}
      </Heading>
      <SuccessMessage>{t('import.done.imported', { fuel: result.fuelImported, expenses: result.expensesImported })}</SuccessMessage>
      {skipped > 0 && (
        <Text as="p" size="2">
          {t('import.done.skipped', { fuel: result.fuelSkippedDuplicates, expenses: result.expensesSkippedDuplicates })}
        </Text>
      )}
      <Issues issues={result.errors} title={t('import.done.errorsTitle', { count: result.errors.length })} />
      <Flex gap="3" wrap="wrap">
        <Button size="3" asChild>
          <Link to={`/vehicles/${result.vehicleId}`}>{t('import.done.open')}</Link>
        </Button>
        <Button size="3" variant="soft" onClick={onAnother}>
          {t('import.done.another')}
        </Button>
      </Flex>
    </Flex>
  )
}
