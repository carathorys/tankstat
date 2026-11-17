import { useApolloClient, useMutation, useQuery } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Card from '@mui/material/Card'
import Checkbox from '@mui/material/Checkbox'
import FormControlLabel from '@mui/material/FormControlLabel'
import LinearProgress from '@mui/material/LinearProgress'
import Radio from '@mui/material/Radio'
import RadioGroup from '@mui/material/RadioGroup'
import Stack from '@mui/material/Stack'
import Step from '@mui/material/Step'
import StepLabel from '@mui/material/StepLabel'
import Stepper from '@mui/material/Stepper'
import Typography from '@mui/material/Typography'
import { useEffect, useId, useMemo, useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router'
import { Loading } from '../components/Loading.tsx'
import { PasswordInput } from '../forms/PasswordInput.tsx'
import { Segmented } from '../components/Segmented.tsx'
import { LabeledSelect } from '../components/UnitSelect.tsx'
import { Field } from '../forms/Field.tsx'
import { FieldInput } from '../forms/FieldInput.tsx'
import { Form } from '../forms/Form.tsx'
import {
  CarRestoreDocument,
  EndCarRestoreDocument,
  ImportRunDocument,
  MyRestoreRunDocument,
  OpenCarRestoreDocument,
  RestoreCarsDocument,
  RestorePeopleDocument,
  RestoreReviewDocument,
  SessionDocument,
  StartCarRestoreDocument,
  SubmitCarRestoreKeyDocument,
  type BackupImportFieldsFragment,
  type ImportRunFieldsFragment,
  type RefusedEntryPolicy,
  type RestoreCarsQuery,
} from '../gql/generated.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { ApiError } from '../pictures/ApiError.ts'
import { useErrorText } from '../i18n/errors.ts'
import { useFormat } from '../i18n/format.ts'
import { validLocale } from '../i18n/locale.ts'
import { ErrorMessage } from '../messages.tsx'
import { ReviewCard, RunView, RUN_POLL_MS } from '../pages/import/BackupImport.tsx'
import { KindsDisclosure, ReplaceConfirmations, type Replacement } from '../pages/import/BackupKinds.tsx'
import { defaultKinds, defaultMedia, defaultTyres, kindsInput, mediaInput, nothingChosen, replaces, tyresInput, type Kind, type Kinds, type MediaChoice, type TyreChoice } from '../pages/import/backupKinds.ts'
import { active, RestoreMode } from '../pages/import/importMode.ts'

type Step = 'key' | 'cars' | 'review' | 'done'
const STEPS: Step[] = ['key', 'cars', 'review', 'done']

/** How many cars a page of the Cars step shows. */
export const RESTORE_PAGE_SIZE = 10

/** How often the opening of the restore point is asked about. */
export const OPENING_POLL_MS = 1000

type Offer = RestoreCarsQuery['restoreCars']['cars'][number]

/** What the administrator chose for a car of the restore point: whether it comes, who gets it when its owner is gone, and its kinds. */
interface Choice {
  picked: boolean
  offer: Offer
  /** For a car whose owner has no account: who gets it (the administrator unless another is picked, the owner's decision 3). */
  ownerId: string | null
  policy: RefusedEntryPolicy
  kinds: Kinds
  tyres: TyreChoice
  media: MediaChoice
}

const fresh = (offer: Offer): Choice => ({ picked: false, offer, ownerId: null, policy: 'IMPORT_THE_REST', kinds: defaultKinds(), tyres: defaultTyres(), media: defaultMedia() })

/** The picked cars as the server takes them: a gone car comes whole (its kinds as boxes), a car here by its kinds' choices. */
const toInput = (choices: Record<string, Choice>, names: Record<string, string> = {}) =>
  Object.entries(choices)
    .filter(([, c]) => c.picked)
    .map(([carId, c]) => ({
      carId,
      targetVehicleId: null,
      policy: c.policy,
      ...kindsInput(c.kinds, c.offer.state !== 'HERE'),
      tyres: tyresInput(c.tyres),
      media: mediaInput(c.media),
      confirmedName: names[carId],
      ownerId: c.offer.state === 'OWNER_GONE' ? c.ownerId : null,
    }))

/**
 * *Backups › Restore cars* (#340): an administrator puts chosen cars of a restore point back as they were, under their own ids, to their
 * owners. Key (skipped for a plain backup), Cars, Review, Done; the run and its report are the import's (#337), worded for a restore. The
 * restore point comes by `?file=`; its run's report, from the notification, by `?run=`. The page's import pieces run in the restore's
 * mode (`RestoreMode`): its queries and its words.
 */
export function RestoreCarsPage({ pollMs = RUN_POLL_MS, openingPollMs = OPENING_POLL_MS }: { pollMs?: number; openingPollMs?: number }) {
  const { t } = useTranslation()
  const { date } = useFormat()
  const [params, setParams] = useSearchParams()
  const file = params.get('file')
  const asked = params.get('run')
  const mine = useQuery(MyRestoreRunDocument, { fetchPolicy: 'network-only', skip: asked !== null })
  const byId = useQuery(ImportRunDocument, { variables: { id: asked ?? '' }, fetchPolicy: 'network-only', skip: asked === null })
  const [status, setStatus] = useState<BackupImportFieldsFragment>()
  const [step, setStep] = useState<Step>('key')
  const [choices, setChoices] = useState<Record<string, Choice>>({})
  const [run, setRun] = useState<ImportRunFieldsFragment>()
  const [left, setLeft] = useState(false)

  // A run of the administrator's that goes or waits when the page opens, or the one its notification names, is shown first.
  const kept = asked !== null ? byId.data?.importRun : mine.data?.myRestoreRun
  const found = run ?? (left ? undefined : kept && (asked !== null || active(kept)) ? kept : undefined)
  const waiting = (asked !== null ? byId.loading : mine.loading) && !run
  const backupOf = status?.file?.createdAt ?? found?.backupOf
  const title = backupOf ? t('restore.title', { date: date(backupOf.slice(0, 10)) }) : t('restore.pageTitle')
  usePageTitle(title)
  const shown: Step = found ? (active(found) ? 'review' : 'done') : step
  const current = STEPS.indexOf(shown)

  return (
    <RestoreMode.Provider value>
      <section aria-labelledby="page-title">
        <Typography id="page-title" component="h1" variant="h3" sx={{ mb: 1.5 }}>
          {title}
        </Typography>
        <Stepper activeStep={current} aria-label={t('restore.pageTitle')} sx={{ mb: 2, flexWrap: 'wrap', rowGap: 1 }}>
          {STEPS.map((s, i) => (
            <Step key={s} completed={i < current || (shown === 'done' && found?.state === 'DONE')} aria-current={i === current ? 'step' : undefined}>
              <StepLabel>{t(`restore.steps.${s}`)}</StepLabel>
            </Step>
          ))}
        </Stepper>
        {waiting ? (
          <Loading />
        ) : found ? (
          <RunView
            run={found}
            pollMs={pollMs}
            onChange={setRun}
            onBackToReview={
              found.fileToken && status
                ? () => {
                    setRun(undefined)
                    setLeft(true)
                    setStep(Object.values(choices).some((c) => c.picked) ? 'review' : 'cars')
                  }
                : undefined
            }
            onAnother={() => {
              setRun(undefined)
              setLeft(true)
              setChoices({})
              setStatus(undefined)
              setStep('key')
              setParams({}, { replace: true })
            }}
          />
        ) : !file && !status ? (
          <Stack sx={{ gap: 1.5, alignItems: 'flex-start' }}>
            <Typography variant="body2">{t('restore.key.notFound')}</Typography>
            <Button component={Link} to="/admin/backups" size="large" variant="soft">
              {t('restore.key.toBackups')}
            </Button>
          </Stack>
        ) : step === 'key' || !status || status.state !== 'OPENED' ? (
          <KeyStep
            file={file ?? ''}
            status={status}
            pollMs={openingPollMs}
            onStatus={(next) => {
              setStatus(next)
              if (next.state === 'OPENED') setStep('cars')
            }}
          />
        ) : step === 'cars' ? (
          <CarsStep token={status.token} choices={choices} onChoices={setChoices} onNext={() => setStep('review')} />
        ) : (
          <ReviewStep
            token={status.token}
            choices={choices}
            onChoices={setChoices}
            onBack={() => setStep('cars')}
            onStarted={(started) => {
              setLeft(false)
              setRun(started)
            }}
          />
        )}
      </section>
      {status && <EndOnLeave token={status.token} />}
    </RestoreMode.Provider>
  )
}

/** The opened restore point is closed when the page is left (a run that holds it keeps it; the file stays in the folder). */
function EndOnLeave({ token }: { token: string }) {
  const client = useApolloClient()
  useEffect(() => () => void client.mutate({ mutation: EndCarRestoreDocument, variables: { token } }).catch(() => undefined), [client, token])
  return null
}

// ---- 1. the key ----------------------------------------------------------------------------------------------

function KeyStep({
  file,
  status,
  pollMs,
  onStatus,
}: {
  file: string
  status: BackupImportFieldsFragment | undefined
  pollMs: number
  onStatus: (status: BackupImportFieldsFragment) => void
}) {
  const { t } = useTranslation()
  const errorText = useErrorText()
  const client = useApolloClient()
  const [open] = useMutation(OpenCarRestoreDocument)
  const [submit] = useMutation(SubmitCarRestoreKeyDocument)
  const [key, setKey] = useState<'passphrase' | 'identity'>('passphrase')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>()
  const started = useRef(false)

  /** Asks until the server is done opening; what it says then is the step's. */
  async function settle(first: BackupImportFieldsFragment) {
    let now = first
    while (now.state === 'OPENING') {
      await new Promise((resolve) => setTimeout(resolve, pollMs))
      const { data } = await client.query({ query: CarRestoreDocument, variables: { token: now.token }, fetchPolicy: 'network-only' })
      if (!data) throw new Error('No answer.')
      now = data.carRestore
    }
    onStatus(now)
  }

  // The restore point opens as the page comes (a plain one goes straight on to its cars).
  useEffect(() => {
    if (started.current || status) return
    started.current = true
    setBusy(true)
    void open({ variables: { file } })
      .then(({ data }) => (data ? settle(data.openCarRestore) : undefined))
      .catch(setError)
      .finally(() => setBusy(false))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [file])

  async function tryKey(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const typed = String(new FormData(e.currentTarget).get('secret') ?? '').trim()
    if (!status || !typed) return
    setBusy(true)
    setError(undefined)
    try {
      const { data } = await submit({ variables: { input: { token: status.token, passphrase: key === 'passphrase' ? typed : null, identity: key === 'identity' ? typed : null } } })
      if (data) await settle(data.submitCarRestoreKey)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  const refusal = status?.state === 'ENDED' && status.refusal ? new ApiError(status.refusal.key, (status.refusal.args ?? {}) as Record<string, unknown>, '', 400) : undefined
  return (
    <Stack sx={{ gap: 2, alignItems: 'flex-start' }}>
      {(busy || status?.state === 'OPENING') && (
        <Stack role="status" sx={{ gap: 0.5, alignSelf: 'stretch', maxWidth: 420 }}>
          <Typography variant="body2">{t('restore.key.opening')}</Typography>
          <LinearProgress aria-hidden />
        </Stack>
      )}
      {status?.state === 'NEEDS_KEY' && (
        <Form onSubmit={(e) => void tryKey(e)} style={{ display: 'contents' }}>
          <Typography variant="body2">{t('restore.key.intro')}</Typography>
          <Segmented
            label={t('admin.backups.keyKind')}
            value={key}
            onChange={setKey}
            options={[
              { value: 'passphrase', label: t('admin.backups.keyPassphrase') },
              { value: 'identity', label: t('admin.backups.keyIdentity') },
            ]}
          />
          <Field
            key={key}
            name="secret"
            required
            label={key === 'passphrase' ? t('admin.backups.passphrase') : t('admin.backups.identity')}
            hint={key === 'passphrase' ? t('admin.backups.passphraseHint') : t('admin.backups.identityHint')}
            refusal={status.wrongKey ? errorText(new ApiError('backup.wrongKey', {}, '', 400)) : undefined}
          >
            <PasswordInput autoComplete="off" disabled={busy} showLabel={key === 'passphrase' ? t('forms.password.showPassphrase') : t('forms.password.showSecret')} />
          </Field>
          <Button size="large" type="submit" disabled={busy}>
            {t('restore.key.open')}
          </Button>
        </Form>
      )}
      {refusal && <ErrorMessage error={refusal} />}
      {error !== undefined && <ErrorMessage error={error} />}
    </Stack>
  )
}

// ---- 2. the cars ---------------------------------------------------------------------------------------------

function CarsStep({ token, choices, onChoices, onNext }: { token: string; choices: Record<string, Choice>; onChoices: (next: Record<string, Choice>) => void; onNext: () => void }) {
  const { t } = useTranslation()
  const [search, setSearch] = useState('')
  const [owner, setOwner] = useState('')
  const [page, setPage] = useState(0)
  const cars = useQuery(RestoreCarsDocument, {
    variables: { token, search: search.trim() || null, ownerId: owner || null, skip: page * RESTORE_PAGE_SIZE, take: RESTORE_PAGE_SIZE },
    fetchPolicy: 'network-only',
  })
  const data = (cars.data ?? cars.previousData)?.restoreCars
  const picked = Object.values(choices).filter((c) => c.picked)
  const problem = (c: Choice) => (c.offer.state !== 'HERE' && nothingChosen(c.kinds, c.tyres) ? t('errors.import.nothingChosen', { car: c.offer.car.name }) : null)
  const ready = picked.length > 0 && picked.every((c) => problem(c) === null)
  const pages = data ? Math.max(1, Math.ceil(data.total / RESTORE_PAGE_SIZE)) : 1

  if (cars.error) return <ErrorMessage error={cars.error} />
  if (!data) return <Loading />
  const set = (offer: Offer, change: Partial<Choice>) => onChoices({ ...choices, [offer.car.id]: { ...(choices[offer.car.id] ?? fresh(offer)), offer, ...change } })
  return (
    <Stack sx={{ gap: 2, alignItems: 'stretch' }}>
      <Stack direction={{ xs: 'column', md: 'row' }} sx={{ gap: 1.5, alignItems: { md: 'flex-end' } }}>
        <Field name="search" label={t('restore.cars.search')}>
          <FieldInput
            type="search"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value)
              setPage(0)
            }}
          />
        </Field>
        <LabeledSelect
          label={t('restore.cars.owner')}
          value={owner}
          onChange={(next) => {
            setOwner(next)
            setPage(0)
          }}
          options={[{ value: '', label: t('restore.cars.everyone') }, ...data.owners.map((o) => ({ value: o.id, label: o.name ?? '' }))]}
        />
      </Stack>
      <Typography variant="body2" role="status">
        {t('restore.cars.count', { count: data.total, picked: picked.length })}
      </Typography>
      {data.cars.length === 0 && <Typography variant="body2">{t('restore.cars.noMatch')}</Typography>}
      {data.cars.map((offer) => (
        <CarCard key={offer.car.id} token={token} offer={offer} choice={choices[offer.car.id] ?? fresh(offer)} problem={problem(choices[offer.car.id] ?? fresh(offer))} onChange={(change) => set(offer, change)} />
      ))}
      {pages > 1 && (
        <Stack direction="row" sx={{ gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
          <Button variant="soft" disabled={page === 0} onClick={() => setPage(page - 1)}>
            {t('restore.cars.previous')}
          </Button>
          <Typography variant="body2">{t('restore.cars.page', { page: page + 1, pages })}</Typography>
          <Button variant="soft" disabled={page + 1 >= pages} onClick={() => setPage(page + 1)}>
            {t('restore.cars.next')}
          </Button>
        </Stack>
      )}
      {picked.length === 0 && <Typography variant="body2">{t('restore.cars.none')}</Typography>}
      <Stack direction="row" sx={{ gap: 1.5, flexWrap: 'wrap' }}>
        <Button component={Link} to="/admin/backups" size="large" variant="soft">
          {t('restore.cars.back')}
        </Button>
        <Button size="large" disabled={!ready} onClick={onNext}>
          {t('restore.cars.review')}
        </Button>
      </Stack>
    </Stack>
  )
}

/** A car of the restore point: its state in words (never colour alone), what it misses, and for a car whose owner has no account the picker. */
function CarCard({ token, offer, choice, problem, onChange }: { token: string; offer: Offer; choice: Choice; problem: string | null; onChange: (change: Partial<Choice>) => void }) {
  const { t, i18n } = useTranslation()
  const heading = useId()
  const { car } = offer
  const list = (items: string[]) => new Intl.ListFormat(validLocale(i18n.language, i18n.resolvedLanguage), { type: 'conjunction' }).format(items)
  const missing = list(
    [
      offer.missingRefuelings > 0 ? t('import.kinds.refueling.listItem', { count: offer.missingRefuelings }) : '',
      offer.missingExpenses > 0 ? t('import.kinds.expense.listItem', { count: offer.missingExpenses }) : '',
      offer.missingSchedules > 0 ? t('import.kinds.recurring.listItem', { count: offer.missingSchedules }) : '',
      offer.missingTyreSets > 0 ? t('restore.cars.tyreSets', { count: offer.missingTyreSets }) : '',
    ].filter(Boolean),
  )
  const grants = offer.grants > 0 ? t('restore.cars.grants', { count: offer.grants }) : t('restore.cars.notShared')
  const owner = offer.ownerNameHere ?? car.ownerName
  return (
    <Card component="section" aria-labelledby={heading} sx={{ p: 2 }}>
      <Stack sx={{ gap: 1 }}>
        <FormControlLabel
          disabled={offer.state === 'IN_TRASH'}
          control={<Checkbox checked={choice.picked} onChange={(e) => onChange({ picked: e.target.checked })} />}
          label={
            <Typography id={heading} component="h2" variant="h6">
              {car.name}
              {car.licensePlate ? ` · ${car.licensePlate}` : ''}
              {owner ? ` · ${t('restore.cars.ownerOf', { name: owner })}` : ''}
            </Typography>
          }
        />
        {offer.state === 'HERE' && (
          <Typography variant="body2">
            <strong>{t('restore.cars.here')}</strong> {missing ? t('restore.cars.missing', { list: missing }) : t('restore.cars.nothingMissing')} {t('restore.cars.sharingStays')}
          </Typography>
        )}
        {(offer.state === 'GONE' || offer.state === 'OWNER_GONE') && (
          <Typography variant="body2">
            <strong>{t('restore.cars.gone')}</strong> {t('restore.cars.comesBack', { grants })}
          </Typography>
        )}
        {offer.state === 'IN_TRASH' && (
          <Typography variant="body2">
            <strong>{t('restore.cars.inTrash')}</strong>
          </Typography>
        )}
        {offer.state === 'OWNER_GONE' && <OwnerPicker offer={offer} ownerId={choice.ownerId} onOwner={(ownerId) => onChange({ ownerId })} />}
        {choice.picked && offer.state !== 'IN_TRASH' && (
          <KindsDisclosure
            token={token}
            car={car}
            target={offer.state === 'HERE' ? { id: car.id, name: car.name } : null}
            kinds={choice.kinds}
            onKinds={(kinds) => onChange({ kinds })}
            tyres={choice.tyres}
            onTyres={(tyres) => onChange({ tyres })}
            media={choice.media}
            onMedia={(media) => onChange({ media })}
          />
        )}
        {problem && (
          <Typography variant="body2" role="status" sx={{ color: 'error.main' }}>
            {problem}
          </Typography>
        )}
      </Stack>
    </Card>
  )
}

/**
 * Who gets a car whose owner has no account (the owner's decision 3): a labelled radio group, the administrator first and chosen, then
 * the account with the same e-mail (suggested, marked), then everyone else.
 */
function OwnerPicker({ offer, ownerId, onOwner }: { offer: Offer; ownerId: string | null; onOwner: (ownerId: string | null) => void }) {
  const { t } = useTranslation()
  const label = useId()
  const session = useQuery(SessionDocument)
  const people = useQuery(RestorePeopleDocument, { fetchPolicy: 'cache-first' })
  const me = session.data?.session.user
  const others = useMemo(() => {
    const all = (people.data?.restorePeople ?? []).filter((p) => p.id !== me?.id)
    const same = all.filter((p) => p.id === offer.sameEmailUserId)
    return [...same, ...all.filter((p) => p.id !== offer.sameEmailUserId)]
  }, [people.data, me?.id, offer.sameEmailUserId])
  return (
    <Stack sx={{ gap: 0.5 }}>
      <Typography variant="body2">{t('restore.cars.ownerGone', { name: offer.car.ownerName ?? '' })}</Typography>
      <Typography id={label} variant="body2" sx={{ fontWeight: 'fontWeightBold' }}>
        {t('restore.cars.whoGets')}
      </Typography>
      <RadioGroup aria-labelledby={label} value={ownerId ?? ''} onChange={(e) => onOwner(e.target.value || null)}>
        <FormControlLabel value="" control={<Radio />} label={t('restore.cars.you', { name: me?.displayName ?? '' })} />
        {others.map((person) => (
          <FormControlLabel
            key={person.id}
            value={person.id}
            control={<Radio />}
            label={`${person.name} · ${person.email}${person.id === offer.sameEmailUserId ? ` · ${t('restore.cars.sameEmail')}` : ''}`}
          />
        ))}
      </RadioGroup>
    </Stack>
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
  const { t, i18n } = useTranslation()
  const asked = JSON.stringify(toInput(choices).map(({ policy: _policy, confirmedName: _name, ...car }) => car))
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const compared = useMemo(() => JSON.parse(asked) as ReturnType<typeof toInput>, [asked])
  const [confirming, setConfirming] = useState(false)
  const review = useQuery(RestoreReviewDocument, { variables: { token, cars: compared }, fetchPolicy: 'network-only' })
  const [start, started] = useMutation(StartCarRestoreDocument)

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
  const cars = review.data.restoreReview
  const list = (names: string[]) => new Intl.ListFormat(validLocale(i18n.language, i18n.resolvedLanguage), { type: 'conjunction' }).format(names)
  const owners = [...new Set(cars.map((c) => c.restore?.ownerName).filter((n): n is string => !!n))]

  // A car whose kinds delete asks for its name, typed, with its owner named (the owner's decision on #338 and #340).
  const replacements: Replacement[] = cars
    .filter((car) => car.restore?.state === 'HERE' && replaces(choices[car.carId]?.kinds ?? defaultKinds()))
    .map((car) => {
      const replaced = car.kinds.filter((k) => k.mode === 'REPLACE_ALL')
      return {
        carId: car.carId,
        target: car.targetName ?? car.name,
        owner: car.restore?.ownerName ?? '',
        deleted: replaced.map((k) => ({ kind: k.kind as Kind, count: k.targetLive + k.targetTrashed })),
        trashed: replaced.reduce((sum, k) => sum + k.targetTrashed, 0),
        linksLost: car.linksLost,
        photosLost: replaced.reduce((sum, k) => sum + k.photosLost, 0),
      }
    })
  const begin = (names: Record<string, string>) => {
    void start({ variables: { input: { token, cars: toInput(choices, names) } } })
      .then(({ data }) => data && onStarted(data.startCarRestore))
      .catch(() => undefined) // said under the cards
  }

  return (
    <Stack sx={{ gap: 2, alignItems: 'stretch' }}>
      {cars.map((car) => {
        const choice = choices[car.carId]
        return (
          <ReviewCard
            key={car.carId}
            car={car}
            kinds={choice?.kinds ?? defaultKinds()}
            tyres={choice?.tyres ?? defaultTyres()}
            media={choice?.media ?? defaultMedia()}
            policy={choice?.policy ?? 'IMPORT_THE_REST'}
            onPolicy={(policy) => choice && onChoices({ ...choices, [car.carId]: { ...choice, policy } })}
          />
        )
      })}
      <Typography variant="body2">{t('restore.review.refused')}</Typography>
      {owners.length > 0 && <Typography variant="body2">{t('restore.review.told', { names: list(owners) })}</Typography>}
      <Typography variant="body2" sx={{ color: 'text.secondary' }}>
        {t('import.plan.mayWait')}
      </Typography>
      {started.error && <ErrorMessage error={started.error} />}
      <Stack direction="row" sx={{ gap: 1.5, flexWrap: 'wrap' }}>
        <Button size="large" variant="soft" disabled={started.loading} onClick={onBack}>
          {t('import.plan.back')}
        </Button>
        <Button size="large" loading={started.loading} onClick={() => (replacements.length > 0 ? setConfirming(true) : begin({}))}>
          {t('restore.review.start', { count: cars.length })}
        </Button>
      </Stack>
      {confirming && (
        <ReplaceConfirmations
          cars={replacements}
          onCancel={() => setConfirming(false)}
          onConfirmed={(names) => {
            setConfirming(false)
            begin(names)
          }}
        />
      )}
    </Stack>
  )
}
