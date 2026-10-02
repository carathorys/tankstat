import { useMutation, useQuery } from '@apollo/client/react'
import { Box, Button, Checkbox, Code, Flex, Heading, Select, Table, Tabs, Text, VisuallyHidden } from '@radix-ui/themes'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { UserChip } from '../components/UserAvatar.tsx'
import { FieldForm } from '../forms.tsx'
import {
  AdminDocument,
  CreateUserDocument,
  DeleteUserDocument,
  IssuePasswordResetDocument,
  SessionDocument,
  SetAccessGrantDocument,
  SetDefaultAccessDocument,
  SetUserAdminDocument,
  SetUserDisabledDocument,
  SetUserPasswordDocument,
  UpdateUserDocument,
  type AccessLevel,
  type IssuePasswordResetMutation,
} from '../gql/generated.ts'
import { ErrorMessage, SuccessMessage } from '../messages.tsx'
import { DeleteUserDialog, EditUserDialog, SetPasswordDialog } from './UserDialogs.tsx'

const refetch = { refetchQueries: ['Admin'], awaitRefetchQueries: true }

type ResetLink = IssuePasswordResetMutation['issuePasswordReset']

const linkFor = (reset: ResetLink) => reset.url ?? `${window.location.origin}/?resetToken=${reset.token}`

