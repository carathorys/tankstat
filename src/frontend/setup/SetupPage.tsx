import { useApolloClient, useMutation, useQuery } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import FormControlLabel from '@mui/material/FormControlLabel'
import Link from '@mui/material/Link'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link as RouterLink, useNavigate } from 'react-router'
import { EncryptionSetup } from '../admin/encryption.tsx'
import { RestartWait, RestoreDialog } from '../admin/restore.tsx'
import { Loading } from '../components/Loading.tsx'
import { FieldForm } from '../forms/FieldForm.tsx'
import {
  BackupRecipientDocument,
  SessionDocument,
  SetupAcknowledgeOpenAccessDocument,
  SetupClaimAdministratorDocument,
  SetupCreateAdministratorDocument,
  SetupDocument,
  SetupRestorePointsDocument,
  type AuthMode,
  type RestoreTargetFieldsFragment,
  type SetupRestorePointsQuery,
} from '../gql/generated.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { useFormat } from '../i18n/format.ts'
import { ErrorMessage, SuccessMessage, WarningMessage } from '../messages.tsx'
import { standaloneLink } from '../theme/components.ts'

type RestorePoint = SetupRestorePointsQuery['setupRestorePoints'][number]

/**
 * The setup of a new instance (#90): while it waits for its gatekeeper, the one-time code from the server's log opens what no administrator
 * could, since none exists yet: restoring a backup of the folder (the restored data brings its own users and setup), or starting fresh with
 * the first administrator (Standalone), becoming it (OIDC, proxy: the signed-in user) or acknowledging open access (sign-in off). The code
 * stays in this page only (never stored); a restart makes a new one.
 */
export function SetupPage({ mode }: { mode: AuthMode }) {
  const { t } = useTranslation()
  usePageTitle(t('setup.title'))
  // Asked of the server, never of the device: whether the instance still waits is its business, and a kept answer would be stale.
  const { data, error, loading } = useQuery(SetupDocument, { fetchPolicy: 'network-only' })
  const client = useApolloClient()
  const { data: session } = useQuery(SessionDocument)
  const isAdmin = session?.session.user?.isAdmin === true
  // Completed since the session was asked (on another server of the instance, say): the session is asked again, so its notice, and
  // the takeover of every page with it, go too.
  const stale = data?.setup.pending === false && session?.notices.some((n) => n.code === 'SETUP_PENDING') === true
  useEffect(() => {
    if (stale) void client.refetchQueries({ include: [SessionDocument] })
  }, [client, stale])

  return (
    <section aria-labelledby="page-title">
      <Typography id="page-title" component="h1" variant="h3" sx={{ mb: 1.5 }}>
        {t('setup.title')}
      </Typography>
      {error !== undefined && !data && <ErrorMessage error={error} />}
      {loading && !data && <Loading />}
      {data && !data.setup.pending && (isAdmin ? <Done mode={mode} /> : <NothingToDo />)}
      {data?.setup.pending && <Steps mode={mode} />}
    </section>
  )
}

function NothingToDo() {
  const { t } = useTranslation()
  return (
    <Stack sx={{ gap: 1.5, alignItems: 'flex-start' }}>
      <Typography>{t('setup.notPending')}</Typography>
      <Link component={RouterLink} to="/" sx={standaloneLink}>
        {t('setup.home')}
      </Link>
    </Stack>
  )
}

/**
 * After the setup, for an administrator (the one it just made lands here): what is worth setting up next, none of it part of the setup
 * itself. Backups encrypted (the Backups page's own key set, here at once), then where the schedule, the defaults and the users are.
 */
