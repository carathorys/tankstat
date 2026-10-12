import { useApolloClient, useMutation, useQuery } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useEffect, useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router'
import { ConfirmDialog } from '../components/ConfirmDialog.tsx'
import { DefinitionList } from '../components/DefinitionList.tsx'
import { Loading } from '../components/Loading.tsx'
import { TabbedPanels } from '../components/TabbedPanels.tsx'
import { HealthDocument, RestartServerDocument, SystemSettingsDocument, type SettingsGroup, type SystemSettingsQuery } from '../gql/generated.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { useErrorText } from '../i18n/errors.ts'
import { ErrorMessage, WarningMessage } from '../messages.tsx'
import { navigation } from '../navigation.ts'
import { classifyFailure } from '../offline/errors.ts'
import { PROBE_TIMEOUT_MS, withDeadline } from '../offline/runtime.ts'
import { waitForRestart } from './restartFlow.ts'
import { valueOfDraft } from './settingsRelevance.ts'
import { problemsOf } from './settingsText.ts'
import { changesOf, draftOf, effectiveOf, type GroupDraft } from './settingsDraft.ts'
import { ProblemList, SettingsGroupForm } from './SettingsGroupForm.tsx'

/**
 * The settings the server runs with, for administrators: the app's own settings file (what became of it at the last start), the restart
 * that makes saved changes count, and a tab with a form per group. It needs the server (nothing of it is kept on the device).
 */
export function SystemSettingsPage() {
  const { t } = useTranslation()
  usePageTitle(t('systemSettings.title'))
  const { data, error } = useQuery(SystemSettingsDocument, { fetchPolicy: 'cache-and-network' })
  // The server's answer or nothing: when asking again fails, Apollo keeps the earlier answer beside the error, and that may be out of date.
  const settings = error ? undefined : data?.systemSettings

  return (
    <section aria-labelledby="page-title">
      <Typography id="page-title" component="h1" variant="h3" sx={{ mb: 1.5 }}>
        {t('systemSettings.title')}
      </Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary', mb: 2 }}>
        {t('systemSettings.description')}
      </Typography>
      {error && <ErrorMessage error={error} />}
      {!data && !error && <Loading />}
      {settings && (
        <>
          <FileStatus file={settings.file} />
          <RestartCard pending={settings.groups.flatMap((g) => g.settings).filter((s) => s.restartPending).length} />
          <SettingsTabs groups={settings.groups} />
        </>
      )}
    </section>
  )
}

type Group = SystemSettingsQuery['systemSettings']['groups'][number]

/** A group's values as the form holds them, with the server's settings they started from: a new answer of the server starts them afresh. */
interface HeldDraft {
  basis: string
  draft: GroupDraft
}

/**
 * A tab per group, the chosen one in the address (`?tab=smtp`), the first one without. What is typed into a group stays here, not in its
 * form (which only the chosen tab shows), so it outlasts a switch of tabs; a tab with changes not saved yet says so. Every group's values
 * as they are held decide which settings of the others are used (a sign-in mode chosen in one tab greys out the e-mail settings in the
 * other at once).
 */
function SettingsTabs({ groups }: { groups: readonly Group[] }) {
  const { t } = useTranslation()
  const [params, setParams] = useSearchParams()
  const requested = params.get('tab')?.toUpperCase()
  const tab = groups.find((g) => g.name === requested)?.name ?? groups[0]?.name
  const [held, setHeld] = useState<Partial<Record<SettingsGroup, HeldDraft>>>({})

  // The draft of a group, or a fresh one when there is none or the server's answer changed since (after a save, or a change made elsewhere
  // seen on return): worked out as the page is drawn, so an answer never meets a draft of the settings before it.
  const basisOf = (group: Group) => JSON.stringify(group.settings)
  const draftFor = (group: Group): GroupDraft => {
    const kept = held[group.name]
    return kept && kept.basis === basisOf(group) ? kept.draft : draftOf(group.settings)
  }
  const server = Object.fromEntries(groups.flatMap((g) => g.settings.map((s) => [s.key, { value: effectiveOf(s), inFile: s.source === 'FILE' }] as const)))
  const valueOf = valueOfDraft(server, Object.assign({}, ...groups.map((g) => draftFor(g).values)) as Record<string, string>)
  const hold = (group: Group, draft: GroupDraft | undefined) =>
    setHeld((h) => {
      const next = { ...h }
      if (draft) next[group.name] = { basis: basisOf(group), draft }
      else delete next[group.name]
      return next
    })

  if (!tab) return null
  return (
    <Box sx={{ mt: 3 }}>
      <TabbedPanels
        label={t('systemSettings.tabs')}
        value={tab}
        onChange={(next) => setParams(next === groups[0]?.name ? {} : { tab: next.toLowerCase() }, { replace: true })}
        tabs={groups.map((group) => {
          const name = t(`systemSettings.groups.${group.name}`)
          return {
            value: group.name,
            label: changesOf(group.settings, draftFor(group)).length > 0 ? t('systemSettings.tabUnsaved', { group: name }) : name,
            content: () => (
              // A new answer of the server starts the group's form afresh (its error and confirmation; the values start afresh above).
              <SettingsGroupForm
                key={basisOf(group)}
                group={group}
                draft={draftFor(group)}
                valueOf={valueOf}
                onDraft={(draft) => hold(group, draft)}
                onSaved={() => hold(group, undefined)}
              />
            ),
          }
        })}
      />
    </Box>
  )
}

