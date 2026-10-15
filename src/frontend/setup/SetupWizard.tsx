import { useApolloClient, useMutation, useQuery } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import FormControlLabel from '@mui/material/FormControlLabel'
import Divider from '@mui/material/Divider'
import Link from '@mui/material/Link'
import Stack from '@mui/material/Stack'
import Step from '@mui/material/Step'
import StepContent from '@mui/material/StepContent'
import StepLabel from '@mui/material/StepLabel'
import Stepper from '@mui/material/Stepper'
import Typography from '@mui/material/Typography'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { healthProbe, waitForRestart } from '../admin/restartFlow.ts'
import { BackupDetailsDialog } from '../admin/backupDetails.tsx'
import { secretsInPlain, useHeldText } from '../admin/backupMarks.ts'
import { RestartWait, RestoreDialog, RestoreRiskLines } from '../admin/restore.tsx'
import { UploadBackup } from '../admin/upload.tsx'
import { SettingsGroupForm } from '../admin/SettingsGroupForm.tsx'
import { unusedReason } from '../admin/settingsRelevance.ts'
import { changesOf } from '../admin/settingsDraft.ts'
import { useGroupDrafts } from '../admin/useGroupDrafts.ts'
import { Loading } from '../components/Loading.tsx'
import { FieldForm } from '../forms/FieldForm.tsx'
import {
  SessionDocument,
  SetupClaimAdministratorDocument,
  SetupFinishDocument,
  SetupRestorePointsDocument,
  SystemSettingsDocument,
  type AuthMode,
  type RestoreTargetFieldsFragment,
  type SetupFinishInput,
  type SetupRestorePointsQuery,
} from '../gql/generated.ts'
import { useFormat } from '../i18n/format.ts'
import { ErrorMessage, WarningMessage } from '../messages.tsx'
import { useMediaQuery } from '../hooks/useMediaQuery.ts'
import { classifyFailure } from '../offline/errors.ts'
import { standaloneLink } from '../theme/components.ts'
import { MEDIA } from '../theme/media.ts'
import { claimNext, remember } from './claimNext.ts'
import { useHoldTakeover } from './takeoverHold.ts'

type RestorePoint = SetupRestorePointsQuery['setupRestorePoints'][number]

const MODE_KEY = 'Auth:Mode'

/**
 * How the stepper is laid out on a desktop: `horizontal`, every label in one row (each under its step's icon) and the step at hand below
 * the whole row; `vertical`, the labels stacked and the step at hand open under its own label. Below the desktop width it is always
 * vertical: a row of seven to ten labels does not fit a phone.
 */
const DESKTOP_LAYOUT: 'horizontal' | 'vertical' = 'horizontal'

/** How long a stacked step takes to open (and the one before it to fold), a change of state's short motion. */
const STEP_MOTION_MS = 200

/**
 * The setup of a new instance as one wizard (#90). First the one-time code from the server's log, which opens what no administrator could,
 * since none exists yet. Then every step at once in a stepper, the one at hand open: the start, which asks whether to set the instance up
 * or restore a backup (no way is preselected, and the start can be gone back to at any time); setting it up, a step per group of the
 * Server settings page with the page's own forms (`SettingsGroupForm`, saved with the code as each step goes on: the sign-in first, its
 * mode to be chosen, none preselected and sign-in off one of them; every later group may be skipped, and one with nothing to choose has no
 * step), then the gatekeeper of the sign-in mode chosen, after which the server restarts once when a saved setting waits for it; restoring,
 * the backups of the folder, then the wait for the restart. The code stays in this page only (never stored); a restart in the server's
 * process keeps it.
 */
export function SetupWizard() {
  const client = useApolloClient()
  const { data: session } = useQuery(SessionDocument)
  const [entered, setEntered] = useState<{ code: string; points: RestorePoint[]; uploadLimitMb: number }>()

  const check = async (code: string) => {
    const { data } = await client.query({ query: SetupRestorePointsDocument, variables: { code }, fetchPolicy: 'network-only' })
    setEntered({ code, points: data?.setupRestorePoints ?? [], uploadLimitMb: data?.setupUploadLimitMb ?? 0 })
  }

  if (!entered) return <CodeStep onCode={check} />
  return (
    <WizardSteps
      code={entered.code}
      points={entered.points}
      uploadLimitMb={entered.uploadLimitMb}
      name={session?.session.user?.displayName ?? ''}
      generation={session?.session.instance?.generation ?? undefined}
      onLookAgain={() => check(entered.code)}
      onOtherCode={() => setEntered(undefined)}
    />
  )
}

