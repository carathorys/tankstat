import { ChangePasswordForm } from '../PasswordForms.tsx'
import type { AuthMode, SessionUser } from '../session.ts'

export function AccountPage({ mode, user }: { mode: AuthMode; user: SessionUser | null }) {
  return (
    <section>
      <h1>Account</h1>
      {user ? (
        <p>
          Signed in as <strong>{user.displayName}</strong> ({user.email || 'no e-mail'}){user.isAdmin && ', administrator'}.
        </p>
      ) : (
        <p>Authentication is disabled, so there is no personal account.</p>
      )}
      {user && mode === 'STANDALONE' && <ChangePasswordForm />}
      {user && mode !== 'STANDALONE' && <p>Your password is managed by your identity provider or proxy.</p>}
    </section>
  )
}
