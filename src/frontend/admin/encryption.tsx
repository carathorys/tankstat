import { useMutation } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import FormControlLabel from '@mui/material/FormControlLabel'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import type { DocumentNode } from 'graphql'
import { KeyRound } from 'lucide-react'
import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { DefinitionList } from '../components/DefinitionList.tsx'
import { DialogButtons, DialogCancel, DialogFrame } from '../dialogs/DialogFrame.tsx'
import { DialogTrigger } from '../dialogs/DialogTrigger.tsx'
import { useDialogState } from '../dialogs/useDialogState.ts'
import { Field } from '../forms/Field.tsx'
import { FieldInput } from '../forms/FieldInput.tsx'
import { Form } from '../forms/Form.tsx'
import { PrepareBackupEncryptionDocument, SystemSettingsDocument, UpdateSystemSettingsDocument, type PrepareBackupEncryptionMutation } from '../gql/generated.ts'
import { ErrorMessage, SuccessMessage, WarningMessage } from '../messages.tsx'
import { ProblemList } from './ProblemList.tsx'
import { shortRecipient } from './recipient.ts'
import { problemsOf } from './settingsText.ts'

type KeySet = PrepareBackupEncryptionMutation['prepareBackupEncryption']

export const Code = ({ children }: { children: ReactNode }) => (
  <Box component="code" sx={(theme) => ({ fontFamily: 'monospace', fontSize: '0.9em', px: 0.5, borderRadius: '4px', backgroundColor: theme.vars.palette.neutral.soft, wordBreak: 'break-all' })}>
    {children}
  </Box>
)

/**
 * A key set for encrypted backups: made on the server from a passphrase (or one made up), shown once, then set as two settings. On the
 * Backups page and on the setup's done screen, for the administrator the setup just made; `refresh` is the query of the page that shows
 * the recipient, asked again once the keys are in use.
 */
export function EncryptionSetup({ recipient, refresh }: { recipient: string | null; refresh: DocumentNode }) {
  const { t } = useTranslation()
  const [open, setOpen] = useDialogState()
  // Escape and the backdrop do not close it while the keys are made, nor while they are shown and not yet kept: they are shown only once.
  const [locked, setLocked] = useState(false)
  return (
    <section aria-labelledby="backups-encryption">
      <Typography id="backups-encryption" component="h2" variant="h5" sx={{ mb: 1 }}>
        {t('admin.backups.encryptionTitle')}
      </Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary', mb: 1.5 }}>
        {recipient ? t('admin.backups.encryptionOn', { recipient: shortRecipient(recipient) }) : t('admin.backups.encryptionOff')}
      </Typography>
      <DialogTrigger
        trigger={
          <Button variant="soft">
            <KeyRound size={16} aria-hidden />
            {t('admin.backups.makeKeys')}
          </Button>
        }
        open={open}
        onOpen={() => setOpen(true)}
      />
      <DialogFrame
        open={open}
        onClose={() => {
          setLocked(false)
          setOpen(false)
        }}
        busy={locked}
        maxWidth={560}
        title={t('admin.backups.keysTitle')}
        description={t('admin.backups.keysDescription')}
      >
        <KeySetForm replacing={recipient !== null} refresh={refresh} onLock={setLocked} />
      </DialogFrame>
    </section>
  )
}

function KeySetForm({ replacing, refresh, onLock }: { replacing: boolean; refresh: DocumentNode; onLock: (locked: boolean) => void }) {
  const { t } = useTranslation()
  const [prepare] = useMutation(PrepareBackupEncryptionDocument, { fetchPolicy: 'no-cache' })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>()
  const [keys, setKeys] = useState<KeySet>()

  if (keys) return <KeySetShown keys={keys} refresh={refresh} onLock={onLock} />

  const submit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const passphrase = String(new FormData(e.currentTarget).get('passphrase') ?? '')
    setBusy(true)
    onLock(true)
    setError(undefined)
    try {
      const made = await prepare({ variables: { passphrase: passphrase === '' ? null : passphrase } })
      if (made.data) setKeys(made.data.prepareBackupEncryption) // shown, and the dialog stays until they are kept
      else onLock(false)
    } catch (err) {
      setError(err)
      onLock(false)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Form onSubmit={(e) => void submit(e)} refusal={error}>
      <Stack sx={{ gap: 1.5 }}>
        {replacing && <WarningMessage>{t('admin.backups.keysReplacing')}</WarningMessage>}
        <Field name="passphrase" label={t('admin.backups.newPassphrase')} hint={t('admin.backups.newPassphraseHint')}>
          <FieldInput type="password" autoComplete="new-password" />
        </Field>
        {error !== undefined && <ErrorMessage error={error} />}
        <DialogButtons>
          <DialogCancel disabled={busy} />
          <Button type="submit" loading={busy}>
            {t('admin.backups.makeKeysConfirm')}
          </Button>
        </DialogButtons>
      </Stack>
    </Form>
  )
}