function Done({ mode }: { mode: AuthMode }) {
  const { t } = useTranslation()
  // The recipient alone, past the cache: an answer of only the settings would replace the Backups page's whole overview there.
  const { data, error } = useQuery(BackupRecipientDocument, { fetchPolicy: 'no-cache' })
  const next: { to: string; label: string }[] = [
    { to: '/admin/settings?tab=backups', label: t('setup.nextBackups') },
    { to: '/admin/settings?tab=defaults', label: t('setup.nextDefaults') },
    ...(mode === 'STANDALONE' ? [{ to: '/admin/people', label: t('setup.nextPeople') }] : []),
    { to: '/', label: t('setup.home') },
  ]
  return (
    <Stack sx={{ gap: 3, maxWidth: 720 }}>
      <SuccessMessage>{t('setup.done')}</SuccessMessage>
      {data ? <EncryptionSetup recipient={data.backups.settings.recipient ?? null} refresh={BackupRecipientDocument} /> : error !== undefined ? <ErrorMessage error={error} /> : <Loading />}
      <section aria-labelledby="setup-next">
        <Typography id="setup-next" component="h2" variant="h5" sx={{ mb: 1 }}>
          {t('setup.nextTitle')}
        </Typography>
        <Stack component="ul" sx={{ m: 0, pl: 2.5, gap: 1 }}>
          {next.map((item) => (
            <li key={item.to}>
              <Link component={RouterLink} to={item.to} sx={standaloneLink}>
                {item.label}
              </Link>
            </li>
          ))}
        </Stack>
      </section>
    </Stack>
  )
}

function Steps({ mode }: { mode: AuthMode }) {
  const { t } = useTranslation()
  const client = useApolloClient()
  const { data: session } = useQuery(SessionDocument)
  const [entered, setEntered] = useState<{ code: string; points: RestorePoint[] }>()
  // The generation of the data when the restore was asked for, kept with it: the session's own may change while the wait runs.
  const [restoring, setRestoring] = useState<{ point: RestoreTargetFieldsFragment; generation: string | undefined }>()

  const check = async (code: string) => {
    const { data } = await client.query({ query: SetupRestorePointsDocument, variables: { code }, fetchPolicy: 'network-only' })
    setEntered({ code, points: data?.setupRestorePoints ?? [] })
  }
  const startOver = () => {
    setRestoring(undefined)
    setEntered(undefined)
  }

  if (restoring)
    return (
      <RestartWait
        point={restoring.point}
        generation={restoring.generation}
        notRestored={<Button onClick={startOver}>{t('setup.newCode')}</Button>}
      />
    )

  if (!entered)
    return (
      <Stack sx={{ gap: 1.5, maxWidth: 560 }}>
        <Typography>{t('setup.intro')}</Typography>
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {t('setup.codeWhere')}
        </Typography>
        <FieldForm fields={[{ name: 'code', label: 'setup.code', autoComplete: 'off' }]} submitLabel="setup.continue" onSubmit={(v) => check(v.code ?? '')} />
      </Stack>
    )

  return (
    <Stack sx={{ gap: 3, maxWidth: 720 }}>
      <RestoreStep
        points={entered.points}
        code={entered.code}
        onLookAgain={() => check(entered.code)}
        onRestoring={(point) => setRestoring({ point, generation: session?.session.instance?.generation ?? undefined })}
      />
      <section aria-labelledby="setup-fresh">
        <Typography id="setup-fresh" component="h2" variant="h4" sx={{ mb: 1 }}>
          {t('setup.freshTitle')}
        </Typography>
        {mode === 'STANDALONE' && <AdministratorStep code={entered.code} />}
        {(mode === 'OIDC' || mode === 'PROXY_HEADER') && <ClaimStep code={entered.code} name={session?.session.user?.displayName ?? ''} />}
        {mode === 'NONE' && <OpenAccessStep code={entered.code} />}
      </section>
      <Button variant="ghost" color="primary" sx={{ alignSelf: 'flex-start' }} onClick={startOver}>
        {t('setup.otherCode')}
      </Button>
    </Stack>
  )
}

