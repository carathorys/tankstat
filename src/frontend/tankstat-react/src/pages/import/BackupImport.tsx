import { useApolloClient, useMutation, useQuery } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Card from '@mui/material/Card'
import Checkbox from '@mui/material/Checkbox'
import FormControlLabel from '@mui/material/FormControlLabel'
import LinearProgress from '@mui/material/LinearProgress'
import Radio from '@mui/material/Radio'
import RadioGroup from '@mui/material/RadioGroup'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useEffect, useId, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { Loading } from '../../components/Loading.tsx'
import { Segmented } from '../../components/Segmented.tsx'
import {
  BackupImportCarsDocument,
  BackupImportReviewDocument,
  CancelImportRunDocument,
  ImportRunDocument,
  StartBackupImportDocument,
  VehicleDefaultsDocument,
  type BackupImportCarsQuery,
  type DistanceUnit,
  type ImportRunFieldsFragment,
  type RefusedEntryPolicy,
  type VolumeUnit,
} from '../../gql/generated.ts'
import { useKeyText } from '../../i18n/errors.ts'
import { useFormat } from '../../i18n/format.ts'
import { ErrorMessage, SuccessMessage } from '../../messages.tsx'
import { BackupFileStep } from './BackupFileStep.tsx'

export type BackupStep = 'file' | 'cars' | 'review' | 'done'

/** How often a run that goes or waits is asked about. */
export const RUN_POLL_MS = 2000

type Offers = BackupImportCarsQuery['backupImportCars']
type Offer = Offers['cars'][number]

/** What the person chose for a car of the file: whether it comes, as a new car or into one of their own, and what a refused entry does. */
interface Choice {
  picked: boolean
  asNew: boolean
  target: string | null
  policy: RefusedEntryPolicy
}

const active = (run: ImportRunFieldsFragment | undefined) => run?.state === 'WAITING' || run?.state === 'RUNNING'

/**
 * Importing cars from a Tankstat backup or export (#337), the steps after the source is chosen: the file (#336's step), the cars,
 * the review, then the run (waiting in line, running, its report or why nothing came in). The page owns the stepper and tells this
 * which step shows; a run found going or waiting when the page opens, or a report opened from its notification, comes in as `run`.
 */
export function BackupImport({
  step,
  onStep,
  run: found,
  onAnother,
  pollMs = RUN_POLL_MS,
}: {
  step: BackupStep
  onStep: (step: BackupStep) => void
  run?: ImportRunFieldsFragment
  onAnother: () => void
  pollMs?: number
}) {
  const [token, setToken] = useState<string | undefined>(found?.fileToken ?? undefined)
  const [choices, setChoices] = useState<Record<string, Choice>>({})
  const [run, setRun] = useState<ImportRunFieldsFragment | undefined>(found)

  if (run && (step === 'review' || step === 'done')) {
    return (
      <RunView
        run={run}
        pollMs={pollMs}
        onChange={(next) => {
          setRun(next)
          if (!active(next)) onStep('done')
        }}
        onBackToReview={
          run.fileToken
            ? () => {
                setToken(run.fileToken ?? undefined)
                setRun(undefined)
                onStep('review')
              }
            : undefined
        }
        onAnother={onAnother}
      />
    )
  }
  if (step === 'file' || !token) {
    return (
      <BackupFileStep
        onOpened={(opened) => {
          setToken(opened)
          setChoices({})
          onStep('cars')
        }}
      />
    )
  }
  if (step === 'cars') return <CarsStep token={token} choices={choices} onChoices={setChoices} onBack={() => onStep('file')} onNext={() => onStep('review')} />
  return (
    <ReviewStep
      token={token}
      choices={choices}
      onChoices={setChoices}
      onBack={() => onStep('cars')}
      onStarted={(started) => {
        setRun(started)
        if (!active(started)) onStep('done')
      }}
    />
  )
}

/** A car's units in short words ("km, L"). */
function useUnits() {
  const { t } = useTranslation()
  return (distance: DistanceUnit, volume: VolumeUnit) => `${t(`units.distanceShort.${distance}`)}, ${t(`units.volumeShort.${volume}`)}`
}

/** The choices as the server takes them: the picked cars, each new or into its target. */
const toInput = (choices: Record<string, Choice>) =>
  Object.entries(choices)
    .filter(([, c]) => c.picked)
    .map(([carId, c]) => ({ carId, targetVehicleId: c.asNew ? null : c.target, policy: c.policy }))

