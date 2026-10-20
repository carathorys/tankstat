import { request, type FullConfig } from '@playwright/test'
import { ADMIN } from './app.ts'

/** The password minimum the shared server runs the tests with: above the users made here, below every other password the tests use (17+). */
export const RAISED_MINIMUM = 14

/**
 * Once, before any test (each CI shard against its own server): a user per project whose password is the floor's 10 characters, then the
 * shared server's password minimum raised above it. Their next sign-in is asked to change the password (`account.e2e.ts`). Done here, not
 * in the test, because the raise would race the other size's user, and every other test's, made with a short password meanwhile; it is
 * never lowered, and every other password of the tests (`createUser`'s, the administrator's, the setup's) is longer than the minimum.
 * The users reach the tests as `E2E_SHORT_PASSWORDS` (JSON by project name), as the waiting servers do as `E2E_SETUP`.
 */
export default async function globalSetup(config: FullConfig) {
  const api = await request.newContext({ baseURL: config.projects[0].use.baseURL })
  const send = async (query: string, variables: Record<string, unknown>) => {
    const body = (await (await api.post('/graphql', { data: { query, variables } })).json()) as { data?: Record<string, unknown>; errors?: { message: string }[] }
    if (body.errors?.length) throw new Error(`The e2e global setup failed: ${body.errors.map((e) => e.message).join('; ')}`)
    return body.data!
  }
  try {
    await send('mutation($i: LoginInput!) { login(input: $i) { id } }', { i: ADMIN })
    const users: Record<string, { email: string; password: string }> = {}
    for (const project of config.projects) {
      const id = crypto.randomUUID().slice(0, 8)
      const user = { email: `short-${id}@example.com`, password: `pw${id}` } // 10 characters: the floor
      const created = (await send('mutation($i: CreateUserInput!) { createUser(input: $i) { reset { token } } }', {
        i: { email: user.email, displayName: `Short ${id}`, isAdmin: false },
      })) as { createUser: { reset: { token: string } } }
      await send('mutation($i: ResetPasswordInput!) { resetPassword(input: $i) }', {
        i: { token: created.createUser.reset.token, newPassword: user.password, newPasswordConfirmation: user.password },
      })
      users[project.name] = user
    }
    await send('mutation($c: [SystemSettingChangeInput!]!) { updateSystemSettings(changes: $c) { restartPending } }', {
      c: [{ key: 'Auth:Standalone:PasswordMinLength', value: String(RAISED_MINIMUM) }],
    })
    process.env.E2E_SHORT_PASSWORDS = JSON.stringify(users)
  } finally {
    await api.dispose()
  }
}
