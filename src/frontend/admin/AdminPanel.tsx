import { useMutation, useQuery } from '@apollo/client/react'
import { useState } from 'react'
import { Form } from '../forms.tsx'
import { Checkbox } from '../ui/Checkbox.tsx'
import { Select } from '../ui/Select.tsx'
import { Tabs } from '../ui/Tabs.tsx'
import {
  ADMIN_QUERY,
  CREATE_USER_MUTATION,
  ISSUE_RESET_MUTATION,
  SET_ADMIN_MUTATION,
  SET_DEFAULT_ACCESS_MUTATION,
  SET_DISABLED_MUTATION,
  SET_GRANT_MUTATION,
  type AccessLevel,
  type ResetLink,
} from './admin.ts'

const refetch = { refetchQueries: [ADMIN_QUERY], awaitRefetchQueries: true }

function linkFor(reset: ResetLink) {
  return reset.url ?? `${window.location.origin}/?resetToken=${reset.token}`
}

function ResetLinkNotice({ reset }: { reset: ResetLink }) {
  return (
    <p role="status">
      {reset.emailSent ? 'The link was e-mailed. ' : 'Hand this one-time link to the user: '}
      <code>{linkFor(reset)}</code>
    </p>
  )
}

export function AdminPanel() {
  const { data, error } = useQuery(ADMIN_QUERY)
  const [createUser] = useMutation(CREATE_USER_MUTATION, refetch)
  const [issueReset] = useMutation(ISSUE_RESET_MUTATION)
  const [setAdmin] = useMutation(SET_ADMIN_MUTATION, refetch)
  const [setDisabled] = useMutation(SET_DISABLED_MUTATION, refetch)
  const [setDefault] = useMutation(SET_DEFAULT_ACCESS_MUTATION, refetch)
  const [setGrant] = useMutation(SET_GRANT_MUTATION, refetch)
  const [reset, setReset] = useState<ResetLink>()
  const [actionError, setActionError] = useState<string>()

  if (error) return <p role="alert">{error.message}</p>
  if (!data) return <p>Loading administration…</p>

  const name = (id: string) => data.users.find((u) => u.id === id)?.displayName ?? id
  const run = async (action: () => Promise<unknown>) => {
    setActionError(undefined)
    try {
      await action()
    } catch (e) {
      setActionError(e instanceof Error ? e.message : String(e))
    }
  }

  return (
    <section aria-label="Administration">
      <h1>Administration</h1>
      {actionError && <p role="alert">{actionError}</p>}
      {reset && <ResetLinkNotice reset={reset} />}

      <Tabs
        defaultValue="users"
        tabs={[
          {
            value: 'users',
            label: 'Users',
            content: (
              <>
                <h3>Users</h3>
                <table>
                  <thead>
                    <tr>
                      <th>Name</th>
                      <th>E-mail</th>
                      <th>Admin</th>
                      <th>Disabled</th>
                      <th />
                    </tr>
                  </thead>
                  <tbody>
                    {data.users.map((u) => (
                      <tr key={u.id}>
                        <td>{u.displayName}</td>
                        <td>{u.email}</td>
                        <td>
                          <Checkbox
                            label={`Administrator: ${u.displayName}`}
                            checked={u.isAdmin}
                            onCheckedChange={(checked) => run(() => setAdmin({ variables: { userId: u.id, isAdmin: checked } }))}
                          />
                        </td>
                        <td>
                          <Checkbox
                            label={`Disabled: ${u.displayName}`}
                            checked={u.isDisabled}
                            onCheckedChange={(checked) => run(() => setDisabled({ variables: { userId: u.id, disabled: checked } }))}
                          />
                        </td>
                        <td>
                          {u.provider === 'LOCAL' && (
                            <button
                              type="button"
                              onClick={() => run(async () => setReset((await issueReset({ variables: { userId: u.id } })).data?.issuePasswordReset))}
                            >
                              Reset link for {u.displayName}
                            </button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>

                <h3>Add user</h3>
                <Form
                  fields={[
                    { name: 'email', label: 'E-mail', type: 'email' },
                    { name: 'displayName', label: 'Name' },
                  ]}
                  submitLabel="Create user"
                  onSubmit={async (v) => {
                    const created = await createUser({
                      variables: { input: { email: v.email ?? '', displayName: v.displayName || null, isAdmin: false } },
                    })
                    setReset(created.data?.createUser.reset)
                  }}
                />

              </>
            ),
          },
          {
            value: 'access',
            label: 'Access',
            content: (
              <>
                <h3>Access to other people&apos;s data</h3>
                <p>
                  Everyone may, by default:{' '}
                  <Select
                    label="Default access for everyone"
                    value={data.accessSettings.defaultLevelForOthers}
                    onValueChange={(level) => run(() => setDefault({ variables: { level: level as AccessLevel } }))}
                    options={[
                      { value: 'NONE', label: 'nothing' },
                      { value: 'VIEW', label: 'view' },
                      { value: 'EDIT', label: 'view and edit' },
                    ]}
                  />
                </p>
                <ul>
                  {data.accessGrants.map((g) => (
                    <li key={g.id}>
                      {name(g.granteeId)} may {g.level === 'EDIT' ? 'view and edit' : 'view'} the data of {name(g.ownerId)}{' '}
                      <button
                        type="button"
                        onClick={() => run(() => setGrant({ variables: { input: { ownerId: g.ownerId, granteeId: g.granteeId, level: 'NONE' } } }))}
                      >
                        Revoke
                      </button>
                    </li>
                  ))}
                </ul>
                <GrantForm users={data.users} onGrant={(input) => run(() => setGrant({ variables: { input } }))} />
              </>
            ),
          },
        ]}
      />
    </section>
  )
}

function GrantForm({
  users,
  onGrant,
}: {
  users: { id: string; displayName: string }[]
  onGrant: (input: { ownerId: string; granteeId: string; level: AccessLevel }) => void
}) {
  const [ownerId, setOwnerId] = useState('')
  const [granteeId, setGranteeId] = useState('')
  const [level, setLevel] = useState<AccessLevel>('VIEW')
  const people = users.map((u) => ({ value: u.id, label: u.displayName }))

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault()
        if (ownerId && granteeId) onGrant({ ownerId, granteeId, level })
      }}
    >
      Grant to <Select label="Grant to" value={granteeId} onValueChange={setGranteeId} options={people} /> access to the
      data of <Select label="Data owner" value={ownerId} onValueChange={setOwnerId} options={people} /> at level{' '}
      <Select
        label="Level"
        value={level}
        onValueChange={(v) => setLevel(v as AccessLevel)}
        options={[
          { value: 'VIEW', label: 'view' },
          { value: 'EDIT', label: 'view and edit' },
        ]}
      />{' '}
      <button type="submit">Grant access</button>
    </form>
  )
}