// ---- 2. the cars ---------------------------------------------------------------------------------------------

function CarsStep({
  token,
  choices,
  onChoices,
  onBack,
  onNext,
}: {
  token: string
  choices: Record<string, Choice>
  onChoices: (next: Record<string, Choice>) => void
  onBack: () => void
  onNext: () => void
}) {
  const { t } = useTranslation()
  const { date } = useFormat()
  const unitsOf = useUnits()
  const offers = useQuery(BackupImportCarsDocument, { variables: { token }, fetchPolicy: 'network-only' })
  const defaults = useQuery(VehicleDefaultsDocument)
  const data = offers.data?.backupImportCars

  if (offers.error) return <ErrorMessage error={offers.error} />
  if (!data) return <Loading />

  // A car the person has not touched yet: not picked; once picked, the app's match if it has one, else a new car.
  const choiceOf = (offer: Offer): Choice => choices[offer.car.id] ?? { picked: false, asNew: offer.suggestedTargetId == null, target: offer.suggestedTargetId ?? null, policy: 'IMPORT_THE_REST' }
  const set = (offer: Offer, change: Partial<Choice>) => onChoices({ ...choices, [offer.car.id]: { ...choiceOf(offer), ...change } })
  const picked = data.cars.filter((o) => choiceOf(o).picked)
  const problemOf = (offer: Offer): string | null => {
    const choice = choiceOf(offer)
    if (!choice.picked || choice.asNew) return null
    const target = data.targets.find((v) => v.id === choice.target)
    if (!target) return t('import.cars.neither')
    if (target.distanceUnit !== offer.car.distanceUnit || target.volumeUnit !== offer.car.volumeUnit)
      return t('import.cars.unitsDiffer', {
        car: offer.car.name,
        units: unitsOf(offer.car.distanceUnit, offer.car.volumeUnit),
        target: target.name,
        targetUnits: unitsOf(target.distanceUnit, target.volumeUnit),
      })
    if (picked.some((other) => other.car.id !== offer.car.id && !choiceOf(other).asNew && choiceOf(other).target === choice.target)) return t('import.cars.taken', { target: target.name })
    return null
  }
  const ready = picked.length > 0 && picked.every((o) => problemOf(o) === null)
  const usual = defaults.data?.vehicleDefaults

  return (
    <Stack sx={{ gap: 2, alignItems: 'stretch' }}>
      <Typography variant="body2">{t('import.cars.intro', { date: date(data.file.createdAt.slice(0, 10)), version: data.file.version, count: data.cars.length })}</Typography>
      {data.file.trashedCars > 0 && (
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {t('import.cars.trashed', { count: data.file.trashedCars })}
        </Typography>
      )}
      {data.cars.map((offer) => (
        <CarCard
          key={offer.car.id}
          offer={offer}
          targets={data.targets}
          choice={choiceOf(offer)}
          problem={problemOf(offer)}
          showUnits={!usual || usual.distanceUnit !== offer.car.distanceUnit || usual.volumeUnit !== offer.car.volumeUnit}
          onChange={(change) => set(offer, change)}
        />
      ))}
      {picked.length === 0 && (
        <Typography variant="body2" role="status">
          {t('import.cars.none')}
        </Typography>
      )}
      <Stack direction="row" sx={{ gap: 1.5, flexWrap: 'wrap' }}>
        <Button size="large" variant="soft" onClick={onBack}>
          {t('import.cars.back')}
        </Button>
        <Button size="large" disabled={!ready} onClick={onNext}>
          {t('import.cars.next')}
        </Button>
      </Stack>
    </Stack>
  )
}

