import { useMutation, useQuery } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import Stack from '@mui/material/Stack'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { SurfaceTable } from '../components/SurfaceTable.tsx'
import { TabbedPanels } from '../components/TabbedPanels.tsx'
import { InlineSelect } from '../components/UnitSelect.tsx'
import { UserChip } from '../components/UserAvatar.tsx'
import { visuallyHidden } from '../components/visuallyHidden.ts'
import { FieldForm } from '../forms/FieldForm.tsx'
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
  const [tab, setTab] = useState<'users' | 'access'>('users')

  if (error) return <ErrorMessage error={error} />
  if (!data) return <Typography>{t('admin.loading')}</Typography>

  const name = (id: string) => data.users.find((u) => u.id === id)?.displayName ?? id
  const run = async (action: () => Promise<unknown>) => {
    setActionError(undefined)
    try {
      await action()
    } catch (e) {
      setActionError(e)
    }
  }

  const users = (
    <>
      <Box sx={{ mb: 2 }}>
        <SurfaceTable>
          <TableHead>
            <TableRow>
              <TableCell>{t('admin.colName')}</TableCell>
              <TableCell>{t('admin.colEmail')}</TableCell>
              <TableCell>{t('admin.colAdmin')}</TableCell>
              <TableCell>{t('admin.colDisabled')}</TableCell>
              <TableCell>
                <span style={visuallyHidden}>{t('grid.actions')}</span>
              </TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {data.users.map((u) => (
              <TableRow key={u.id}>
                <TableCell component="th" scope="row">
                  <UserChip user={u} />
                </TableCell>
                <TableCell>{u.email}</TableCell>
                <TableCell>
                  <Checkbox
                    slotProps={{ input: { 'aria-label': t('admin.adminAria', { name: u.displayName }) } }}
                    checked={u.isAdmin}
                    onChange={(e) => run(() => setAdmin({ variables: { userId: u.id, isAdmin: e.target.checked } }))}
                  />
                </TableCell>
                <TableCell>
                  <Checkbox
                    slotProps={{ input: { 'aria-label': t('admin.disabledAria', { name: u.displayName }) } }}
                    checked={u.isDisabled}
                    onChange={(e) => run(() => setDisabled({ variables: { userId: u.id, disabled: e.target.checked } }))}
                  />
                </TableCell>
                <TableCell align="right">
                  <Stack direction="row" sx={{ gap: 1, justifyContent: 'flex-end', flexWrap: 'wrap' }}>
                    {u.provider === 'LOCAL' && (
                      <EditUserDialog
                        user={u}
                        trigger={
                          <Button size="small" variant="soft">
                            {t('admin.edit', { name: u.displayName })}
                          </Button>
                        }
                        onSubmit={(input) => updateUser({ variables: { input: { userId: u.id, ...input } } })}
                      />
                    )}
                    {u.provider === 'LOCAL' && (
                      <Button size="small" variant="soft" onClick={() => run(async () => setReset((await issueReset({ variables: { userId: u.id } })).data?.issuePasswordReset))}>
                        {t('admin.resetLink', { name: u.displayName })}
                      </Button>
                    )}
                    {u.provider === 'LOCAL' && data.canSetUserPasswords && (
                      <SetPasswordDialog
                        name={u.displayName}
                        trigger={
                          <Button size="small" variant="soft">
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
                          <Button size="small" variant="soft" color="error">
                            {t('admin.delete', { name: u.displayName })}
                          </Button>
                        }
                        onSubmit={(input) => deleteUser({ variables: { input: { userId: u.id, ...input } } })}
                      />
                    )}
                  </Stack>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </SurfaceTable>
      </Box>

      <Typography component="h2" variant="h5" sx={{ mb: 1.5 }}>
        {t('admin.addUser')}
      </Typography>
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
    </>
  )

  const access = (
    <>
      <Typography component="h2" variant="h5" sx={{ mb: 1.5 }}>
        {t('admin.accessTitle')}
      </Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary', mb: 1.5 }}>
        {t('admin.levelsHelp')}
      </Typography>
      <Stack direction="row" sx={{ alignItems: 'center', gap: 1, mb: 2, flexWrap: 'wrap' }}>
        <Typography>{t('admin.defaultLabel')}</Typography>
        <InlineSelect<AccessLevel>
          label={t('admin.defaultAria')}
          value={data.accessSettings.defaultLevelForOthers}
          onChange={(level) => run(() => setDefault({ variables: { level } }))}
          options={(['NONE', 'VIEW', 'EDIT', 'DELETE'] as const).map((l) => ({ value: l, label: t(`level.${l}`) }))}
        />
      </Stack>

      <Stack component="ul" sx={{ gap: 1, mb: 2, listStyle: 'none', p: 0, mt: 0 }}>
        {data.accessGrants.map((g) => (
          <li key={g.id}>
            <Stack direction="row" sx={{ alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
              <Typography>
                {t(g.level === 'DELETE' ? 'admin.grantDelete' : g.level === 'EDIT' ? 'admin.grantEdit' : 'admin.grantView', { grantee: name(g.granteeId), owner: name(g.ownerId) })}
              </Typography>
              <Button size="small" variant="soft" color="error" onClick={() => run(() => setGrant({ variables: { input: { ownerId: g.ownerId, granteeId: g.granteeId, level: 'NONE' } } }))}>
                {t('admin.revoke')}
              </Button>
            </Stack>
          </li>
        ))}
      </Stack>

      <GrantForm users={data.users} onGrant={(input) => run(() => setGrant({ variables: { input } }))} />
    </>
  )

  return (
    <section aria-label={t('admin.title')}>
      <Typography component="h1" variant="h3" sx={{ mb: 1.5 }}>
        {t('admin.title')}
      </Typography>
      {actionError !== undefined && <ErrorMessage error={actionError} />}
      {reset && (
        <SuccessMessage>
          {reset.emailSent ? t('admin.linkEmailed') : t('admin.linkHandOver')}{' '}
          <Box component="code" sx={(theme) => ({ fontFamily: 'monospace', fontSize: '0.9em', px: 0.5, borderRadius: '4px', backgroundColor: theme.vars.palette.primary.soft, wordBreak: 'break-all' })}>
            {linkFor(reset)}
          </Box>
        </SuccessMessage>
      )}
      <TabbedPanels
        label={t('admin.title')}
        value={tab}
        onChange={setTab}
        tabs={[
          { value: 'users', label: t('admin.users'), content: () => users },
          { value: 'access', label: t('admin.access'), content: () => access },
        ]}
      />
    </section>
  )
}

function PersonSelect({ label, value, onValueChange, users }: { label: string; value: string; onValueChange: (id: string) => void; users: { id: string; displayName: string }[] }) {
  const { t } = useTranslation()
  return <InlineSelect label={label} value={value} onChange={onValueChange} placeholder={t('admin.choose')} options={users.map((u) => ({ value: u.id, label: u.displayName }))} />
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
      <Stack direction="row" sx={{ alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
        <Typography>{t('admin.grantTo')}</Typography>
        <PersonSelect label={t('admin.grantTo')} value={granteeId} onValueChange={setGranteeId} users={users} />
        <Typography>{t('admin.accessTo')}</Typography>
        <PersonSelect label={t('admin.dataOwner')} value={ownerId} onValueChange={setOwnerId} users={users} />
        <Typography>{t('admin.atLevel')}</Typography>
        <InlineSelect<AccessLevel>
          label={t('admin.level')}
          value={level}
          onChange={setLevel}
          options={(['VIEW', 'EDIT', 'DELETE'] as const).map((l) => ({ value: l, label: t(`level.${l}`) }))}
        />
        <Button type="submit">{t('admin.grantAction')}</Button>
      </Stack>
    </form>
  )
}