function CodeStep({ onCode }: { onCode: (code: string) => Promise<void> }) {
  const { t } = useTranslation()
  return (
    <Stack sx={{ gap: 1.5, maxWidth: 560 }}>
      <Typography>{t('setup.intro')}</Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary' }}>
        {t('setup.codeWhere')}
      </Typography>
      <FieldForm fields={[{ name: 'code', label: 'setup.code', autoComplete: 'off' }]} submitLabel="setup.continue" onSubmit={(v) => onCode(v.code ?? '')} />
    </Stack>
  )
}

interface WizardStep {
  id: string
  label: string
  /** What the step still needs before the setup can finish (a choice, a save). */
  needs?: string
  content: () => ReactNode
}

/**
 * The stepper: every step's label at once, the one at hand open below its label (vertical, so it fits a phone as well). Once a way is chosen
 * on the start, every step opens by its label, in any order (Next, Back and Skip walk them in order); what a step still needs (a sign-in
 * mode, changes not saved yet) is said on its label once it was left, and the last step lists it instead of finishing. The settings are
 * the server's, asked with the code (never kept on the device); what is typed into a step is held above the forms (`useGroupDrafts`), so it
 * is there again when the step is, and a step's Next saves it before it goes on.
 */
function WizardSteps({
  code,
  points,
  uploadLimitMb,
  name,
  generation,
  onLookAgain,
  onOtherCode,
}: {
  code: string
  points: RestorePoint[]
  uploadLimitMb: number
  name: string
  generation: string | undefined
  onLookAgain: () => Promise<void>
  onOtherCode: () => void
}) {
  const { t } = useTranslation()
  const { data, error } = useQuery(SystemSettingsDocument, { variables: { code }, fetchPolicy: 'network-only' })
  const settings = data?.systemSettings
  const groups = settings?.groups ?? []
  const { draftFor, hold, valueOf, basisOf } = useGroupDrafts(groups, [MODE_KEY])
  // Back from a provider's sign-in after the last step restarted into it: on at that step, the claim.
  const [resumed] = useState(claimNext)
  const [way, setWay] = useState<'fresh' | 'restore' | undefined>(resumed ? 'fresh' : undefined)
  const [active, setActive] = useState(resumed ? Number.MAX_SAFE_INTEGER : 0)
  // The steps left at least once: what one of them still needs is said on its label from then on.
  const [left, setLeft] = useState<ReadonlySet<string>>(new Set())
  // The generation of the data when the restore was asked for, kept with it: the session's own may change while the wait runs.
  const [restoring, setRestoring] = useState<{ point: RestoreTargetFieldsFragment; generation: string | undefined }>()
  // The restore's restart takes the server away: the page stays, with the wait that reloads it once the restored data is served.
  useHoldTakeover(restoring !== undefined)

  const go = (step: number) => {
    setLeft((l) => new Set(l).add(steps[at]!.id))
    setActive(step)
  }
  const choose = (chosen: 'fresh' | 'restore') => {
    if (chosen !== way) {
      setWay(chosen)
      setLeft(new Set()) // the other way's steps are other steps: none of them gone through yet
    }
    go(1)
  }

  // A step per group with something to choose: none for one the environment holds whole, or whose every setting another one leaves
  // unused (the e-mail with a provider's sign-in).
  const groupSteps = groups.filter((g) => g.settings.some((s) => s.editable && unusedReason(s.key, valueOf) === null))
  // What a step still needs before the setup can finish: the sign-in mode chosen (no source but the shipped default holds one, and the step
  // holds none), and nothing typed that is not saved.
  const needs = (group: (typeof groups)[number]): string | undefined => {
    const mode = group.settings.find((s) => s.key === MODE_KEY)
    if (mode?.editable && mode.source !== 'FILE' && !(draftFor(group).values[MODE_KEY] ?? '').trim()) return t('setup.wizard.chooseMode')
    return changesOf(group.settings, draftFor(group)).length > 0 ? t('setup.wizard.unsaved') : undefined
  }
  const fresh: WizardStep[] = [
    ...groupSteps.map((group, i) => ({
      id: group.name,
      label: t(`systemSettings.groups.${group.name}`),
      needs: needs(group),
      content: () => (
        <SettingsGroupForm
          key={`${group.name}:${basisOf(group)}`}
          group={group}
          draft={draftFor(group)}
          valueOf={valueOf}
          onDraft={(draft) => hold(group, draft)}
          // On, keeping what the step holds: a save's answer starts it afresh from the server anyway (its settings changed), and a step
          // with nothing to save (a choice of what runs already) shows the choice again when it is gone back to.
          onSaved={() => go(i + 2)}
          code={code}
          required={group.name === 'AUTH' ? [MODE_KEY] : []}
          confirmModeChange={false}
          submitLabel={t('setup.wizard.next')}
          saveUnchanged
          announceSaved={false}
          actions={
            <>
              <BackButton onBack={() => go(i)} />
              {group.name !== 'AUTH' && (
                <Button
                  variant="ghost"
                  color="primary"
                  onClick={() => {
                    hold(group, undefined) // skipped: what was typed here is not saved
                    go(i + 2)
                  }}
                >
                  {t('setup.wizard.skip')}
                </Button>
              )}
            </>
          }
        />
      ),
    })),
    {
      id: 'last',
      label: t('setup.wizard.lastTitle'),
      content: () =>
        settings && (
          <LastStep
            code={code}
            name={name}
            next={modeOf(valueOf(MODE_KEY))}
            administrators={valueOf('Auth:AdminEmails')}
            restartPending={settings.restartPending}
            onBack={() => go(groupSteps.length)}
            blockers={groupSteps.flatMap((group, i) => {
              const need = needs(group)
              return need ? [{ step: t(`systemSettings.groups.${group.name}`), need, open: () => go(i + 1) }] : []
            })}
          />
        ),
    },
  ]
  const restore: WizardStep[] = [
    {
      id: 'backup',
      label: t('setup.restoreTitle'),
      content: () => (
        <RestoreStep
          points={points}
          code={code}
          uploadLimitMb={uploadLimitMb}
          onLookAgain={onLookAgain}
          onRestoring={(point) => {
            setRestoring({ point, generation })
            go(2)
          }}
        />
      ),
    },
    {
      id: 'restart',
      label: t('setup.wizard.restartTitle'),
      content: () =>
        restoring && (
          <RestartWait
            point={restoring.point}
            generation={restoring.generation}
            notRestored={
              // The restart kept the code: back to the backups, asked for again (a new process made a new code: back to it).
              <Button
                onClick={() => {
                  setRestoring(undefined)
                  go(1)
                  void onLookAgain().catch(onOtherCode)
                }}
              >
                {t('setup.backToSetup')}
              </Button>
            }
          />
        ),
    },
  ]
  const start: WizardStep = { id: 'start', label: t('setup.wizard.startTitle'), content: () => <StartStep onFresh={() => choose('fresh')} onRestore={() => choose('restore')} /> }
  // The way's steps follow the start once it is chosen: before that, the start is all there is.
  const steps = [start, ...(way === 'restore' ? restore : way === 'fresh' ? fresh : [])]
  const at = Math.min(active, steps.length - 1)
  const last = at === steps.length - 1
  // Every step opens by its label, except while a restore restarts the server, and the restart's wait before there is one.
  const closed = (i: number, step: WizardStep) => (restoring !== undefined ? i !== at : step.id === 'restart')

  // The step at hand from its top as it opens (one opened from further down, a label or the last step's list, would else open out of
  // sight), and its label takes the focus: the button that led to it is gone (or was a label itself), and it is read out.
  const label = useRef<HTMLSpanElement>(null)
  const horizontal = useMediaQuery(MEDIA.desktop, true) && DESKTOP_LAYOUT === 'horizontal'
  useEffect(() => {
    const opened = label.current
    if (!opened) return
    opened.focus({ preventScroll: true })
    const still = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
    const scroll = () => opened.scrollIntoView?.({ block: 'start', behavior: still ? 'auto' : 'smooth' })
    // Measured once the steps stopped moving: stacked, the step that closes folds while this one opens, and a scroll before that ends is
    // moved by it; in a row, once the step below it is laid out.
    if (horizontal) {
      const frame = requestAnimationFrame(scroll)
      return () => cancelAnimationFrame(frame)
    }
    const timer = setTimeout(scroll, STEP_MOTION_MS + 20)
    return () => clearTimeout(timer)
  }, [at, way, horizontal])
  const content = way === 'fresh' && at > 0 && !settings ? error ? <ErrorMessage error={error} /> : <Loading /> : steps[at]!.content()

  return (
    <Stack sx={{ gap: 2, maxWidth: horizontal ? undefined : 720 }}>
      {/* A list of the steps (no StepButton: MUI makes a stepper with one a tab list, which may hold nothing but its tabs, and the step at
          hand's content sits in it), each label a button once the step was reached, the one at hand the current step. */}
      <Stepper orientation={horizontal ? 'horizontal' : 'vertical'} alternativeLabel={horizontal} nonLinear activeStep={at} aria-label={t('setup.wizard.steps')}>
        {steps.map((step, i) => (
          <Step key={step.id} completed={left.has(step.id) && i !== at && !step.needs} disabled={closed(i, step)}>
            <StepLabel
              // The label's link covers the whole label, its number (or tick) too: a small target on its own on a touch screen. The link
              // (a button, which MUI positions) is static, so its stretch is measured from here.
              sx={{ position: 'relative' }}
              // What it still needs, once it was left (or the last step asks): an error step, saying what.
              error={i !== at && (left.has(step.id) || last) && step.needs !== undefined}
              optional={
                i !== at && (left.has(step.id) || last) && step.needs ? (
                  <Typography variant="caption" sx={{ color: 'error.main' }}>
                    {step.needs}
                  </Typography>
                ) : undefined
              }
            >
              {i === at ? (
                <Box component="span" ref={label} tabIndex={-1} aria-current="step" sx={{ scrollMarginTop: (theme) => theme.spacing(12) }}>
                  {step.label}
                </Box>
              ) : !closed(i, step) ? (
                <Link component="button" type="button" onClick={() => go(i)} sx={{ position: 'static', '&::after': { content: '""', position: 'absolute', inset: 0 }, ...standaloneLink }}>
                  {step.label}
                </Link>
              ) : (
                step.label
              )}
            </StepLabel>
            {/* Vertical: the step at hand open under its own label. */}
            {!horizontal && <StepContent transitionDuration={STEP_MOTION_MS}>{i === at && content}</StepContent>}
          </Step>
        ))}
      </Stepper>
      {/* Horizontal: the step at hand below the whole row of labels, as wide as a form reads well. */}
      {horizontal && <Box sx={{ maxWidth: 720, width: '100%', alignSelf: 'center', mt: 1 }}>{content}</Box>}
    </Stack>
  )
}

