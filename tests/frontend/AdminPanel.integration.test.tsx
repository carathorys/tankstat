import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import App from '../../src/frontend/App.tsx'
import { server } from './server.ts'
import { fakeVehicleBackend, gqlError, healthHandler, renderWithApollo, sessionHandler, user } from './mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const users = [
  { id: 'u1', provider: 'LOCAL', email: 'alice@example.com', displayName: 'Alice', isAdmin: true, isDisabled: false, avatarUrl: null },
  { id: 'u2', provider: 'LOCAL', email: 'bob@example.com', displayName: 'Bob', isAdmin: false, isDisabled: false, avatarUrl: null },
  { id: 'u3', provider: 'OIDC', email: 'carol@example.com', displayName: 'Carol', isAdmin: false, isDisabled: false, avatarUrl: null },
]

function adminServer() {
  const calls: Record<string, unknown[]> = {}
  const record = (name: string, vars: unknown) => (calls[name] ??= []).push(vars)
  server.use(
    sessionHandler('STANDALONE', () => user({ isAdmin: true })),
    healthHandler,
    ...fakeVehicleBackend().handlers,
    graphql.query('Admin', () =>
      HttpResponse.json({
        data: {
          users,
          accessSettings: { defaultLevelForOthers: 'NONE' },
          accessGrants: [{ id: 'g1', ownerId: 'u2', granteeId: 'u1', level: 'VIEW' }],
        },
      }),
    ),
    graphql.mutation('SetUserAdmin', ({ variables }) => {
      record('SetUserAdmin', variables)
      return HttpResponse.json({ data: { setUserAdmin: { id: variables.userId } } })
    }),
    graphql.mutation('SetUserDisabled', ({ variables }) => {
      record('SetUserDisabled', variables)
      return HttpResponse.json({ data: { setUserDisabled: { id: variables.userId } } })
    }),
    graphql.mutation('SetDefaultAccess', ({ variables }) => {
      record('SetDefaultAccess', variables)
      return HttpResponse.json({ data: { setDefaultAccess: { defaultLevelForOthers: variables.level } } })
    }),
    graphql.mutation('SetAccessGrant', ({ variables }) => {
      record('SetAccessGrant', variables)
      return HttpResponse.json({ data: { setAccessGrant: true } })
    }),
    graphql.mutation('CreateUser', ({ variables }) => {
      record('CreateUser', variables)
      return HttpResponse.json({ data: { createUser: { user: { id: 'u9' }, reset: { token: 'tok.en', url: null, emailSent: false } } } })
    }),
    graphql.mutation('IssuePasswordReset', ({ variables }) => {
      record('IssuePasswordReset', variables)
      return HttpResponse.json({ data: { issuePasswordReset: { token: 'reset.tok', url: 'https://tank.test/?resetToken=reset.tok', emailSent: false } } })
    }),
  )
  return calls
}

/** Radix Select renders its options in a portal on the document body. */
const choose = async (ui: ReturnType<typeof userEvent.setup>, control: string, option: string) => {
  await ui.click(screen.getByRole('combobox', { name: control }))
  await ui.click(await screen.findByRole('option', { name: option }))
}

const panel = async () => within(await screen.findByRole('region', { name: 'Administration' }))

it('lists users and the access rules for administrators', async () => {
  const ui = userEvent.setup()
  adminServer()
  renderWithApollo(<App />, '/admin')

  const admin = await panel()
  await admin.findByText('bob@example.com')
  expect(admin.getByRole('checkbox', { name: 'Administrator: Alice' })).toBeChecked()
  expect(admin.getByRole('checkbox', { name: 'Administrator: Bob' })).not.toBeChecked()

  await ui.click(admin.getByRole('tab', { name: /Access/ }))
  expect(await admin.findByText(/Alice may view the data of Bob/)).toBeInTheDocument()
  expect(admin.getByRole('combobox', { name: 'Default access for everyone' })).toHaveTextContent('nothing')
})

