import { useApolloClient, useQuery } from '@apollo/client/react'
import Link from '@mui/material/Link'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { Link as RouterLink } from 'react-router'
import { EncryptionSetup } from '../admin/encryption.tsx'
import { Loading } from '../components/Loading.tsx'
import { BackupRecipientDocument, SessionDocument, SetupDocument, type AuthMode } from '../gql/generated.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { ErrorMessage, SuccessMessage } from '../messages.tsx'
import { standaloneLink } from '../theme/components.ts'
import { remember } from './claimNext.ts'
import { SetupWizard } from './SetupWizard.tsx'

/**
 * The setup of a new instance (#90): while it waits for its gatekeeper, the wizard (`SetupWizard`: the one-time code from the server's
 * log, the settings, the first administrator or the acknowledgement, or a backup restored instead); once it is done, the done screen for an
 * administrator, "nothing to set up" for anyone else.
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
  useEffect(() => {
    if (data?.setup.pending === false) remember(false) // nothing of the wizard is left for this tab once the setup is done
  }, [data])

  return (
    <section aria-labelledby="page-title">
      <Typography id="page-title" component="h1" variant="h3" sx={{ mb: 1.5 }}>
        {t('setup.title')}
      </Typography>
      {error !== undefined && !data && <ErrorMessage error={error} />}
      {loading && !data && <Loading />}
      {data && !data.setup.pending && (isAdmin ? <Done mode={mode} /> : <NothingToDo />)}
      {data?.setup.pending && <SetupWizard />}
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
      {data ? <EncryptionSetup recipient={data.backups.settings.recipient ?? null} passphraseMinLength={data.backups.settings.passphraseMinLength} refresh={BackupRecipientDocument} /> : error !== undefined ? <ErrorMessage error={error} /> : <Loading />}
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
