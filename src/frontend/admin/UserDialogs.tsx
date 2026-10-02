import { Button, Dialog, Flex, Text } from '@radix-ui/themes'
import { useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { LabeledSelect } from '../components/UnitSelect.tsx'
import { FieldForm } from '../forms.tsx'
import type { UserDataDisposition } from '../gql/generated.ts'
import { ErrorMessage } from '../messages.tsx'

export interface PersonRef {
  id: string
  displayName: string
}

/** A dialog that opens from its `trigger` and closes once `children` calls the supplied `close`. */
function UserDialog({
  trigger,
  title,
  description,
  children,
}: {
  trigger: ReactNode
  title: string
  description: string
  children: (close: () => void) => ReactNode
}) {
  const [open, setOpen] = useState(false)
  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger>{trigger}</Dialog.Trigger>
      <Dialog.Content maxWidth="450px">
        <Dialog.Title>{title}</Dialog.Title>
        <Dialog.Description size="2" mb="4">
          {description}
        </Dialog.Description>
        {children(() => setOpen(false))}
      </Dialog.Content>
    </Dialog.Root>
  )
}

function CancelButton() {
  const { t } = useTranslation()
  return (
    <Dialog.Close>
      <Button type="button" variant="soft" color="gray">
        {t('common.cancel')}
      </Button>
    </Dialog.Close>
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
      {(close) => (
        <FieldForm
          fields={[
            { name: 'email', label: 'fields.email', type: 'email', defaultValue: user.email },
            { name: 'displayName', label: 'fields.name', required: false, defaultValue: user.displayName },
          ]}
          submitLabel="admin.save"
          onSubmit={async (v) => {
            await onSubmit({ email: v.email ?? '', displayName: v.displayName?.trim() || null })
            close()
          }}
        >
          <CancelButton />
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
      {(close) => (
        <FieldForm
          fields={[{ name: 'password', label: 'fields.newPassword', type: 'password', autoComplete: 'new-password' }]}
          submitLabel="admin.setPassword"
          onSubmit={async (v) => {
            await onSubmit(v.password ?? '')
            close()
          }}
        >
          <CancelButton />
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
      {(close) => (
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
      <Flex direction="column" gap="3">
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
          <Text as="p" size="2" color="gray">
            {t('admin.moveGrantsNote')}
          </Text>
        )}
        {choice === 'PURGE' && (
          <Text as="p" size="2" color="red">
            {t('admin.purgeWarning')}
          </Text>
        )}
        {error !== undefined && <ErrorMessage error={error} />}
        <Flex gap="3" justify="end">
          <CancelButton />
          <Button type="submit" color="red" disabled={busy || incomplete}>
            {t('admin.deleteConfirm')}
          </Button>
        </Flex>
      </Flex>
    </form>
  )
}