function CarCard({
  offer,
  targets,
  choice,
  problem,
  showUnits,
  onChange,
}: {
  offer: Offer
  targets: Offers['targets']
  choice: Choice
  problem: string | null
  showUnits: boolean
  onChange: (change: Partial<Choice>) => void
}) {
  const { t } = useTranslation()
  const unitsOf = useUnits()
  const heading = useId()
  const group = useId()
  const note = useId()
  const { car } = offer
  return (
    <Card component="section" aria-labelledby={heading} sx={{ p: 2 }}>
      <Stack sx={{ gap: 1 }}>
        <FormControlLabel
          control={<Checkbox checked={choice.picked} onChange={(e) => onChange({ picked: e.target.checked })} />}
          label={
            <Typography id={heading} component="h2" variant="h6">
              {car.name}
              {car.licensePlate ? ` · ${car.licensePlate}` : ''}
            </Typography>
          }
        />
        <Typography variant="body2">{t('import.cars.counts', { fuel: car.refuelings, expenses: car.expenses, recurring: car.schedules })}</Typography>
        {!offer.ownedByMe && car.ownerName && (
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            {t('import.cars.owner', { name: car.ownerName })}
          </Typography>
        )}
        {showUnits && (
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            {t('import.cars.units', { units: unitsOf(car.distanceUnit, car.volumeUnit) })}
          </Typography>
        )}
        {choice.picked && (
          <>
            <FormControlLabel control={<Checkbox checked={choice.asNew} onChange={(e) => onChange({ asNew: e.target.checked })} />} label={t('import.cars.asNew')} />
            {!choice.asNew &&
              (targets.length === 0 ? (
                <Typography variant="body2">{t('import.cars.noOwnCars')}</Typography>
              ) : (
                <>
                  <Typography id={group} variant="body2">
                    {t('import.cars.target')}
                  </Typography>
                  <RadioGroup aria-labelledby={group} aria-describedby={problem ? note : undefined} value={choice.target ?? ''} onChange={(e) => onChange({ target: e.target.value })}>
                    {targets.map((target) => (
                      <FormControlLabel
                        key={target.id}
                        value={target.id}
                        control={<Radio />}
                        label={`${target.name}${target.licensePlate ? ` · ${target.licensePlate}` : ''}${target.id === offer.suggestedTargetId ? ` · ${t('import.cars.suggested')}` : ''}`}
                      />
                    ))}
                  </RadioGroup>
                </>
              ))}
            {problem && (
              <Typography id={note} variant="body2" role="status" sx={{ color: 'error.main' }}>
                {problem}
              </Typography>
            )}
          </>
        )}
      </Stack>
    </Card>
  )
}

// ---- 3. the review -------------------------------------------------------------------------------------------

function ReviewStep({
  token,
  choices,
  onChoices,
  onBack,
  onStarted,
}: {
  token: string
  choices: Record<string, Choice>
  onChoices: (next: Record<string, Choice>) => void
  onBack: () => void
  onStarted: (run: ImportRunFieldsFragment) => void
}) {
  const { t } = useTranslation()
  // What is compared is the cars and their targets: a policy changed here asks nothing again.
  const compared = useMemo(
    () => toInput(choices).map(({ carId, targetVehicleId }) => ({ carId, targetVehicleId })),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [JSON.stringify(toInput(choices).map((c) => [c.carId, c.targetVehicleId]))],
  )
  const review = useQuery(BackupImportReviewDocument, { variables: { token, cars: compared }, fetchPolicy: 'network-only' })
  const [start, started] = useMutation(StartBackupImportDocument)

  if (review.error) {
    return (
      <Stack sx={{ gap: 1.5, alignItems: 'flex-start' }}>
        <ErrorMessage error={review.error} />
        <Button size="large" variant="soft" onClick={onBack}>
          {t('import.plan.back')}
        </Button>
      </Stack>
    )
  }
  if (!review.data) return <Loading />

  return (
    <Stack sx={{ gap: 2, alignItems: 'stretch' }}>
      {review.data.backupImportReview.map((car) => (
        <ReviewCard key={car.carId} car={car} policy={choices[car.carId]?.policy ?? 'IMPORT_THE_REST'} onPolicy={(policy) => onChoices({ ...choices, [car.carId]: { ...choices[car.carId], policy } })} />
      ))}
      <Typography variant="body2">{t('import.plan.yours')}</Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary' }}>
        {t('import.plan.mayWait')}
      </Typography>
      {started.error && <ErrorMessage error={started.error} />}
      <Stack direction="row" sx={{ gap: 1.5, flexWrap: 'wrap' }}>
        <Button size="large" variant="soft" disabled={started.loading} onClick={onBack}>
          {t('import.plan.back')}
        </Button>
        <Button
          size="large"
          loading={started.loading}
          onClick={() => {
            void start({ variables: { input: { token, cars: toInput(choices) } } })
              .then(({ data }) => data && onStarted(data.startBackupImport))
              .catch(() => undefined) // said under the cards
          }}
        >
          {t('import.plan.import')}
        </Button>
      </Stack>
    </Stack>
  )
}