/** The backups of the folder: one restored is the instance's data from then on, its users and its setup included. */
function RestoreStep({
  points,
  code,
  onLookAgain,
  onRestoring,
}: {
  points: RestorePoint[]
  code: string
  onLookAgain: () => Promise<void>
  onRestoring: (point: RestoreTargetFieldsFragment) => void
}) {
  const { t } = useTranslation()
  const { dateTime } = useFormat()
  const [error, setError] = useState<unknown>()
  const lookAgain = () => {
    setError(undefined)
    onLookAgain().catch(setError)
  }
  return (
    <section aria-labelledby="setup-restore">
      <Typography id="setup-restore" component="h2" variant="h4" sx={{ mb: 1 }}>
        {t('setup.restoreTitle')}
      </Typography>
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
                </Stack>
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
            )
          })}
        </Stack>
      )}
      {error !== undefined && <ErrorMessage error={error} />}
      <Button variant="ghost" color="primary" onClick={lookAgain}>
        {t('setup.lookAgain')}
      </Button>
    </section>
  )
}

/**
 * After the step that completes the setup: the session is asked again (the notice is gone, an administrator signed in), then the done
 * screen for the new administrator (`/setup`, its next steps), or the start page with sign-in off, where nobody administers anything.
 */
function useFinish(to: '/setup' | '/') {
  const client = useApolloClient()
  const navigate = useNavigate()
  return async () => {
    // The address first: the session's new answer ends the takeover and draws the page of the address, which unmounts this step (a
    // navigate after that would be lost).
    void navigate(to, { replace: true })
    await client.resetStore()
  }
}

/** Standalone: the first administrator, signed in on this browser as it is made. */
function AdministratorStep({ code }: { code: string }) {
  const { t } = useTranslation()
  const [create] = useMutation(SetupCreateAdministratorDocument)
  const finish = useFinish('/setup')
  return (
    <Stack sx={{ gap: 1.5, maxWidth: 480 }}>
      <Typography>{t('setup.adminIntro')}</Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary' }}>
        {t('setup.passwordHint')}
      </Typography>
      <FieldForm
        fields={[
          { name: 'email', label: 'fields.email', type: 'email', autoComplete: 'username' },
          { name: 'password', label: 'fields.password', type: 'password', autoComplete: 'new-password' },
        ]}
        submitLabel="setup.createAdmin"
        onSubmit={async (v) => {
          await create({ variables: { input: { code, email: v.email ?? '', password: v.password ?? '' } } })
          await finish()
        }}
      />
    </Stack>
  )
}

/** OIDC and proxy mode: the signed-in user becomes the first administrator. */
function ClaimStep({ code, name }: { code: string; name: string }) {
  const { t } = useTranslation()
  const [claim] = useMutation(SetupClaimAdministratorDocument)
  const finish = useFinish('/setup')
  const { busy, error, run } = useStepAction(async () => {
    await claim({ variables: { code } })
    await finish()
  })
  return (
    <Stack sx={{ gap: 1.5, alignItems: 'flex-start', maxWidth: 560 }}>
      <Typography>{t('setup.claimIntro', { name })}</Typography>
      {error !== undefined && <ErrorMessage error={error} />}
      <Button loading={busy} onClick={run}>
        {t('setup.claim')}
      </Button>
    </Stack>
  )
}

/** Sign-in off: nobody is an administrator, so the setup ends with the acknowledgement of what that means. */
function OpenAccessStep({ code }: { code: string }) {
  const { t } = useTranslation()
  const [acknowledge] = useMutation(SetupAcknowledgeOpenAccessDocument)
  const finish = useFinish('/')
  const [understood, setUnderstood] = useState(false)
  const { busy, error, run } = useStepAction(async () => {
    await acknowledge({ variables: { code } })
    await finish()
  })
  return (
    <Stack sx={{ gap: 1.5, alignItems: 'flex-start', maxWidth: 560 }}>
      <WarningMessage>{t('setup.openIntro')}</WarningMessage>
      <FormControlLabel control={<Checkbox checked={understood} onChange={(e) => setUnderstood(e.target.checked)} />} label={t('setup.openAcknowledge')} />
      {error !== undefined && <ErrorMessage error={error} />}
      <Button loading={busy} disabled={!understood} onClick={run}>
        {t('setup.finish')}
      </Button>
    </Stack>
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