export function AdminPanel() {
  const { t } = useTranslation()
  const { data, error } = useQuery(AdminDocument, { fetchPolicy: 'cache-and-network' })
  const [createUser] = useMutation(CreateUserDocument, refetch)
  const [issueReset] = useMutation(IssuePasswordResetDocument)
  const [setAdmin] = useMutation(SetUserAdminDocument, refetch)
  const [setDisabled] = useMutation(SetUserDisabledDocument, refetch)
  const [updateUser] = useMutation(UpdateUserDocument, refetch)
  const [setPassword] = useMutation(SetUserPasswordDocument)
  const [deleteUser] = useMutation(DeleteUserDocument, refetch)
  const { data: session } = useQuery(SessionDocument)
  const selfId = session?.session.user?.id
  const [setDefault] = useMutation(SetDefaultAccessDocument, refetch)
  const [setGrant] = useMutation(SetAccessGrantDocument, refetch)
  const [reset, setReset] = useState<ResetLink>()
  const [actionError, setActionError] = useState<unknown>()

  if (error) return <ErrorMessage error={error} />
  if (!data) return <Text as="p">{t('admin.loading')}</Text>

  const name = (id: string) => data.users.find((u) => u.id === id)?.displayName ?? id
  const run = async (action: () => Promise<unknown>) => {
    setActionError(undefined)
    try {
      await action()
    } catch (e) {
      setActionError(e)
    }
  }

  return (
    <section aria-label={t('admin.title')}>
      <Heading mb="3">{t('admin.title')}</Heading>
      {actionError !== undefined && <ErrorMessage error={actionError} />}
      {reset && (
        <SuccessMessage>
          {reset.emailSent ? t('admin.linkEmailed') : t('admin.linkHandOver')} <Code>{linkFor(reset)}</Code>
        </SuccessMessage>
      )}

      <Tabs.Root defaultValue="users">
        <Tabs.List>
          <Tabs.Trigger value="users">{t('admin.users')}</Tabs.Trigger>
          <Tabs.Trigger value="access">{t('admin.access')}</Tabs.Trigger>
        </Tabs.List>

        <Box pt="4">
          <Tabs.Content value="users">
            <Box style={{ overflowX: 'auto' }}>
              <Table.Root variant="surface">
                <Table.Header>
                  <Table.Row>
                    <Table.ColumnHeaderCell>{t('admin.colName')}</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>{t('admin.colEmail')}</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>{t('admin.colAdmin')}</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>{t('admin.colDisabled')}</Table.ColumnHeaderCell>
                    <Table.ColumnHeaderCell>
                      <VisuallyHidden>{t('grid.actions')}</VisuallyHidden>
                    </Table.ColumnHeaderCell>
                  </Table.Row>
                </Table.Header>
                <Table.Body>
                  {data.users.map((u) => (
                    <Table.Row key={u.id} align="center">
                      <Table.RowHeaderCell>
                        <UserChip user={u} />
                      </Table.RowHeaderCell>
                      <Table.Cell>{u.email}</Table.Cell>
                      <Table.Cell>
                        <Checkbox
                          aria-label={t('admin.adminAria', { name: u.displayName })}
                          checked={u.isAdmin}
                          onCheckedChange={(checked) => run(() => setAdmin({ variables: { userId: u.id, isAdmin: checked === true } }))}
                        />
                      </Table.Cell>
                      <Table.Cell>
                        <Checkbox
                          aria-label={t('admin.disabledAria', { name: u.displayName })}
                          checked={u.isDisabled}
                          onCheckedChange={(checked) => run(() => setDisabled({ variables: { userId: u.id, disabled: checked === true } }))}
                        />
                      </Table.Cell>
                      <Table.Cell justify="end">
                        <Flex gap="2" justify="end" wrap="wrap">
                          {u.provider === 'LOCAL' && (
                            <EditUserDialog
                              user={u}
                              trigger={
                                <Button size="1" variant="soft">
                                  {t('admin.edit', { name: u.displayName })}
                                </Button>
                              }
                              onSubmit={(input) => updateUser({ variables: { input: { userId: u.id, ...input } } })}
                            />
                          )}
                          {u.provider === 'LOCAL' && (
                            <Button
                              size="1"
                              variant="soft"
                              onClick={() => run(async () => setReset((await issueReset({ variables: { userId: u.id } })).data?.issuePasswordReset))}
                            >
                              {t('admin.resetLink', { name: u.displayName })}
                            </Button>
                          )}
                          {u.provider === 'LOCAL' && data.canSetUserPasswords && (
                            <SetPasswordDialog
                              name={u.displayName}
                              trigger={
                                <Button size="1" variant="soft">
                                  {t('admin.setPasswordFor', { name: u.displayName })}
                                </Button>
                              }
                              onSubmit={(password) => setPassword({ variables: { userId: u.id, newPassword: password } })}
                            />
                          )}
                          {u.id !== selfId && u.provider === 'LOCAL' && (
                            <DeleteUserDialog
                              user={u}
                              others={data.users}
                              trigger={
                                <Button size="1" variant="soft" color="red">
                                  {t('admin.delete', { name: u.displayName })}
                                </Button>
                              }
                              onSubmit={(input) => deleteUser({ variables: { input: { userId: u.id, ...input } } })}
                            />
                          )}
                        </Flex>
                      </Table.Cell>
                    </Table.Row>
                  ))}
                </Table.Body>
              </Table.Root>
            </Box>

            <Heading as="h2" size="4" mb="3">
              {t('admin.addUser')}
            </Heading>
            <FieldForm
              fields={[
                { name: 'email', label: 'fields.email', type: 'email' },
                { name: 'displayName', label: 'fields.name', required: false },
              ]}
              submitLabel="admin.createUser"
              onSubmit={async (v) => {
                const created = await createUser({
                  variables: { input: { email: v.email ?? '', displayName: v.displayName || null, isAdmin: false } },
                })
                setReset(created.data?.createUser.reset)
              }}
            />
          </Tabs.Content>

          <Tabs.Content value="access">
            <Heading as="h2" size="4" mb="3">
              {t('admin.accessTitle')}
            </Heading>
            <Text as="p" size="2" color="gray" mb="3">
              {t('admin.levelsHelp')}
            </Text>
            <Flex align="center" gap="2" mb="4" wrap="wrap">
              <Text>{t('admin.defaultLabel')}</Text>
              <Select.Root
                value={data.accessSettings.defaultLevelForOthers}
                onValueChange={(level) => run(() => setDefault({ variables: { level: level as AccessLevel } }))}
              >
                <Select.Trigger aria-label={t('admin.defaultAria')} />
                <Select.Content>
                  {(['NONE', 'VIEW', 'EDIT', 'DELETE'] as const satisfies readonly AccessLevel[]).map((l) => (
                    <Select.Item key={l} value={l}>
                      {t(`level.${l}`)}
                    </Select.Item>
                  ))}
                </Select.Content>
              </Select.Root>
            </Flex>

            <Flex direction="column" gap="2" mb="4" asChild>
              <ul style={{ listStyle: 'none', padding: 0, margin: 0 }}>
                {data.accessGrants.map((g) => (
                  <li key={g.id}>
                    <Flex align="center" gap="3" wrap="wrap">
                      <Text>
                        {t(g.level === 'DELETE' ? 'admin.grantDelete' : g.level === 'EDIT' ? 'admin.grantEdit' : 'admin.grantView', { grantee: name(g.granteeId), owner: name(g.ownerId) })}
                      </Text>
                      <Button
                        size="1"
                        variant="soft"
                        color="red"
                        onClick={() => run(() => setGrant({ variables: { input: { ownerId: g.ownerId, granteeId: g.granteeId, level: 'NONE' } } }))}
                      >
                        {t('admin.revoke')}
                      </Button>
                    </Flex>
                  </li>
                ))}
              </ul>
            </Flex>

            <GrantForm users={data.users} onGrant={(input) => run(() => setGrant({ variables: { input } }))} />
          </Tabs.Content>
        </Box>
      </Tabs.Root>
    </section>
  )
}