function ReviewCard({
  car,
  policy,
  onPolicy,
}: {
  car: { name: string; ownerName?: string | null; ownedByMe: boolean; targetName?: string | null; newRefuelings: number; newExpenses: number; newSchedules: number; existingRefuelings: number; existingExpenses: number; existingSchedules: number; differing: number; inTrash: number }
  policy: RefusedEntryPolicy
  onPolicy: (policy: RefusedEntryPolicy) => void
}) {
  const { t } = useTranslation()
  const heading = useId()
  const existing = car.existingRefuelings + car.existingExpenses + car.existingSchedules
  return (
    <Card component="section" aria-labelledby={heading} sx={{ p: 2 }}>
      <Stack sx={{ gap: 1 }}>
        <Typography id={heading} component="h2" variant="h6">
          {car.targetName ? t('import.plan.cardAdded', { car: car.name, target: car.targetName }) : t('import.plan.cardNew', { car: car.name })}
        </Typography>
        {!car.ownedByMe && car.ownerName && (
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            {t('import.plan.owner', { name: car.ownerName })}
          </Typography>
        )}
        <Stack component="ul" sx={{ gap: 0.5, m: 0, pl: 2.5 }}>
          <Typography component="li" variant="body2">
            {t('import.plan.new', { fuel: car.newRefuelings, expenses: car.newExpenses, recurring: car.newSchedules })}
          </Typography>
          {existing > 0 && (
            <Typography component="li" variant="body2">
              {t('import.plan.existing', { fuel: car.existingRefuelings, expenses: car.existingExpenses, recurring: car.existingSchedules })}
            </Typography>
          )}
          {car.differing > 0 && (
            <Typography component="li" variant="body2">
              {t('import.plan.differ', { count: car.differing })}
            </Typography>
          )}
          {car.inTrash > 0 && (
            <Typography component="li" variant="body2">
              {t('import.plan.inTrash', { count: car.inTrash, target: car.targetName ?? '' })}
            </Typography>
          )}
          {car.targetName && (
            <Typography component="li" variant="body2">
              {t('import.plan.pastFigures', { target: car.targetName })}
            </Typography>
          )}
        </Stack>
        <Typography variant="body2">{t('import.plan.policy')}</Typography>
        <Segmented
          label={`${car.name}: ${t('import.plan.policy')}`}
          value={policy}
          onChange={onPolicy}
          options={[
            { value: 'IMPORT_THE_REST', label: t('import.plan.policyRest') },
            { value: 'LEAVE_CAR_OUT', label: t('import.plan.policyLeaveOut') },
          ]}
        />
      </Stack>
    </Card>
  )
}

// ---- the run: waiting, running, done, or nothing imported -------------------------------------------------------

