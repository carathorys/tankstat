import type { ApolloCache } from '@apollo/client'
import { useApolloClient, useMutation, useQuery } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Card from '@mui/material/Card'
import Link from '@mui/material/Link'
import Typography from '@mui/material/Typography'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSignOut } from './auth/useSignOut.ts'
import { FieldForm } from './forms/FieldForm.tsx'
import { ChangePasswordDocument, ResetPasswordDocument, SessionDocument } from './gql/generated.ts'
import { SuccessMessage } from './messages.tsx'
import { OfflineError } from './offline/errors.ts'
import { standaloneLink } from './theme/components.ts'
import { useToast } from './toast/toastContext.ts'

/** The Account page's change form; also the form of the change asked at sign-in (`required`), whose view has its own heading. */
export function ChangePasswordForm({ required = false }: { required?: boolean }) {
  const { t } = useTranslation()
  const { toast } = useToast()
  const client = useApolloClient()
  // Whose password it is: the App's own Session answer, from the cache (the strength meter counts their address and name against it).
  const me = useQuery(SessionDocument, { fetchPolicy: 'cache-only' }).data?.session.user
  const [changePassword] = useMutation(ChangePasswordDocument)
  const [done, setDone] = useState(false)

  // The change asked at sign-in ends with the new password, which cleared the flag on the server: the pages come back at once, never held
  // up by asking the session again (a failed answer would make a saved password look refused, and the device's kept answer still has the
  // flag), which then goes past the device and keeps the server's answer for it.
  const lift = (cache: ApolloCache) =>
    cache.updateQuery({ query: SessionDocument }, (data) =>
      data?.session.user ? { ...data, session: { ...data.session, user: { ...data.session.user, passwordChangeRequired: false } } } : data,
    )

  return (
    <section>
      {!required && (
        <Typography component="h2" variant="h5" sx={{ my: 1.5 }}>
          {t('auth.changeTitle')}
        </Typography>
      )}
      {done && <SuccessMessage>{t('auth.changed')}</SuccessMessage>}
      <FieldForm
        fields={[
          { name: 'current', label: 'fields.currentPassword', type: 'password', autoComplete: 'current-password' },
          { name: 'next', label: 'fields.newPassword', type: 'newPassword', repeatLabel: 'fields.newPasswordRepeat', personal: [me?.email, me?.displayName] },
        ]}
        submitLabel="auth.changeSubmit"
        onSubmit={async (v) => {
          await changePassword({
            variables: { input: { currentPassword: v.current ?? '', newPassword: v.next ?? '', newPasswordConfirmation: v.nextRepeat ?? '' } },
            ...(required ? { update: lift } : {}),
          })
          if (!required) {
            setDone(true)
            return
          }
          toast(t('auth.changed'))
          void client
            .query({ query: SessionDocument, fetchPolicy: 'network-only', context: { offline: 'bypass', queryDeduplication: false } })
            .catch(() => undefined)
        }}
      />
    </section>
  )
}

/**
 * Shown in place of every page while the session says the password last signed in with is shorter than the minimum now
 * (`passwordChangeRequired`, Standalone only): why, the change form, and Sign out. The server holds nothing else back.
 */
export function PasswordChangeRequiredView() {
  const { t } = useTranslation()
  const { toast } = useToast()
  const signOut = useSignOut('STANDALONE')
  const leave = async () => {
    try {
      await signOut()
    } catch (error) {
      // Signing out ends the session on the server: without it the device stays signed in, so say why nothing happened.
      if (error instanceof OfflineError) toast(t('errors.offline'))
      else throw error
    }
  }

  return (
    <Card sx={{ maxWidth: 420, mx: 'auto', my: 4, p: 3 }}>
      <Typography component="h1" variant="h4" sx={{ mb: 1.5 }}>
        {t('auth.changeRequired.title')}
      </Typography>
      <Typography sx={{ mb: 1.5 }}>{t('auth.changeRequired.text')}</Typography>
      <ChangePasswordForm required />
      <Button variant="ghost" color="primary" sx={{ mt: 1.5 }} onClick={() => void leave()}>
        {t('nav.signOut')}
      </Button>
    </Card>
  )
}

/** Opened from the link in a reset/setup e-mail (or one an administrator handed over). */
export function ResetPasswordView({ token }: { token: string }) {
  const { t } = useTranslation()
  const [resetPassword] = useMutation(ResetPasswordDocument)
  const [done, setDone] = useState(false)

  return (
    <Card sx={{ maxWidth: 420, mx: 'auto', my: 4, p: 3 }}>
      {done ? (
        <>
          <SuccessMessage>{t('auth.setDone')}</SuccessMessage>
          <Link href="/" sx={standaloneLink}>
            {t('auth.continueSignIn')}
          </Link>
        </>
      ) : (
        <>
          <Typography component="h2" variant="h4" sx={{ mb: 1.5 }}>
            {t('auth.setTitle')}
          </Typography>
          <FieldForm
            fields={[{ name: 'password', label: 'fields.newPassword', type: 'newPassword', repeatLabel: 'fields.newPasswordRepeat' }]}
            submitLabel="auth.setSubmit"
            onSubmit={async (v) => {
              await resetPassword({ variables: { input: { token, newPassword: v.password ?? '', newPasswordConfirmation: v.passwordRepeat ?? '' } } })
              setDone(true)
            }}
          />
        </>
      )}
    </Card>
  )
}