function KeySetShown({ keys, refresh, onLock }: { keys: KeySet; refresh: DocumentNode; onLock: (locked: boolean) => void }) {
  const { t } = useTranslation()
  const [saved, setSaved] = useState(false)
  // The settings page's own save: the answer is that page as the server now has it, and the page here says which key it encrypts for.
  const [update, { loading: using, data: used, error: refused }] = useMutation(UpdateSystemSettingsDocument, {
    update: (cache, { data }) => {
      if (data) cache.writeQuery({ query: SystemSettingsDocument, data: { systemSettings: data.updateSystemSettings } })
    },
    refetchQueries: [refresh],
  })
  const use = () =>
    update({
      variables: {
        changes: [
          { key: 'Backups:Encryption:Recipient', value: keys.recipient },
          { key: 'Backups:Encryption:Salt', value: keys.salt },
        ],
      },
    }).catch(() => undefined) // the refusal is shown below
  // Kept open until the passphrase is kept, and while the keys are being saved: closed then, a refusal would go unseen.
  useEffect(() => onLock(!saved || using), [saved, using, onLock])
  // The button that had the focus is gone once the keys are in use: the focus goes to what took its place, which also has it read out.
  const inUse = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (used) inUse.current?.focus()
  }, [used])
  // A save the server refused as a whole names the settings at fault (another one of the file, as a rule), as the settings page lists them.
  const problems = problemsOf(refused)
  return (
    <Stack sx={{ gap: 1.5 }}>
      <SuccessMessage>{t('admin.backups.keysMade')}</SuccessMessage>
      <WarningMessage>{t('admin.backups.keysOnce')}</WarningMessage>
      <DefinitionList
        items={[
          { label: keys.generated ? t('admin.backups.madeUpPassphrase') : t('admin.backups.passphrase'), value: <Code>{keys.passphrase}</Code> },
          { label: t('admin.backups.identity'), value: <Code>{keys.identity}</Code> },
        ]}
      />
      {used ? (
        <Box ref={inUse} tabIndex={-1}>
          <SuccessMessage>{t('admin.backups.keysInUse')}</SuccessMessage>
        </Box>
      ) : (
        <Button variant="soft" onClick={() => void use()} loading={using} sx={{ alignSelf: 'flex-start' }}>
          <KeyRound size={16} aria-hidden />
          {t('admin.backups.keysUse')}
        </Button>
      )}
      {refused &&
        (problems.length > 0 ? (
          <ErrorMessage error={refused}>
            {t('systemSettings.problems')}
            <ProblemList problems={problems} />
          </ErrorMessage>
        ) : (
          <ErrorMessage error={refused} />
        ))}
      <Typography variant="body2">{t('admin.backups.keysSettings')}</Typography>
      <Box component="pre" sx={(theme) => ({ m: 0, p: 1.5, borderRadius: '8px', backgroundColor: theme.vars.palette.neutral.soft, whiteSpace: 'pre-wrap', wordBreak: 'break-all', fontSize: '0.85em' })}>
        {`Backups__Encryption__Recipient=${keys.recipient}\nBackups__Encryption__Salt=${keys.salt}`}
      </Box>
      <FormControlLabel control={<Checkbox checked={saved} onChange={(e) => setSaved(e.target.checked)} />} label={t('admin.backups.keysSaved')} />
      <DialogButtons>
        <DialogCancel label={t('admin.backups.keysDone')} disabled={!saved || using} />
      </DialogButtons>
    </Stack>
  )
}