function RunView({
  run,
  pollMs,
  onChange,
  onBackToReview,
  onAnother,
}: {
  run: ImportRunFieldsFragment
  pollMs: number
  onChange: (run: ImportRunFieldsFragment) => void
  onBackToReview?: () => void
  onAnother: () => void
}) {
  const { t } = useTranslation()
  const client = useApolloClient()
  const [cancel, cancelling] = useMutation(CancelImportRunDocument)
  const going = active(run)

  // While it goes or waits the server is asked again; what it answers is the page's state (also after a cancel).
  useEffect(() => {
    if (!going) return
    let stopped = false
    const timer = setInterval(() => {
      void client
        .query({ query: ImportRunDocument, variables: { id: run.id }, fetchPolicy: 'network-only' })
        .then(({ data }) => {
          if (!stopped && data?.importRun) onChange(data.importRun)
        })
        .catch(() => undefined) // out of reach for a moment: the next ask says
    }, pollMs)
    return () => {
      stopped = true
      clearInterval(timer)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [going, run.id, pollMs, client])

  if (going) {
    const { progress } = run
    return (
      <Card sx={{ p: 2 }}>
        <Stack sx={{ gap: 1.5, alignItems: 'flex-start' }}>
          <Typography role="status">
            {run.state === 'WAITING'
              ? `${t('import.run.waiting', { count: run.ahead })} ${t('import.run.fileKept')}`
              : t('import.run.running', { car: progress.car, cars: progress.cars, done: progress.done, total: progress.total })}
          </Typography>
          <LinearProgress
            aria-hidden
            sx={{ alignSelf: 'stretch' }}
            variant={run.state === 'WAITING' || progress.total === 0 ? 'indeterminate' : 'determinate'}
            value={progress.total === 0 ? undefined : Math.round((progress.done / progress.total) * 100)}
          />
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            {t('import.run.mayLeave')}
          </Typography>
          {cancelling.error && <ErrorMessage error={cancelling.error} />}
          <ConfirmDialog
            trigger={
              <Button size="large" variant="soft" color="error" disabled={cancelling.loading}>
                {t('import.run.cancel')}
              </Button>
            }
            title={t('import.run.cancelTitle')}
            description={t('import.run.cancelText')}
            confirmLabel={t('import.run.cancel')}
            onConfirm={() => void cancel({ variables: { id: run.id } }).catch(() => undefined)}
          />
        </Stack>
      </Card>
    )
  }
  if (run.state === 'DONE' && run.report) return <Report run={run} onAnother={onAnother} />
  return (
    <Stack sx={{ gap: 1.5, alignItems: 'flex-start' }}>
      <ErrorMessage>
        {run.state === 'DONE' ? t('import.run.gone') : `${t(`import.run.failure.${run.failure ?? 'FAILED'}`, { minutes: run.limitMinutes })} ${t('import.run.nothing')}`}
      </ErrorMessage>
      <Stack direction="row" sx={{ gap: 1.5, flexWrap: 'wrap' }}>
        {onBackToReview && run.state !== 'DONE' && (
          <Button size="large" onClick={onBackToReview}>
            {t('import.run.backToReview')}
          </Button>
        )}
        <Button size="large" variant="soft" onClick={onAnother}>
          {t('import.run.another')}
        </Button>
      </Stack>
    </Stack>
  )
}

function Report({ run, onAnother }: { run: ImportRunFieldsFragment; onAnother: () => void }) {
  const { t } = useTranslation()
  const { date } = useFormat()
  const keyText = useKeyText()
  const cars = run.report ?? []
  return (
    <Stack sx={{ gap: 2, alignItems: 'stretch' }}>
      <SuccessMessage>{t('import.run.finished', { count: cars.filter((c) => !c.leftOut).length })}</SuccessMessage>
      {cars.map((car) => (
        <Card key={car.carId} component="section" aria-label={car.name} sx={{ p: 2 }}>
          <Stack sx={{ gap: 1, alignItems: 'flex-start' }}>
            <Typography component="h2" variant="h6">
              {car.targetName ? t('import.plan.cardAdded', { car: car.name, target: car.targetName }) : t('import.plan.cardNew', { car: car.name })}
            </Typography>
            {car.leftOut ? (
              <Typography variant="body2">{t('import.run.leftOut')}</Typography>
            ) : (
              <Typography variant="body2">{t('import.run.imported', { fuel: car.refuelings, expenses: car.expenses, recurring: car.schedules })}</Typography>
            )}
            {!car.leftOut && car.existing + car.differing + car.inTrash > 0 && (
              <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                {t('import.run.skipped', { existing: car.existing, differing: car.differing, inTrash: car.inTrash })}
              </Typography>
            )}
            {car.refused.length > 0 && (
              <>
                <Typography variant="body2">{t('import.run.refused', { count: car.refused.length + car.refusedMore })}</Typography>
                <Stack component="ul" sx={{ gap: 0.5, m: 0, pl: 2.5 }}>
                  {car.refused.map((entry, i) => (
                    <Typography key={i} component="li" variant="body2">
                      {t(`import.run.entry.${entry.kind === 'expense' || entry.kind === 'recurring' ? entry.kind : 'refueling'}`, { date: entry.date ? date(entry.date) : '', title: entry.title ?? '' })}
                      {': '}
                      {keyText(entry.key, (entry.args ?? {}) as Record<string, unknown>) ?? entry.key}
                    </Typography>
                  ))}
                </Stack>
                {car.refusedMore > 0 && <Typography variant="body2">{t('import.run.refusedMore', { count: car.refusedMore })}</Typography>}
              </>
            )}
            {car.vehicleId && !car.leftOut && (
              <Button component={Link} to={`/vehicles/${car.vehicleId}`} size="large" variant="soft" aria-label={`${t('import.run.open')}: ${car.targetName ?? car.name}`}>
                {t('import.run.open')}
              </Button>
            )}
          </Stack>
        </Card>
      ))}
      <Stack direction="row" sx={{ gap: 1.5, flexWrap: 'wrap' }}>
        <Button size="large" variant="soft" onClick={onAnother}>
          {t('import.run.another')}
        </Button>
      </Stack>
    </Stack>
  )
}