function PersonSelect({
  label,
  value,
  onValueChange,
  users,
}: {
  label: string
  value: string
  onValueChange: (id: string) => void
  users: { id: string; displayName: string }[]
}) {
  const { t } = useTranslation()

  return (
    <Select.Root value={value || undefined} onValueChange={onValueChange}>
      <Select.Trigger aria-label={label} placeholder={t('admin.choose')} />
      <Select.Content>
        {users.map((u) => (
          <Select.Item key={u.id} value={u.id}>
            {u.displayName}
          </Select.Item>
        ))}
      </Select.Content>
    </Select.Root>
  )
}

function GrantForm({
  users,
  onGrant,
}: {
  users: { id: string; displayName: string }[]
  onGrant: (input: { ownerId: string; granteeId: string; level: AccessLevel }) => void
}) {
  const { t } = useTranslation()
  const [ownerId, setOwnerId] = useState('')
  const [granteeId, setGranteeId] = useState('')
  const [level, setLevel] = useState<AccessLevel>('VIEW')

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault()
        if (ownerId && granteeId) onGrant({ ownerId, granteeId, level })
      }}
    >
      <Flex align="center" gap="2" wrap="wrap">
        <Text>{t('admin.grantTo')}</Text>
        <PersonSelect label={t('admin.grantTo')} value={granteeId} onValueChange={setGranteeId} users={users} />
        <Text>{t('admin.accessTo')}</Text>
        <PersonSelect label={t('admin.dataOwner')} value={ownerId} onValueChange={setOwnerId} users={users} />
        <Text>{t('admin.atLevel')}</Text>
        <Select.Root value={level} onValueChange={(v) => setLevel(v as AccessLevel)}>
          <Select.Trigger aria-label={t('admin.level')} />
          <Select.Content>
            <Select.Item value="VIEW">{t('level.VIEW')}</Select.Item>
            <Select.Item value="EDIT">{t('level.EDIT')}</Select.Item>
            <Select.Item value="DELETE">{t('level.DELETE')}</Select.Item>
          </Select.Content>
        </Select.Root>
        <Button type="submit">{t('admin.grantAction')}</Button>
      </Flex>
    </form>
  )
}
