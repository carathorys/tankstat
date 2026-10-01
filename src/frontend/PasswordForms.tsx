import { useApolloClient, useMutation } from '@apollo/client/react'
import { useState } from 'react'
import { Form } from './forms.tsx'
import { CHANGE_PASSWORD_MUTATION, LOGOUT_MUTATION, RESET_PASSWORD_MUTATION } from './session.ts'

export function ChangePasswordForm() {
  const [changePassword] = useMutation(CHANGE_PASSWORD_MUTATION)
  const [done, setDone] = useState(false)

  return (
    <section>
      <h2>Change password</h2>
      {done && <p role="status">Password changed. Your other sessions were signed out.</p>}
      <Form
        fields={[
          { name: 'current', label: 'Current password', type: 'password', autoComplete: 'current-password' },
          { name: 'next', label: 'New password', type: 'password', autoComplete: 'new-password' },
        ]}
        submitLabel="Update password"
        onSubmit={async (v) => {
          await changePassword({ variables: { input: { currentPassword: v.current ?? '', newPassword: v.next ?? '' } } })
          setDone(true)
        }}
      />
    </section>
  )
}

export function LogoutButton() {
  const client = useApolloClient()
  const [logout] = useMutation(LOGOUT_MUTATION)

  return (
    <button
      type="button"
      onClick={async () => {
        await logout()
        await client.resetStore()
      }}
    >
      Sign out
    </button>
  )
}

/** Opened from the link in a reset/setup e-mail (or one an administrator handed over). */
export function ResetPasswordView({ token }: { token: string }) {
  const [resetPassword] = useMutation(RESET_PASSWORD_MUTATION)
  const [done, setDone] = useState(false)

  if (done) {
    return (
      <section>
        <p role="status">Your password has been set.</p>
        <a href="/">Continue to sign in</a>
      </section>
    )
  }

  return (
    <section>
      <h2>Set your password</h2>
      <Form
        fields={[{ name: 'password', label: 'New password', type: 'password', autoComplete: 'new-password' }]}
        submitLabel="Set password"
        onSubmit={async (v) => {
          await resetPassword({ variables: { input: { token, newPassword: v.password ?? '' } } })
          setDone(true)
        }}
      />
    </section>
  )
}