function FileStatus({ file }: { file: SystemSettingsQuery['systemSettings']['file'] }) {
  const { t } = useTranslation()
  return (
    <Stack sx={{ gap: 1 }}>
      <DefinitionList items={[{ label: t('systemSettings.file'), value: file.path }]} />
      {!file.exists && (
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {t('systemSettings.fileMissing')}
        </Typography>
      )}
      {file.ignored && (
        <WarningMessage>
          {t('systemSettings.fileIgnored')}
          <ProblemList problems={file.problems} />
        </WarningMessage>
      )}
    </Stack>
  )
}

/** What waits for a restart, and the restart itself: asked once more, then the page waits for the server to go and come back, and reloads. */
function RestartCard({ pending }: { pending: number }) {
  const { t } = useTranslation()
  const heading = useId()
  const client = useApolloClient()
  const [restartServer] = useMutation(RestartServerDocument)
  const [busy, setBusy] = useState(false)
  const [status, setStatus] = useState('')
  const [error, setError] = useState<unknown>()
  const errorText = useErrorText()
  const problems = problemsOf(error)
  const trigger = useRef<HTMLButtonElement>(null)
  // The wait outlives the page when it is left meanwhile; how it ends is then no business of the page open by then.
  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])
  // The button is off while busy, so the focus the confirmation gives back to it as it closes is lost. Once the confirmation is gone and the
  // restart did not go ahead (refused, or the wait ran out), the focus returns to the button, unless it went somewhere else meanwhile.
  const [confirmGone, setConfirmGone] = useState(true)
  useEffect(() => {
    if (confirmGone && !busy && (error !== undefined || status !== '') && (!document.activeElement || document.activeElement === document.body)) trigger.current?.focus()
  }, [confirmGone, busy, error, status])

  async function restart() {
    setBusy(true)
    setError(undefined)
    setStatus(t('systemSettings.restarting'))
    try {
      await restartServer()
    } catch (e) {
      // The host may stop before the answer gets out: a request that went and got no answer is the restart happening. Anything answered is a
      // refusal, and so is an OfflineError: the server was known to be out of reach, so nothing was sent and nothing restarts.
      if (classifyFailure(e) === 'answered') {
        setError(e)
        setStatus('')
        setBusy(false)
        return
      }
    }
    // A deadline per probe, as the runtime's own: a request nobody answers must not hold the wait past `downFor` and `upFor`.
    const probe = () =>
      withDeadline(client.query({ query: HealthDocument, fetchPolicy: 'network-only', context: { offline: 'bypass' } }), PROBE_TIMEOUT_MS).then(
        () => true,
        () => false,
      )
    const outcome = await waitForRestart(probe)
    if (!mounted.current) {
      // The page was left meanwhile: nothing is reloaded under the person's hands. Who is signed in is asked instead, since a restart in
      // another sign-in mode may have changed it.
      if (outcome === 'back') void client.refetchQueries({ include: ['Session'] }).catch(() => undefined)
      return
    }
    if (outcome === 'back') {
      setStatus(t('systemSettings.restartBack'))
      navigation.reload()
    } else {
      setStatus(t('systemSettings.restartTimeout'))
      setBusy(false)
    }
  }

  return (
    <Stack component="section" aria-labelledby={heading} sx={{ gap: 1, mt: 3 }}>
      <Typography component="h2" variant="h5" id={heading}>
        {t('systemSettings.restartTitle')}
      </Typography>
      <Typography variant="body2">{pending > 0 ? t('systemSettings.pending', { count: pending }) : t('systemSettings.nothingPending')}</Typography>
      {error !== undefined &&
        (problems.length > 0 ? (
          // A restart the next start would refuse: the settings at fault, as a refused save lists them.
          <ErrorMessage error={error}>
            {errorText(error)}
            <ProblemList problems={problems} />
          </ErrorMessage>
        ) : (
          <ErrorMessage error={error} />
        ))}
      <div>
        <ConfirmDialog
          trigger={
            <Button ref={trigger} variant="soft" color="error" size="large" disabled={busy}>
              {t('systemSettings.restart')}
            </Button>
          }
          title={t('systemSettings.restartConfirmTitle')}
          description={t('systemSettings.restartConfirmDescription')}
          confirmLabel={t('systemSettings.restart')}
          onConfirm={() => {
            setConfirmGone(false)
            void restart()
          }}
          onClosed={() => setConfirmGone(true)}
        />
      </div>
      <Typography variant="body2" role="status">
        {status}
      </Typography>
    </Stack>
  )
}