/** The start: set the instance up, or restore a backup instead; one of the two is chosen, neither is preselected. */
function StartStep({ onFresh, onRestore }: { onFresh: () => void; onRestore: () => void }) {
  const { t } = useTranslation()
  return (
    <Stack sx={{ gap: 1.5, alignItems: 'center', textAlign: 'center' }}>
      <Typography>{t('setup.wizard.startIntro')}</Typography>
      {/* Two ways of the same weight, as wide as each other: neither is the one the page pushes. */}
      <Stack sx={{ gap: 1.5, alignItems: 'stretch' }}>
        <Button variant="contained" onClick={onFresh}>
          {t('setup.wizard.startFresh')}
        </Button>
        {/* The word stays text, read between the two (a divider holding it would be a separator, whose content is not read). */}
        <Stack direction="row" sx={{ gap: 1.5, alignItems: 'center' }}>
          <Divider sx={{ flex: 1 }} />
          <Typography component="span" sx={{ color: 'text.secondary' }}>
            {t('setup.wizard.or')}
          </Typography>
          <Divider sx={{ flex: 1 }} />
        </Stack>
        <Button variant="contained" onClick={onRestore}>
          {t('setup.restoreChoose')}
        </Button>
      </Stack>
    </Stack>
  )
}

/** The sign-in mode a setting's value names, as the schema spells it; nothing (the shipped default) is sign-in off. */
function modeOf(value: string | undefined): AuthMode {
  switch (value?.toLowerCase()) {
    case 'standalone':
      return 'STANDALONE'
    case 'oidc':
      return 'OIDC'
    case 'proxyheader':
      return 'PROXY_HEADER'
    default:
      return 'NONE'
  }
}

