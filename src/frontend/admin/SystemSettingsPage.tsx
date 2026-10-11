import { useApolloClient, useMutation, useQuery } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ConfirmDialog } from '../components/ConfirmDialog.tsx'
import { DefinitionList } from '../components/DefinitionList.tsx'
import { Loading } from '../components/Loading.tsx'
import { HealthDocument, RestartServerDocument, SystemSettingsDocument, type SystemSettingsQuery } from '../gql/generated.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { ErrorMessage, WarningMessage } from '../messages.tsx'
import { navigation } from '../navigation.ts'
import { isConnectionFailure } from '../offline/errors.ts'
import { waitForRestart } from './restartFlow.ts'
import { SettingsGroupForm } from './SettingsGroupForm.tsx'

/**
 * The settings the server runs with, for administrators: the app's own settings file (what became of it at the last start), the restart
 * that makes saved changes count, and a form per group. It needs the server (nothing of it is kept on the device).
 */
export function SystemSettingsPage() {
  const { t } = useTranslation()
  usePageTitle(t('systemSettings.title'))
  const { data, error } = useQuery(SystemSettingsDocument, { fetchPolicy: 'cache-and-network' })
  const settings = data?.systemSettings

  return (
    <section aria-labelledby="page-title">
      <Typography id="page-title" component="h1" variant="h3" sx={{ mb: 1.5 }}>
        {t('systemSettings.title')}
      </Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary', mb: 2 }}>
        {t('systemSettings.description')}
      </Typography>
      {error && !data && <ErrorMessage error={error} />}
      {!data && !error && <Loading />}
      {settings && (
        <>
          <FileStatus file={settings.file} />
          <RestartCard pending={settings.groups.flatMap((g) => g.settings).filter((s) => s.restartPending).length} />
          {settings.groups.map((group) => (
            // A new answer of the server starts the group's form afresh (after a save, or on a change made elsewhere seen on return).
            <SettingsGroupForm key={`${group.name}:${JSON.stringify(group.settings)}`} group={group} />
          ))}
        </>
      )}
    </section>
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
          {file.problems.length > 0 && (
            <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
              {file.problems.map((p) => (
                <li key={p}>{p}</li>
              ))}
            </Box>
          )}
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

  async function restart() {
    setBusy(true)
    setError(undefined)
    setStatus(t('systemSettings.restarting'))
    try {
      await restartServer()
    } catch (e) {
      // The host may stop before the answer gets out: a lost connection is the restart happening, anything else a refusal.
      if (!isConnectionFailure(e)) {
        setError(e)
        setStatus('')
        setBusy(false)
        return
      }
    }
    const probe = () => client.query({ query: HealthDocument, fetchPolicy: 'network-only', context: { offline: 'bypass' } }).then(() => true, () => false)
    const outcome = await waitForRestart(probe)
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
      {error !== undefined && <ErrorMessage error={error} />}
      <div>
        <ConfirmDialog
          trigger={
            <Button variant="soft" color="error" size="large" disabled={busy}>
              {t('systemSettings.restart')}
            </Button>
          }
          title={t('systemSettings.restartConfirmTitle')}
          description={t('systemSettings.restartConfirmDescription')}
          confirmLabel={t('systemSettings.restart')}
          onConfirm={() => void restart()}
        />
      </div>
      <Typography variant="body2" role="status">
        {status}
      </Typography>
    </Stack>
  )
}
