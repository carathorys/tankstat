import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { LabeledSelect } from '../components/UnitSelect.tsx'
import { DialogButtons, DialogCancel, DialogFrame } from '../dialogs/DialogFrame.tsx'
import { DialogTrigger } from '../dialogs/DialogTrigger.tsx'
import { useDialogState } from '../dialogs/useDialogState.ts'
import { FieldForm } from '../forms/FieldForm.tsx'
import type { UserDataDisposition } from '../gql/generated.ts'
import { ErrorMessage } from '../messages.tsx'
import { useToast } from '../toast/toastContext.ts'

export interface PersonRef {
  id: string
  displayName: string
}

/** A dialog that opens from its `trigger` and closes once `children` calls `close`, or `saved` (which also says "Saved."). */
function UserDialog({
  trigger,
  title,
  description,
  children,
}: {
  trigger: ReactNode
  title: string
  description: string
  children: (done: { close: () => void; saved: () => void }) => ReactNode
}) {
  const [open, setOpen] = useDialogState()
  const { toast } = useToast()
  const { t } = useTranslation()
  const close = () => setOpen(false)
  const saved = () => {
    close()
    toast(t('toast.saved'))
  }
  return (
    <>
      <DialogTrigger trigger={trigger} open={open} onOpen={() => setOpen(true)} />
      <DialogFrame open={open} onClose={close} title={title} description={description}>
        {children({ close, saved })}
      </DialogFrame>
    </>
  )
}

/** Name and e-mail of a local user. Users from an external provider are not edited here (the provider owns those values). */
export function EditUserDialog({
  trigger,
  user,
  onSubmit,
}: {
  trigger: ReactNode
  user: { displayName: string; email: string }
  onSubmit: (values: { email: string; displayName: string | null }) => Promise<unknown>
}) {
  const { t } = useTranslation()
  return (
    <UserDialog trigger={trigger} title={t('admin.editTitle', { name: user.displayName })} description={t('admin.editDescription')}>
      {({ saved }) => (
        <FieldForm
          fields={[
            { name: 'email', label: 'fields.email', type: 'email', defaultValue: user.email },
            { name: 'displayName', label: 'fields.name', required: false, defaultValue: user.displayName },
          ]}
          submitLabel="admin.save"
          onSubmit={async (v) => {
            await onSubmit({ email: v.email ?? '', displayName: v.displayName?.trim() || null })
            saved()
          }}
        >
          <DialogCancel />
        </FieldForm>
      )}
    </UserDialog>
  )
}

/** Sets a local user's password directly (only offered when the instance allows it); they are signed out everywhere. */
export function SetPasswordDialog({
  trigger,
  name,
  onSubmit,
}: {
  trigger: ReactNode
  name: string
  onSubmit: (password: string) => Promise<unknown>
}) {
  const { t } = useTranslation()
  return (
    <UserDialog trigger={trigger} title={t('admin.passwordTitle', { name })} description={t('admin.passwordDescription')}>
      {({ saved }) => (
        <FieldForm
          fields={[{ name: 'password', label: 'fields.newPassword', type: 'password', autoComplete: 'new-password' }]}
          submitLabel="admin.setPassword"
          onSubmit={async (v) => {
            await onSubmit(v.password ?? '')
            saved()
          }}
        >
          <DialogCancel />
        </FieldForm>
      )}
    </UserDialog>
  )
}

type DataChoice = '' | 'MOVE' | 'PURGE'

/** Deleting a user: the admin says what happens to the data they own (move it to someone else, or delete it for good). */
export function DeleteUserDialog({
  trigger,
  user,
  others,
  onSubmit,
}: {
  trigger: ReactNode
  user: PersonRef
  others: PersonRef[]
  onSubmit: (input: { data: UserDataDisposition | null; moveToUserId: string | null }) => Promise<unknown>
}) {
  const { t } = useTranslation()
  return (
    <UserDialog trigger={trigger} title={t('admin.deleteTitle', { name: user.displayName })} description={t('admin.deleteDescription')}>
      {({ close }) => (
        <DeleteForm
          others={others.filter((o) => o.id !== user.id)}
          onSubmit={async (input) => {
            await onSubmit(input)
            close()
          }}
        />
      )}
    </UserDialog>
  )
}

function DeleteForm({
  others,
  onSubmit,
}: {
  others: PersonRef[]
  onSubmit: (input: { data: UserDataDisposition | null; moveToUserId: string | null }) => Promise<unknown>
}) {
  const { t } = useTranslation()
  const [choice, setChoice] = useState<DataChoice>('')
  const [target, setTarget] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>()
  const incomplete = choice === 'MOVE' && target === ''

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (incomplete) return
    setBusy(true)
    setError(undefined)
    try {
      await onSubmit({ data: choice === '' ? null : choice, moveToUserId: choice === 'MOVE' ? target : null })
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit}>
      <Stack sx={{ gap: 1.5 }}>
        <LabeledSelect<DataChoice>
          label={t('admin.dataChoice')}
          value={choice}
          placeholder={t('admin.dataChoiceNone')}
          onChange={setChoice}
          options={[
            { value: 'MOVE', label: t('admin.dataMove') },
            { value: 'PURGE', label: t('admin.dataPurge') },
          ]}
        />
        {choice === 'MOVE' && (
          <LabeledSelect
            label={t('admin.moveTo')}
            value={target}
            placeholder={t('admin.choose')}
            onChange={setTarget}
            options={others.map((o) => ({ value: o.id, label: o.displayName }))}
          />
        )}
        {choice === 'MOVE' && (
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            {t('admin.moveGrantsNote')}
          </Typography>
        )}
        {choice === 'PURGE' && (
          <Typography variant="body2" sx={{ color: 'error.softText' }}>
            {t('admin.purgeWarning')}
          </Typography>
        )}
        {error !== undefined && <ErrorMessage error={error} />}
        <DialogButtons>
          <DialogCancel />
          <Button type="submit" color="error" disabled={busy || incomplete}>
            {t('admin.deleteConfirm')}
          </Button>
        </DialogButtons>
      </Stack>
    </form>
  )
}
