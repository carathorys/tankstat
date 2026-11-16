import { useApolloClient, useMutation } from '@apollo/client/react'
import Button from '@mui/material/Button'
import LinearProgress from '@mui/material/LinearProgress'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { FileUp } from 'lucide-react'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Field, FieldMessage } from '../../forms/Field.tsx'
import { Form } from '../../forms/Form.tsx'
import { PasswordInput } from '../../forms/PasswordInput.tsx'
import { BackupImportDocument, EndBackupImportDocument, OpenBackupImportDocument, type BackupImportFieldsFragment } from '../../gql/generated.ts'
import { useErrorText } from '../../i18n/errors.ts'
import { useFormat } from '../../i18n/format.ts'
import { ErrorMessage, SuccessMessage } from '../../messages.tsx'
import { ApiError } from '../../pictures/ApiError.ts'
import { uploadBackupImport } from '../../pictures/uploadFile.ts'

/** How often the server is asked while it opens the file. */
export const OPENING_POLL_MS = 1000

type Opened = NonNullable<BackupImportFieldsFragment['file']>

/**
 * The first step of importing cars from a Tankstat backup or export (#336): the file, its passphrase when it is encrypted (three
 * tries, then the server throws the file away), and what it holds once opened. #337 puts it on the Import page as a source and builds
 * the steps after it; until then nothing offers it.
 *
 * The server opens the file in the background (a large one takes longer than a request may), so while it does the step asks how it
 * stands. Leaving the step gives the file up on the server.
 */
