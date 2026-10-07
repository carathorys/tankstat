import { useApolloClient, useMutation } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Card from '@mui/material/Card'
import Typography from '@mui/material/Typography'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { OidcSignIn } from './auth/OidcSignIn.tsx'
import { FieldForm } from './forms/FieldForm.tsx'
import { LoginDocument, RequestPasswordResetDocument, type AuthMode } from './gql/generated.ts'
import { SuccessMessage } from './messages.tsx'

function PasswordLogin() {
  const { t } = useTranslation()
  const client = useApolloClient()
  const [login] = useMutation(LoginDocument)
  const [requestReset] = useMutation(RequestPasswordResetDocument)
  const [forgot, setForgot] = useState(false)
  const [sent, setSent] = useState(false)

  if (forgot) {
    return (
      <>
        <Typography component="h2" variant="h4" sx={{ mb: 1.5 }}>
          {t('auth.forgotTitle')}
        </Typography>
        {sent ? (
          <SuccessMessage>{t('auth.resetSent')}</SuccessMessage>
        ) : (
          <FieldForm
            fields={[{ name: 'email', label: 'fields.email', type: 'email', autoComplete: 'username' }]}
            submitLabel="auth.sendReset"
            onSubmit={async (v) => {
              await requestReset({ variables: { email: v.email ?? '' } })
              setSent(true)
            }}
          />
        )}
        <Button variant="ghost" color="primary" sx={{ mt: 1.5 }} onClick={() => setForgot(false)}>
          {t('auth.backToSignIn')}
        </Button>
      </>
    )
  }

  return (
    <>
      <Typography component="h2" variant="h4" sx={{ mb: 1.5 }}>
        {t('auth.signIn')}
      </Typography>
      <FieldForm
        fields={[
          { name: 'email', label: 'fields.email', type: 'email', autoComplete: 'username' },
          { name: 'password', label: 'fields.password', type: 'password', autoComplete: 'current-password' },
        ]}
        submitLabel="auth.signIn"
        onSubmit={async (v) => {
          await login({ variables: { input: { email: v.email ?? '', password: v.password ?? '' } } })
          await client.resetStore()
        }}
      >
        <Button type="button" variant="ghost" color="primary" onClick={() => setForgot(true)}>
          {t('auth.forgotLink')}
        </Button>
      </FieldForm>
    </>
  )
}

/** What an anonymous visitor sees when the instance requires authentication. */
export function LoginView({ mode }: { mode: AuthMode }) {
  const { t } = useTranslation()

  return (
    <Card sx={{ maxWidth: 420, mx: 'auto', my: 4, p: 3 }}>
      {mode === 'OIDC' && <OidcSignIn />}
      {mode === 'PROXY_HEADER' && (
        <>
          <Typography component="h2" variant="h4" sx={{ mb: 1.5 }}>
            {t('auth.notSignedIn')}
          </Typography>
          <Typography>{t('auth.proxyHint')}</Typography>
        </>
      )}
      {mode !== 'OIDC' && mode !== 'PROXY_HEADER' && <PasswordLogin />}
    </Card>
  )
}