function BackButton({ onBack }: { onBack: () => void }) {
  const { t } = useTranslation()
  return (
    <Button variant="ghost" color="primary" onClick={onBack}>
      {t('setup.wizard.back')}
    </Button>
  )
}

/**
 * The wizard's last step, by the sign-in mode chosen (`next`): Standalone the first administrator, sign-in off the acknowledgement, a
 * provider the restart that takes it up (the administrators named, or the claim after it) or, once it runs, the claim.
 */
function LastStep({
  code,
  name,
  next,
  administrators,
  restartPending,
  onBack,
  blockers,
}: {
  code: string
  name: string
  next: AuthMode
  administrators: string | undefined
  restartPending: boolean
  onBack?: () => void
  /** Steps that still need something (`needs`): listed, each with the way to it, in place of the gatekeeper. */
  blockers: { step: string; need: string; open: () => void }[]
}) {
  const { t } = useTranslation()
  if (blockers.length > 0)
    return (
      <section aria-label={t('setup.wizard.lastTitle')}>
        <WarningMessage>{t('setup.wizard.blocked')}</WarningMessage>
        <Stack component="ul" sx={{ m: 0, mt: 1.5, pl: 2.5, gap: 1 }}>
          {blockers.map((b) => (
            <li key={b.step}>
              <Link component="button" type="button" onClick={b.open} sx={standaloneLink}>
                {t('setup.wizard.blocker', { step: b.step, need: b.need })}
              </Link>
            </li>
          ))}
        </Stack>
      </section>
    )
  return (
    <section aria-label={t('setup.wizard.lastTitle')}>
      {next === 'STANDALONE' && <AdministratorStep code={code} onBack={onBack} />}
      {next === 'NONE' && <OpenAccessStep code={code} onBack={onBack} />}
      {(next === 'OIDC' || next === 'PROXY_HEADER') &&
        (restartPending ? <ProviderRestartStep code={code} administrators={administrators} onBack={onBack} /> : <ClaimStep code={code} name={name} onBack={onBack} />)}
    </section>
  )
}

