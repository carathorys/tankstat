import { useApolloClient, useMutation } from '@apollo/client/react'
import { Button, Card, Heading, Text } from '@radix-ui/themes'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { OidcSignIn } from './auth/OidcSignIn.tsx'
import { FieldForm } from './forms.tsx'
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
        <Heading as="h2" size="5" mb="3">
          {t('auth.forgotTitle')}
        </Heading>
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
        <Button mt="3" variant="ghost" onClick={() => setForgot(false)}>
          {t('auth.backToSignIn')}
        </Button>
      </>
    )
  }

  return (
    <>
      <Heading as="h2" size="5" mb="3">
        {t('auth.signIn')}
      </Heading>
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
        <Button type="button" variant="ghost" onClick={() => setForgot(true)}>
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
    <Card size="3" style={{ maxWidth: 420, margin: '2rem auto' }}>
      {mode === 'OIDC' && <OidcSignIn />}
      {mode === 'PROXY_HEADER' && (
        <>
          <Heading as="h2" size="5" mb="3">
            {t('auth.notSignedIn')}
          </Heading>
          <Text as="p">{t('auth.proxyHint')}</Text>
        </>
      )}
      {mode !== 'OIDC' && mode !== 'PROXY_HEADER' && <PasswordLogin />}
    </Card>
  )
}
