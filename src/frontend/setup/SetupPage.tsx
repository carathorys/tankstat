import { useApolloClient, useMutation, useQuery } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import FormControlLabel from '@mui/material/FormControlLabel'
import Link from '@mui/material/Link'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link as RouterLink, useNavigate } from 'react-router'
import { RestartWait, RestoreDialog } from '../admin/restore.tsx'
import { Loading } from '../components/Loading.tsx'
import { FieldForm } from '../forms/FieldForm.tsx'
import {
  SessionDocument,
  SetupAcknowledgeOpenAccessDocument,
  SetupClaimAdministratorDocument,
  SetupCreateAdministratorDocument,
  SetupDocument,
  SetupRestorePointsDocument,
  type AuthMode,
  type SetupRestorePointsQuery,
} from '../gql/generated.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { useFormat } from '../i18n/format.ts'
import { ErrorMessage, WarningMessage } from '../messages.tsx'

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

  return (
    <section aria-labelledby="page-title">
      <Typography id="page-title" component="h1" variant="h3" sx={{ mb: 1.5 }}>
        {t('setup.title')}
      </Typography>
      {error !== undefined && !data && <ErrorMessage error={error} />}
      {loading && !data && <Loading />}
      {data && !data.setup.pending && (
        <Stack sx={{ gap: 1.5, alignItems: 'flex-start' }}>
          <Typography>{t('setup.notPending')}</Typography>
          <Link component={RouterLink} to="/">
            {t('setup.home')}
          </Link>
        </Stack>
      )}
      {data?.setup.pending && <Steps mode={mode} />}
    </section>
  )
}

function Steps({ mode }: { mode: AuthMode }) {
  const { t } = useTranslation()
  const client = useApolloClient()
  const { data: session } = useQuery(SessionDocument)
  const [entered, setEntered] = useState<{ code: string; points: RestorePoint[] }>()
  const [restoring, setRestoring] = useState<RestorePoint>()

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
        point={restoring}
        generation={session?.session.instance?.generation ?? undefined}
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
      <RestoreStep points={entered.points} code={entered.code} onLookAgain={() => check(entered.code)} onRestoring={setRestoring} />
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
  onRestoring: (point: RestorePoint) => void
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
                      <Button color="error" variant="soft" aria-label={t('setup.restoreOne', { made: dateTime(point.createdAt) })}>
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

/** After the step that completes the setup: the session is asked again (the notice is gone, an administrator signed in), then the start page. */
function useFinish() {
  const client = useApolloClient()
  const navigate = useNavigate()
  return async () => {
    await client.resetStore()
    void navigate('/', { replace: true })
  }
}

/** Standalone: the first administrator, signed in on this browser as it is made. */
function AdministratorStep({ code }: { code: string }) {
  const { t } = useTranslation()
  const [create] = useMutation(SetupCreateAdministratorDocument)
  const finish = useFinish()
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
  const finish = useFinish()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>()
  const submit = async () => {
    setBusy(true)
    setError(undefined)
    try {
      await claim({ variables: { code } })
      await finish()
    } catch (err) {
      setError(err)
      setBusy(false)
    }
  }
  return (
    <Stack sx={{ gap: 1.5, alignItems: 'flex-start', maxWidth: 560 }}>
      <Typography>{t('setup.claimIntro', { name })}</Typography>
      {error !== undefined && <ErrorMessage error={error} />}
      <Button loading={busy} onClick={() => void submit()}>
        {t('setup.claim')}
      </Button>
    </Stack>
  )
}

/** Sign-in off: nobody is an administrator, so the setup ends with the acknowledgement of what that means. */
function OpenAccessStep({ code }: { code: string }) {
  const { t } = useTranslation()
  const [acknowledge] = useMutation(SetupAcknowledgeOpenAccessDocument)
  const finish = useFinish()
  const [understood, setUnderstood] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>()
  const submit = async () => {
    setBusy(true)
    setError(undefined)
    try {
      await acknowledge({ variables: { code } })
      await finish()
    } catch (err) {
      setError(err)
      setBusy(false)
    }
  }
  return (
    <Stack sx={{ gap: 1.5, alignItems: 'flex-start', maxWidth: 560 }}>
      <WarningMessage>{t('setup.openIntro')}</WarningMessage>
      <FormControlLabel control={<Checkbox checked={understood} onChange={(e) => setUnderstood(e.target.checked)} />} label={t('setup.openAcknowledge')} />
      {error !== undefined && <ErrorMessage error={error} />}
      <Button loading={busy} disabled={!understood} onClick={() => void submit()}>
        {t('setup.finish')}
      </Button>
    </Stack>
  )
}