/**
 * The end of the setup: `setupFinish` (the gatekeeper, then the restart the saved settings wait for), the wait for the server to come back
 * when it restarts, then the session asked again on the page it goes on with: the done screen for an administrator (`/setup`, after the
 * sign-in a mode change asks for), the start page with sign-in off.
 */
function useFinishSetup(to: '/setup' | '/') {
  const client = useApolloClient()
  const navigate = useNavigate()
  const [finishSetup] = useMutation(SetupFinishDocument)
  const [restart, setRestart] = useState<'waiting' | 'timeout'>()
  // The page stays while the server it restarts is away, so the wait goes on to the done screen.
  useHoldTakeover(restart === 'waiting')

  const done = async () => {
    remember(false)
    // The address first: the session's new answer ends the takeover and draws the page of the address, which unmounts this step (a
    // navigate after that would be lost).
    void navigate(to, { replace: true })
    await client.resetStore()
  }
  const finish = async (input: SetupFinishInput) => {
    let restarting: boolean
    try {
      restarting = (await finishSetup({ variables: { input } })).data?.setupFinish.restarting ?? false
    } catch (e) {
      // A request that went and got no answer is the restart cutting it short; anything answered is a refusal, said under the button.
      if (classifyFailure(e) === 'answered') throw e
      restarting = true
    }
    if (restarting) {
      setRestart('waiting')
      if ((await waitForRestart(healthProbe(client))) === 'timeout') {
        setRestart('timeout')
        return
      }
    }
    await done()
  }
  return { finish, done, restart }
}

/** While the server restarts for the saved settings, and when it does not come back in time. */
function RestartStatus({ restart }: { restart: 'waiting' | 'timeout' | undefined }) {
  const { t } = useTranslation()
  return (
    <Typography role="status" variant="body2" className={restart === 'waiting' ? 'tk-pulse' : undefined}>
      {restart === 'waiting' ? t('setup.wizard.restarting') : restart === 'timeout' ? t('systemSettings.restartTimeout') : ''}
    </Typography>
  )
}