export function BackupFileStep({ onOpened, pollMs = OPENING_POLL_MS }: { onOpened: (token: string, file: Opened) => void; pollMs?: number }) {
  const { t } = useTranslation()
  const { date } = useFormat()
  const errorText = useErrorText()
  const client = useApolloClient()
  const input = useRef<HTMLInputElement>(null)
  const root = useRef<HTMLDivElement>(null)
  const [file, setFile] = useState<File>()
  const [share, setShare] = useState<number>() // of the upload; undefined while the server opens the file
  const [busy, setBusy] = useState(false)
  const [status, setStatus] = useState<BackupImportFieldsFragment>()
  const [error, setError] = useState<unknown>()
  const [open] = useMutation(OpenBackupImportDocument)
  const token = useRef<string | undefined>(undefined)
  const done = useRef(false)

  // Leaving with a file still on the server (not handed on to the next step): it is given up there.
  useEffect(
    () => () => {
      if (token.current && !done.current) void client.mutate({ mutation: EndBackupImportDocument, variables: { token: token.current } }).catch(() => undefined)
    },
    [client],
  )

  // The passphrase field takes the focus whenever it is asked for: first, and again after a wrong one (once it can be typed into).
  const asking = status?.state === 'NEEDS_KEY' && !busy
  const tries = status?.triesLeft
  useEffect(() => {
    if (asking) root.current?.querySelector<HTMLInputElement>('input[name="passphrase"]')?.focus()
  }, [asking, tries])

  /** Asks until the server is done opening; what it says then is shown. */
  async function settle(first: BackupImportFieldsFragment) {
    let now = first
    while (now.state === 'OPENING') {
      await new Promise((resolve) => setTimeout(resolve, pollMs))
      const { data } = await client.query({ query: BackupImportDocument, variables: { token: now.token }, fetchPolicy: 'network-only' })
      if (!data) throw new Error('No answer.')
      now = data.backupImport
    }
    if (now.state === 'ENDED') {
      // The file is gone from the server: the field is empty again, to choose it (or another) anew.
      token.current = undefined
      setFile(undefined)
      if (input.current) input.current.value = ''
    }
    setStatus(now)
  }

  async function read(e: FormEvent) {
    e.preventDefault()
    if (!file) return
    setBusy(true)
    setError(undefined)
    setStatus(undefined)
    setShare(0)
    try {
      const uploaded = await uploadBackupImport(file, setShare)
      token.current = uploaded.token
      setShare(undefined)
      const { data } = await client.query({ query: BackupImportDocument, variables: { token: uploaded.token }, fetchPolicy: 'network-only' })
      if (!data) throw new Error('No answer.')
      await settle(data.backupImport)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
      setShare(undefined)
    }
  }

  async function tryPassphrase(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const typed = String(new FormData(e.currentTarget).get('passphrase') ?? '')
    if (!status || !typed) return
    setBusy(true)
    setError(undefined)
    try {
      const { data } = await open({ variables: { input: { token: status.token, passphrase: typed } } })
      if (!data) throw new Error('No answer.')
      await settle(data.openBackupImport)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  const refusal = status?.state === 'ENDED' && status.refusal ? new ApiError(status.refusal.key, (status.refusal.args ?? {}) as Record<string, unknown>, '', 400) : undefined
  const tooLarge = error instanceof ApiError && error.key === 'import.tooLarge'
  const opened = status?.state === 'OPENED' ? status.file : undefined

  return (
    <Stack ref={root} sx={{ gap: 2, alignItems: 'flex-start' }}>
      <form onSubmit={read} style={{ display: 'contents' }}>
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {t('import.backup.hint')}
        </Typography>
        <Stack direction="row" sx={{ alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
          <input
            ref={input}
            type="file"
            accept=".zip,application/zip"
            hidden
            tabIndex={-1}
            aria-hidden
            onChange={(e) => {
              setFile(e.target.files?.[0])
              setStatus(undefined)
              setError(undefined)
            }}
          />
          <Button type="button" size="large" variant="soft" disabled={busy} onClick={() => input.current?.click()}>
            <FileUp size={16} aria-hidden />
            {t('import.file.chooseFile')}
          </Button>
          <Typography variant="body2" role="status">
            {file ? t('import.file.chosen', { name: file.name }) : t('import.file.none')}
          </Typography>
        </Stack>
        {(!status || status.state === 'ENDED') && (
          <Button size="large" type="submit" disabled={!file || busy}>
            {t('import.file.upload')}
          </Button>
        )}
      </form>

      {busy && (
        <Stack role="status" sx={{ gap: 0.5, alignSelf: 'stretch', maxWidth: 420 }}>
          <Typography variant="body2">{t('import.file.reading')}</Typography>
          <LinearProgress aria-hidden variant={share === undefined ? 'indeterminate' : 'determinate'} value={share === undefined ? undefined : Math.round(share * 100)} />
        </Stack>
      )}

      {status?.state === 'NEEDS_KEY' && (
        <Form onSubmit={tryPassphrase} style={{ display: 'contents' }}>
          <Typography variant="body2">{t('import.backup.encrypted')}</Typography>
          <Field
            name="passphrase"
            required
            label={t('import.backup.passphrase')}
            hint={t('import.backup.passphraseHint')}
            extra={
              status.wrongKey ? (
                <FieldMessage slot="error">
                  {errorText(new ApiError('backup.wrongKey', {}, '', 400))} {status.triesLeft != null && t('import.backup.triesLeft', { count: status.triesLeft })}
                </FieldMessage>
              ) : undefined
            }
          >
            <PasswordInput autoComplete="off" showLabel={t('forms.password.showPassphrase')} disabled={busy} />
          </Field>
          <Button size="large" type="submit" disabled={busy}>
            {t('import.backup.open')}
          </Button>
        </Form>
      )}

      {refusal && <ErrorMessage error={refusal} />}
      {error !== undefined && (
        <ErrorMessage error={error}>{tooLarge ? `${errorText(error)} ${t('import.backup.largerNeedsAdmin')}` : undefined}</ErrorMessage>
      )}

      {opened && status && (
        <>
          <SuccessMessage>{t('import.backup.opened', { date: date(opened.createdAt.slice(0, 10)), version: opened.version, count: opened.cars.length })}</SuccessMessage>
          <Button
            size="large"
            onClick={() => {
              done.current = true
              onOpened(status.token, opened)
            }}
          >
            {t('import.backup.next')}
          </Button>
        </>
      )}
    </Stack>
  )
}
