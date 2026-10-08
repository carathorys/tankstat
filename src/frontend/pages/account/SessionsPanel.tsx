import { useMutation, useQuery } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Card from '@mui/material/Card'
import Chip from '@mui/material/Chip'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSignOut } from '../../auth/useSignOut.ts'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { Loading } from '../../components/Loading.tsx'
import { MySessionsDocument, RevokeOtherSessionsDocument, RevokeSessionDocument, type AuthMode, type MySessionsQuery } from '../../gql/generated.ts'
import { useFormat } from '../../i18n/format.ts'
import { ErrorMessage } from '../../messages.tsx'

type Session = MySessionsQuery['mySessions'][number]

/**
 * The devices the user is signed in on (Standalone and OIDC): each with when it signed in and was last used, this one marked. Signing
 * another device out ends its session at once; signing this one out is the normal sign-out. "Sign out everywhere else" ends all the
 * others after a confirmation.
 */
export function SessionsPanel({ mode }: { mode: AuthMode }) {
  const { t } = useTranslation()
  const { dateTime } = useFormat()
  const heading = useId()
  const { data, error, refetch } = useQuery(MySessionsDocument, { fetchPolicy: 'cache-and-network' })
  const [revoke] = useMutation(RevokeSessionDocument)
  const [revokeOthers] = useMutation(RevokeOtherSessionsDocument)
  const signOut = useSignOut(mode)
  const [status, setStatus] = useState('')
  const [actionError, setActionError] = useState<unknown>()

  const name = (s: Session) => s.client ?? t('account.sessions.unknownDevice')

  async function run(action: () => Promise<string | undefined>) {
    setActionError(undefined)
    setStatus('')
    try {
      const said = await action()
      if (said) setStatus(said)
    } catch (e) {
      setActionError(e)
    }
  }

  const end = (s: Session) =>
    run(async () => {
      if (s.current) {
        await signOut() // this device: the usual sign-out, which shows the sign-in screen
        return undefined
      }
      const result = await revoke({ variables: { id: s.id } })
      await refetch()
      // False: it was signed out already (from another tab or device); the list, asked again, shows how things are.
      return result.data?.revokeSession ? t('account.sessions.signedOutOne', { device: name(s) }) : undefined
    })

  const endOthers = () =>
    run(async () => {
      const result = await revokeOthers()
      await refetch()
      return t('account.sessions.signedOutOthers', { count: result.data?.revokeOtherSessions ?? 0 })
    })

  const others = data?.mySessions.filter((s) => !s.current).length ?? 0

  return (
    <Stack component="section" aria-labelledby={heading} sx={{ gap: 1.5, mt: 3 }}>
      <div>
        <Typography component="h2" variant="h5" id={heading} sx={{ mb: 0.5 }}>
          {t('account.sessions.title')}
        </Typography>
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {t('account.sessions.description')}
        </Typography>
      </div>
      {error && !data && <ErrorMessage error={error} />}
      {actionError !== undefined && <ErrorMessage error={actionError} />}
      {!data && !error && <Loading />}
      {data && (
        <Stack component="ul" sx={{ gap: 1, listStyle: 'none', p: 0, m: 0 }}>
          {data.mySessions.map((s) => (
            <li key={s.id}>
              <Card sx={{ p: 1.5 }}>
                <Stack direction="row" sx={{ alignItems: 'center', gap: 1.5, flexWrap: 'wrap', justifyContent: 'space-between' }}>
                  <div>
                    <Stack direction="row" sx={{ alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                      <Typography component="h3" variant="subtitle1">
                        {name(s)}
                      </Typography>
                      {s.current && <Chip color="primary" label={t('account.sessions.thisDevice')} />}
                    </Stack>
                    <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                      {t('account.sessions.signedIn', { time: dateTime(s.createdAt) })} · {t('account.sessions.lastUsed', { time: dateTime(s.lastUsedAt) })}
                    </Typography>
                  </div>
                  <Button color="error" variant="soft" size="large" aria-label={t('account.sessions.signOutAria', { device: name(s) })} onClick={() => void end(s)}>
                    {t('account.sessions.signOut')}
                  </Button>
                </Stack>
              </Card>
            </li>
          ))}
        </Stack>
      )}
      {others > 0 && (
        <div>
          <ConfirmDialog
            trigger={
              <Button variant="soft" color="error" size="large">
                {t('account.sessions.signOutOthers')}
              </Button>
            }
            title={t('account.sessions.signOutOthersTitle')}
            description={t('account.sessions.signOutOthersDescription')}
            confirmLabel={t('account.sessions.signOutOthers')}
            onConfirm={() => void endOthers()}
          />
        </div>
      )}
      <Typography variant="body2" role="status">
        {status}
      </Typography>
    </Stack>
  )
}