/** Standalone: the first administrator, made as the setup ends (signed in on this browser when Standalone runs already). */
function AdministratorStep({ code, onBack }: { code: string; onBack?: () => void }) {
  const { t } = useTranslation()
  const { finish, restart } = useFinishSetup('/setup')
  return (
    <Stack sx={{ gap: 1.5, maxWidth: 480 }}>
      <Typography>{t('setup.adminIntro')}</Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary' }}>
        {t('setup.passwordHint')}
      </Typography>
      <FieldForm
        fields={[
          { name: 'email', label: 'fields.email', type: 'email', autoComplete: 'username' },
          { name: 'password', label: 'fields.password', type: 'newPassword', repeatLabel: 'fields.passwordRepeat' },
        ]}
        submitLabel="setup.createAdmin"
        onSubmit={(v) => finish({ code, email: v.email ?? '', password: v.password ?? '', passwordConfirmation: v.passwordRepeat ?? '' })}
      >
        {onBack && restart === undefined && <BackButton onBack={onBack} />}
      </FieldForm>
      <RestartStatus restart={restart} />
    </Stack>
  )
}

/**
 * A provider's sign-in chosen while another runs: the restart takes it up. The administrators the settings name become administrators as
 * they sign in; with none named, the visitor signs in through the provider after the restart and comes back to the claim.
 */
function ProviderRestartStep({ code, administrators, onBack }: { code: string; administrators: string | undefined; onBack?: () => void }) {
  const { t } = useTranslation()
  const { list } = useFormat()
  const { finish, restart } = useFinishSetup('/setup')
  const named = (administrators ?? '').split('\n').map((a) => a.trim()).filter(Boolean)
  const { busy, error, run } = useStepAction(async () => {
    remember(named.length === 0)
    await finish({ code })
  })
  return (
    <Stack sx={{ gap: 1.5, alignItems: 'flex-start', maxWidth: 560 }}>
      <Typography>{named.length > 0 ? t('setup.wizard.providerAdmins', { emails: list(named) }) : t('setup.wizard.providerClaim')}</Typography>
      {error !== undefined && <ErrorMessage error={error} />}
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
        <Button loading={busy} onClick={run}>
          {t('setup.wizard.restartFinish')}
        </Button>
        {onBack && restart === undefined && <BackButton onBack={onBack} />}
      </Stack>
      <RestartStatus restart={restart} />
    </Stack>
  )
}

/** OIDC and proxy mode, once the provider runs: the signed-in user becomes the first administrator. */
function ClaimStep({ code, name, onBack }: { code: string; name: string; onBack?: () => void }) {
  const { t } = useTranslation()
  const [claim] = useMutation(SetupClaimAdministratorDocument)
  const { done } = useFinishSetup('/setup')
  const { busy, error, run } = useStepAction(async () => {
    await claim({ variables: { code } })
    await done()
  })
  return (
    <Stack sx={{ gap: 1.5, alignItems: 'flex-start', maxWidth: 560 }}>
      <Typography>{t('setup.claimIntro', { name })}</Typography>
      {error !== undefined && <ErrorMessage error={error} />}
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
        <Button loading={busy} onClick={run}>
          {t('setup.claim')}
        </Button>
        {onBack && <BackButton onBack={onBack} />}
      </Stack>
    </Stack>
  )
}

/** Sign-in off: nobody is an administrator, so the setup ends with the acknowledgement of what that means. */
function OpenAccessStep({ code, onBack }: { code: string; onBack?: () => void }) {
  const { t } = useTranslation()
  const { finish, restart } = useFinishSetup('/')
  const [understood, setUnderstood] = useState(false)
  const { busy, error, run } = useStepAction(() => finish({ code, acknowledgeOpenAccess: true }))
  return (
    <Stack sx={{ gap: 1.5, alignItems: 'flex-start', maxWidth: 560 }}>
      <WarningMessage>{t('setup.openIntro')}</WarningMessage>
      <FormControlLabel control={<Checkbox checked={understood} onChange={(e) => setUnderstood(e.target.checked)} />} label={t('setup.openAcknowledge')} />
      {error !== undefined && <ErrorMessage error={error} />}
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
        <Button loading={busy} disabled={!understood} onClick={run}>
          {t('setup.finish')}
        </Button>
        {onBack && restart === undefined && <BackButton onBack={onBack} />}
      </Stack>
      <RestartStatus restart={restart} />
    </Stack>
  )
}