it('promotes and disables users', async () => {
  const ui = userEvent.setup()
  const calls = adminServer()
  renderWithApollo(<App />, '/admin')
  const admin = await panel()

  await ui.click(await admin.findByRole('checkbox', { name: 'Administrator: Bob' }))
  await ui.click(admin.getByRole('checkbox', { name: 'Disabled: Bob' }))

  expect(calls.SetUserAdmin).toEqual([{ userId: 'u2', isAdmin: true }])
  expect(calls.SetUserDisabled).toEqual([{ userId: 'u2', disabled: true }])
})

it('changes the default access and manages grants', async () => {
  const ui = userEvent.setup()
  const calls = adminServer()
  renderWithApollo(<App />, '/admin')
  const admin = await panel()

  await ui.click(await admin.findByRole('tab', { name: /Access/ }))
  await choose(ui, 'Default access for everyone', 'view')
  await ui.click(admin.getByRole('button', { name: 'Revoke' }))
  await choose(ui, 'Grant to', 'Carol')
  await choose(ui, 'Data owner', 'Bob')
  await choose(ui, 'Level', 'view and edit')
  await ui.click(admin.getByRole('button', { name: 'Grant access' }))

  expect(calls.SetDefaultAccess).toEqual([{ level: 'VIEW' }])
  expect(calls.SetAccessGrant).toEqual([
    { input: { ownerId: 'u2', granteeId: 'u1', level: 'NONE' } },
    { input: { ownerId: 'u2', granteeId: 'u3', level: 'EDIT' } },
  ])
})

it('creates a user and shows the one-time setup link when no e-mail was sent', async () => {
  const ui = userEvent.setup()
  const calls = adminServer()
  renderWithApollo(<App />, '/admin')
  const admin = await panel()

  await ui.type(await admin.findByLabelText('E-mail'), 'dave@example.com')
  await ui.type(admin.getByLabelText('Name'), 'Dave')
  await ui.click(admin.getByRole('button', { name: 'Create user' }))

  expect(await admin.findByText(/Hand this one-time link/)).toBeInTheDocument()
  expect(admin.getByText(/resetToken=tok\.en/)).toBeInTheDocument()
  expect(calls.CreateUser).toEqual([{ input: { email: 'dave@example.com', displayName: 'Dave', isAdmin: false } }])
})

it('issues a reset link only for local users', async () => {
  const ui = userEvent.setup()
  const calls = adminServer()
  renderWithApollo(<App />, '/admin')
  const admin = await panel()

  await admin.findByText('carol@example.com')
  expect(admin.queryByRole('button', { name: 'Reset link for Carol' })).not.toBeInTheDocument() // OIDC user
  await ui.click(admin.getByRole('button', { name: 'Reset link for Bob' }))

  expect(await admin.findByText(/reset\.tok/)).toBeInTheDocument()
  expect(calls.IssuePasswordReset).toEqual([{ userId: 'u2' }])
})

it('shows the server error when an action is refused', async () => {
  const ui = userEvent.setup()
  adminServer()
  server.use(
    graphql.mutation('SetUserAdmin', () => HttpResponse.json(gqlError('You cannot remove your own administrator rights.', 'VALIDATION_FAILED'))),
  )
  renderWithApollo(<App />, '/admin')
  const admin = await panel()

  await ui.click(await admin.findByRole('checkbox', { name: 'Administrator: Alice' }))

  expect(await admin.findByRole('alert')).toHaveTextContent('cannot remove your own administrator rights')
})

it('offers the delete level for the default and for grants, and shows people with avatars', async () => {
  const ui = userEvent.setup()
  adminServer()
  renderWithApollo(<App />, '/admin')
  await screen.findByRole('heading', { name: 'Administration' })

  expect((await within(screen.getByRole('table')).findAllByText('B')).length).toBeGreaterThan(0) // Bob's initials (the avatar fallback appears right after mounting)
  await ui.click(screen.getByRole('tab', { name: /Access/ }))
  await ui.click(await screen.findByRole('combobox', { name: 'Level' }))

  expect(await screen.findByRole('option', { name: 'view, edit and delete permanently' })).toBeInTheDocument()
})
