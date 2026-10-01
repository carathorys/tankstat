import { useApolloClient, useMutation } from '@apollo/client/react'
import { useState } from 'react'
import { Form } from './forms.tsx'
import { LOGIN_MUTATION, REQUEST_RESET_MUTATION, type AuthMode } from './session.ts'

function PasswordLogin() {
  const client = useApolloClient()
  const [login] = useMutation(LOGIN_MUTATION)
  const [requestReset] = useMutation(REQUEST_RESET_MUTATION)
  const [forgot, setForgot] = useState(false)
  const [sent, setSent] = useState(false)

  if (forgot) {
    return (
      <section>
        <h2>Forgot your password?</h2>
        {sent ? (
          <p role="status">
            If that address is registered and the server can send e-mail, a reset link is on its way. Otherwise ask an
            administrator for a reset link.
          </p>
        ) : (
          <Form
            fields={[{ name: 'email', label: 'E-mail', type: 'email', autoComplete: 'username' }]}
            submitLabel="Send reset link"
            onSubmit={async (v) => {
              await requestReset({ variables: { email: v.email ?? '' } })
              setSent(true)
            }}
          />
        )}
        <button type="button" onClick={() => setForgot(false)}>
          Back to sign in
        </button>
      </section>
    )
  }

  return (
    <section>
      <h2>Sign in</h2>
      <Form
        fields={[
          { name: 'email', label: 'E-mail', type: 'email', autoComplete: 'username' },
          { name: 'password', label: 'Password', type: 'password', autoComplete: 'current-password' },
        ]}
        submitLabel="Sign in"
        onSubmit={async (v) => {
          await login({ variables: { input: { email: v.email ?? '', password: v.password ?? '' } } })
          await client.resetStore()
        }}
      >
        <button type="button" onClick={() => setForgot(true)}>
          Forgot password?
        </button>
      </Form>
    </section>
  )
}

/** What an anonymous visitor sees when the instance requires authentication. */
export function LoginView({ mode }: { mode: AuthMode }) {
  if (mode === 'OIDC') {
    return (
      <section>
        <h2>Sign in</h2>
        <p>
          <a href="/auth/oidc/login">Sign in with your identity provider</a>
        </p>
      </section>
    )
  }
  if (mode === 'PROXY_HEADER') {
    return (
      <section>
        <h2>Not signed in</h2>
        <p>Access is controlled by your reverse proxy. Open this app through the proxy and sign in there.</p>
      </section>
    )
  }
  return <PasswordLogin />
}