/** The backups of the folder: one restored is the instance's data from then on, its users and its setup included. */
function RestoreStep({
  points,
  code,
  uploadLimitMb,
  onLookAgain,
  onRestoring,
}: {
  points: RestorePoint[]
  code: string
  uploadLimitMb: number
  onLookAgain: () => Promise<void>
  onRestoring: (point: RestoreTargetFieldsFragment) => void
}) {
  const { t } = useTranslation()
  const { dateTime } = useFormat()
  const heldText = useHeldText()
  const [error, setError] = useState<unknown>()
  const lookAgain = () => {
    setError(undefined)
    onLookAgain().catch(setError)
  }
  return (
    <section aria-label={t('setup.restoreTitle')}>
      <Typography sx={{ mb: 1.5 }}>{t('setup.restoreIntro')}</Typography>
      {points.length === 0 ? (
        <Typography variant="body2" sx={{ color: 'text.secondary', mb: 1 }}>
          {t('setup.restoreNone')}
        </Typography>
      ) : (
        <Stack component="ul" sx={{ listStyle: 'none', p: 0, m: 0, mb: 1, gap: 1.5 }}>
          {points.map((point) => {
            const restorable = point.sameMigration || point.olderMigration
            return (
              <Stack component="li" key={point.file} direction="row" sx={{ gap: 1.5, alignItems: 'center', flexWrap: 'wrap', justifyContent: 'space-between' }}>
                <Stack sx={{ minWidth: 0, overflowWrap: 'anywhere' }}>
                  <Typography>{dateTime(point.createdAt)}</Typography>
                  <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                    {[t('setup.pointMeta', { version: point.appVersion, file: point.file }), point.encrypted && t('setup.encrypted')].filter(Boolean).join(' · ')}
                  </Typography>
                  {point.held && (
                    <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                      {heldText(point)}
                    </Typography>
                  )}
                  {/* In words, not only in red: anyone with the file reads its passwords. */}
                  {secretsInPlain(point) && (
                    <Typography variant="body2" sx={{ color: 'error.softText' }}>
                      {t('admin.backups.secretsPlainMark')}
                    </Typography>
                  )}
                  {point.olderMigration && (
                    <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                      {t('setup.older', { version: point.appVersion })}
                    </Typography>
                  )}
                  {!restorable && (
                    <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                      {t('setup.newer')}
                    </Typography>
                  )}
                  {/* What its restore can break here, before anyone opens it. */}
                  {restorable && <RestoreRiskLines point={point} />}
                </Stack>
                <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
                  <BackupDetailsDialog
                    file={point.file}
                    createdAt={point.createdAt}
                    code={code}
                    trigger={
                      <Button variant="soft" aria-label={t('admin.backups.openAria', { made: dateTime(point.createdAt), file: point.file })}>
                        {t('admin.backups.open')}
                      </Button>
                    }
                  />
                  {restorable && (
                    <RestoreDialog
                      point={point}
                      code={code}
                      onRestoring={onRestoring}
                      trigger={
                        <Button color="error" variant="soft" aria-label={t('admin.backups.restoreAria', { made: dateTime(point.createdAt), file: point.file })}>
                          {t('setup.restore')}
                        </Button>
                      }
                    />
                  )}
                </Stack>
              </Stack>
            )
          })}
        </Stack>
      )}
      {error !== undefined && <ErrorMessage error={error} />}
      <Button variant="ghost" color="primary" onClick={lookAgain}>
        {t('setup.lookAgain')}
      </Button>
      {/* A backup this server's folder does not hold (moving to a new server): sent from this device, then listed above. */}
      <Box sx={{ mt: 3 }}>
        <UploadBackup maxUploadMb={uploadLimitMb} code={code} onUploaded={lookAgain} />
      </Box>
    </section>
  )
}

/** The button of a step that completes the setup: busy while it runs; a refusal is said under it, and the button is free again. */
function useStepAction(action: () => Promise<void>) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>()
  const run = async () => {
    setBusy(true)
    setError(undefined)
    try {
      await action()
    } catch (err) {
      setError(err)
      setBusy(false)
    }
  }
  return { busy, error, run: () => void run() }
}
