import { useApolloClient, useMutation, useQuery } from '@apollo/client/react'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Card from '@mui/material/Card'
import FormControlLabel from '@mui/material/FormControlLabel'
import Radio from '@mui/material/Radio'
import RadioGroup from '@mui/material/RadioGroup'
import Stack from '@mui/material/Stack'
import Step from '@mui/material/Step'
import StepLabel from '@mui/material/StepLabel'
import Stepper from '@mui/material/Stepper'
import Typography from '@mui/material/Typography'
import { AlertTriangle, FileUp } from 'lucide-react'
import { useRef, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { LabeledSelect } from '../components/UnitSelect.tsx'
import { CurrencyInput } from '../forms/CurrencyInput.tsx'
import { Field } from '../forms/Field.tsx'
import { FieldInput } from '../forms/FieldInput.tsx'
import { Form } from '../forms/Form.tsx'
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
import { Loading } from '../components/Loading.tsx'

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

  const current = STEPS.indexOf(step)
  return (
    <section aria-labelledby="page-title">
      <Typography id="page-title" component="h1" variant="h3" sx={{ mb: 1 }}>
        {t('import.title')}
      </Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary', mb: 1.5 }}>
        {t('import.intro')}
      </Typography>
      {/* An ordered list: a screen reader hears the step's place; the current one is marked. */}
      <Stepper activeStep={current} aria-label={t('import.title')} sx={{ mb: 2, flexWrap: 'wrap', rowGap: 1 }}>
        {STEPS.map((s, i) => (
          <Step key={s} completed={i < current || step === 'done'} aria-current={s === step ? 'step' : undefined}>
            <StepLabel>{t(`import.steps.${s}`)}</StepLabel>
          </Step>
        ))}
      </Stepper>

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
      <Stack sx={{ gap: 2, alignItems: 'flex-start' }}>
        <LabeledSelect label={t('import.file.format')} value={format} onChange={setFormat} options={FORMATS.map((f) => ({ value: f, label: t(`import.file.formats.${f}`) }))} />
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {t('import.file.fuelioHint')}
        </Typography>
        <Stack direction="row" sx={{ alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
          <input ref={input} type="file" accept=".csv,text/csv" hidden tabIndex={-1} aria-hidden onChange={(e) => setFile(e.target.files?.[0])} />
          <Button type="button" size="large" variant="soft" onClick={() => input.current?.click()}>
            <FileUp size={16} aria-hidden />
            {t('import.file.chooseFile')}
          </Button>
          <Typography variant="body2" role="status">
            {file ? t('import.file.chosen', { name: file.name }) : t('import.file.none')}
          </Typography>
        </Stack>
        {error !== undefined && <ErrorMessage error={error} />}
        <Button size="large" type="submit" disabled={!file || busy}>
          {busy ? t('import.file.reading') : t('import.file.upload')}
        </Button>
      </Stack>
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
  const usable = (targets.data?.myVehicles ?? []).filter((v) => v.canEdit || v.logAccess === 'EDIT' || v.logAccess === 'DELETE')
  const preview = useQuery(ImportPreviewDocument, { variables: { token, vehicleId: target === 'existing' && vehicleId ? vehicleId : null }, fetchPolicy: 'network-only' })
  const p = preview.data?.importPreview

  const ready = targets.data && defaults.data && p
  if (preview.error || targets.error) return <ErrorMessage error={preview.error ?? targets.error} />
  if (!ready) {
    return (
      <Loading />
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
      <Form
        onSubmit={(e) => {
          e.preventDefault()
          if (canContinue) onStep('review')
        }}
      >
        <Stack sx={{ gap: 2, alignItems: 'flex-start' }}>
          <Typography component="h2" variant="h5">
            {t('import.target.title')}
          </Typography>
          <Choice
            label={t('import.target.title')}
            value={target}
            onChange={setTarget}
            options={[
              { value: 'existing', label: t('import.target.existing') },
              { value: 'new', label: t('import.target.new') },
            ]}
          />

          {target === 'existing' &&
            (usable.length === 0 ? (
              <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                {t('import.target.noVehicles')}
              </Typography>
            ) : (
              <LabeledSelect label={t('import.target.vehicle')} value={vehicleId} onChange={setVehicleId} placeholder={t('import.target.chooseVehicle')} options={usable.map((v) => ({ value: v.id, label: v.licensePlate ? `${v.name} (${v.licensePlate})` : v.name }))} />
            ))}

          {target === 'new' && (
            <Stack component="fieldset" sx={{ gap: 1.5, border: 0, p: 0, m: 0, width: '100%', maxWidth: '28rem', minWidth: 0 }}>
              <Typography component="legend" variant="body2" sx={{ fontWeight: 700, p: 0, mb: 1.5 }}>
                {t('import.target.newDetails')}
              </Typography>
              <Field name="name" label={t('import.target.name')} required>
                <FieldInput value={effectiveNew.name} onChange={(e) => setNewVehicle({ ...effectiveNew, name: e.target.value })} />
              </Field>
              <Field name="plate" label={t('import.target.plate')}>
                <FieldInput value={effectiveNew.licensePlate} onChange={(e) => setNewVehicle({ ...effectiveNew, licensePlate: e.target.value })} />
              </Field>
              <LabeledSelect label={t('fields.fuel')} value={effectiveNew.fuelType} onChange={(fuelType) => setNewVehicle({ ...effectiveNew, fuelType })} options={FUEL_TYPES.map((f) => ({ value: f, label: t(`fuel.${f}`) }))} />
              <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                {t('import.review.unitsFromFile', { distance: t(`units.distance.${effectiveNew.distance}`), volume: t(`units.volume.${effectiveNew.volume}`) })}
              </Typography>
            </Stack>
          )}

          <Box sx={{ width: '100%', maxWidth: '16rem' }}>
            <Field
              name="currency"
              label={t('import.target.currency')}
              hint={t('import.target.currencyHint')}
              required
              invalid={{ message: t('errors.money.currencyInvalid'), test: (v) => v !== '' && !/^[A-Za-z]{3}$/.test(v.trim()) }}
            >
              <CurrencyInput value={effectiveCurrency} onChange={(v) => setCurrency(v.toUpperCase())} preferred={defaults.data!.vehicleDefaults.currency} />
            </Field>
          </Box>

          <Button size="large" type="submit" disabled={!canContinue}>
            {t('import.review.title')}
          </Button>
        </Stack>
      </Form>
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
  const hasDuplicates = existing && p.duplicateFuelRows + p.duplicateExpenseRows + p.duplicateRecurringRows > 0
  const nothing = p.fuelRows + p.expenseRows + p.recurringRows === 0

  return (
    <Stack sx={{ gap: 2, alignItems: 'flex-start' }}>
      <Typography component="h2" variant="h5">
        {t('import.review.title')}
      </Typography>
      <Card sx={{ p: 1.5 }}>
        <Typography>
          {t('import.review.summary', { fuel: p.fuelRows, expenses: p.expenseRows, recurring: p.recurringRows, from: p.firstDate ? date(p.firstDate) : '–', to: p.lastDate ? date(p.lastDate) : '–' })}
        </Typography>
        {p.categories.length > 0 && (
          <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
            {t('import.review.categories', { list: p.categories.join(', ') })}
          </Typography>
        )}
      </Card>

      {hasDuplicates && (
        <Box component="fieldset" sx={{ border: 0, p: 0, m: 0, minWidth: 0 }}>
          <Typography component="legend" variant="body2" sx={{ fontWeight: 700, p: 0 }}>
            {t('import.review.duplicateChoice')}
          </Typography>
          <Typography variant="body2" sx={{ mb: 1 }}>
            {t('import.review.duplicates', { fuel: p.duplicateFuelRows, expenses: p.duplicateExpenseRows, recurring: p.duplicateRecurringRows })}
          </Typography>
          <Choice
            value={importDuplicates ? 'import' : 'skip'}
            onChange={(v) => onImportDuplicates(v === 'import')}
            small
            options={[
              { value: 'skip', label: t('import.review.skipDuplicates') },
              { value: 'import', label: t('import.review.importDuplicates') },
            ]}
          />
        </Box>
      )}

      <Issues issues={p.issues} title={t('import.review.issuesTitle', { count: p.issues.length })} hint={t('import.review.issuesHint')} />
      {error !== undefined && <ErrorMessage error={error} />}

      <Stack direction="row" sx={{ gap: 1.5, flexWrap: 'wrap' }}>
        <Button size="large" variant="soft" color="neutral" type="button" onClick={onBack} disabled={busy}>
          {t('import.review.back')}
        </Button>
        <Button size="large" onClick={onConfirm} disabled={busy || nothing} aria-busy={busy}>
          {busy ? t('import.review.importing') : t('import.review.confirm')}
        </Button>
      </Stack>
    </Stack>
  )
}

/** One of a few choices as radio buttons, one under the other; `label` names the group when no fieldset's legend does. */
function Choice<V extends string>({ label, value, onChange, options, small }: { label?: string; value: V; onChange: (value: V) => void; options: { value: V; label: ReactNode }[]; small?: boolean }) {
  return (
    <RadioGroup aria-label={label} value={value} onChange={(_, next) => onChange(next as V)}>
      {options.map((o) => (
        <FormControlLabel key={o.value} value={o.value} control={<Radio />} label={o.label} slotProps={{ typography: { variant: small ? 'body2' : 'body1' } }} sx={{ minHeight: 44, ml: -1 }} />
      ))}
    </RadioGroup>
  )
}

/** Rows that were left out or failed, each with its translated reason and where it was in the file. */
function Issues({ issues, title, hint }: { issues: readonly Issue[]; title: string; hint?: string }) {
  const { t } = useTranslation()
  const keyText = useKeyText()
  if (issues.length === 0) return null
  const shown = issues.slice(0, MAX_ISSUES_SHOWN)

  return (
    <Alert severity="warning" icon={<AlertTriangle size={16} aria-hidden />} role="none">
      <Typography variant="body2" sx={{ fontWeight: 700 }}>
        {title}
      </Typography>
      {hint && <Typography variant="caption" component="p">{hint}</Typography>}
      <Box component="ul" sx={{ mt: 1, mb: 0, pl: 2 }}>
        {shown.map((issue, i) => (
          <li key={`${issue.section}-${issue.row}-${i}`}>
            {issue.row > 0 && (
              <Box component="span" sx={{ color: 'text.secondary' }}>
                {t('import.row', { section: t(`import.sections.${issue.section as 'log' | 'costs' | 'vehicle'}`, { defaultValue: issue.section }), row: issue.row })}:{' '}
              </Box>
            )}
            {keyText(issue.key, issue.args) ?? issue.key}
          </li>
        ))}
      </Box>
      {issues.length > shown.length && (
        <Typography variant="caption" component="p">
          {t('import.review.moreIssues', { count: issues.length - shown.length })}
        </Typography>
      )}
    </Alert>
  )
}

// ---- 4. done ----------------------------------------------------------------------------------------------------

function DoneStep({ result, onAnother }: { result: Result; onAnother: () => void }) {
  const { t } = useTranslation()
  const skipped = result.fuelSkippedDuplicates + result.expensesSkippedDuplicates + result.recurringSkippedDuplicates

  return (
    <Stack sx={{ gap: 1.5, alignItems: 'flex-start' }}>
      <Typography component="h2" variant="h5">
        {t('import.done.title')}
      </Typography>
      <SuccessMessage>{t('import.done.imported', { fuel: result.fuelImported, expenses: result.expensesImported, recurring: result.recurringImported })}</SuccessMessage>
      {skipped > 0 && (
        <Typography variant="body2">
          {t('import.done.skipped', { fuel: result.fuelSkippedDuplicates, expenses: result.expensesSkippedDuplicates, recurring: result.recurringSkippedDuplicates })}
        </Typography>
      )}
      <Issues issues={result.errors} title={t('import.done.errorsTitle', { count: result.errors.length })} />
      <Stack direction="row" sx={{ gap: 1.5, flexWrap: 'wrap' }}>
        <Button component={Link} to={`/vehicles/${result.vehicleId}`} size="large">
          {t('import.done.open')}
        </Button>
        <Button size="large" variant="soft" onClick={onAnother}>
          {t('import.done.another')}
        </Button>
      </Stack>
    </Stack>
  )
}
