import { useApolloClient, useMutation, useQuery } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import FormControlLabel from '@mui/material/FormControlLabel'
import Stack from '@mui/material/Stack'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'
import { DatabaseBackup, Download, KeyRound, RotateCcw, Upload } from 'lucide-react'
import { useEffect, useEffectEvent, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { DefinitionList } from '../components/DefinitionList.tsx'
import { Segmented } from '../components/Segmented.tsx'
import { SurfaceTable } from '../components/SurfaceTable.tsx'
import { visuallyHidden } from '../components/visuallyHidden.ts'
import { DialogButtons, DialogCancel, DialogFrame } from '../dialogs/DialogFrame.tsx'
import { Field } from '../forms/Field.tsx'
import { FieldInput } from '../forms/FieldInput.tsx'
import { Form } from '../forms/Form.tsx'
import {
  BackupRunDocument,
  BackupsDocument,
  PrepareBackupEncryptionDocument,
  RestoreBackupDocument,
  RunBackupDocument,
  SessionDocument,
  type BackupRunFieldsFragment,
  type BackupsQuery,
  type PrepareBackupEncryptionMutation,
} from '../gql/generated.ts'
import { useKeyText } from '../i18n/errors.ts'
import { useFormat } from '../i18n/format.ts'
import { ErrorMessage, SuccessMessage, WarningMessage } from '../messages.tsx'
import { navigation } from '../navigation.ts'
import { downloadBackup, uploadBackup } from './backupFiles.ts'

type RestorePoint = BackupsQuery['backups']['restorePoints'][number]
type KeySet = PrepareBackupEncryptionMutation['prepareBackupEncryption']

/** How often the page asks how a backup that is being written is getting on, and whether the server is back after a restore. */
export const BACKUP_POLL_MS = 2_000
/** After this long without the server back, the page says what may keep it away (no restart policy, a refused restore). */
export const RESTART_SLOW_MS = 60_000

/** A recipient is long; the start and the end tell two apart. */
const shortRecipient = (recipient: string) => (recipient.length > 20 ? `${recipient.slice(0, 10)}…${recipient.slice(-6)}` : recipient)

const Code = ({ children }: { children: ReactNode }) => (
  <Box component="code" sx={(theme) => ({ fontFamily: 'monospace', fontSize: '0.9em', px: 0.5, borderRadius: '4px', backgroundColor: theme.vars.palette.neutral.soft, wordBreak: 'break-all' })}>
    {children}
  </Box>
)

/**
 * The Backups page of the administration: what the server holds and where its backups go, the backups in the folder (written here,
 * uploaded, or copied in by hand), a backup written now, an upload and a download, a restore (the server restarts, and the page reloads
 * once it is back with the restored data), and a key set for encrypted backups.
 */
export function BackupsPanel() {
  const { t } = useTranslation()
  const { data, error, refetch } = useQuery(BackupsDocument, { fetchPolicy: 'cache-and-network' })
  const [restoring, setRestoring] = useState<{ point: RestorePoint; generation: string | undefined }>()
  const { data: session } = useQuery(SessionDocument)

  if (restoring) return <RestartWait point={restoring.point} generation={restoring.generation} />
  if (error && !data) return <ErrorMessage error={error} />
  if (!data) return <Typography role="status">{t('admin.backups.loading')}</Typography>

  const backups = data.backups
  return (
    <Stack sx={{ gap: 3 }}>
      <Overview backups={backups} />
      <RunNow initial={backups.run} onFinished={() => void refetch()} />
      <UploadBackup maxUploadMb={backups.settings.maxUploadMb} onUploaded={() => void refetch()} />
      <RestorePoints
        backups={backups}
        onRestoring={(point) => setRestoring({ point, generation: session?.session.instance?.generation ?? undefined })}
      />
      <EncryptionSetup recipient={backups.settings.recipient ?? null} />
    </Stack>
  )
}

/** The schedule and what retention keeps, in words. */
function useScheduleText() {
  const { t } = useTranslation()
  const { dateTime, list } = useFormat()
  return (settings: BackupsQuery['backups']['settings']) => {
    const schedule = settings.every
      ? [t('admin.backups.everyDay', { time: settings.every }), settings.nextRun && t('admin.backups.nextRun', { time: dateTime(settings.nextRun) })].filter(Boolean).join(' · ')
      : t('admin.backups.notScheduled')
    const limits = [
      settings.keep != null && t('admin.backups.keepCount', { count: settings.keep }),
      settings.keepDays != null && t('admin.backups.keepDays', { count: settings.keepDays }),
    ].filter((s): s is string => typeof s === 'string')
    const retention = limits.length > 0 ? t('admin.backups.retention', { limits: list(limits) }) : t('admin.backups.keepAll')
    return { schedule, retention }
  }
}

function Overview({ backups }: { backups: BackupsQuery['backups'] }) {
  const { t } = useTranslation()
  const { bytes } = useFormat()
  const scheduleText = useScheduleText()
  const { sizes, settings } = backups
  const { schedule, retention } = scheduleText(settings)
  return (
    <section aria-labelledby="backups-overview">
      <Typography id="backups-overview" component="h2" variant="h5" sx={{ mb: 1.5 }}>
        {t('admin.backups.overview')}
      </Typography>
      <DefinitionList
        items={[
          { label: t('admin.backups.folder'), value: <Code>{backups.folder}</Code> },
          {
            label: t('admin.backups.database'),
            value: [sizes.database === null || sizes.database === undefined ? t('admin.backups.sizeUnknown') : bytes(sizes.database), backups.migration && t('admin.backups.atMigration', { migration: backups.migration })]
              .filter(Boolean)
              .join(' · '),
          },
          { label: t('admin.backups.pictures'), value: bytes(sizes.pictures) },
          { label: t('admin.backups.photos'), value: sizes.photos === null || sizes.photos === undefined ? t('admin.backups.photosWithPictures') : bytes(sizes.photos) },
          {
            label: t('admin.backups.encryption'),
            value: settings.recipient ? t('admin.backups.encryptedFor', { recipient: shortRecipient(settings.recipient) }) : t('admin.backups.notEncrypted'),
          },
          ...(settings.photosApart ? [{ label: t('admin.backups.photosInBackups'), value: settings.photos ? t('admin.backups.photosIncluded') : t('admin.backups.photosLeftOut') }] : []),
          { label: t('admin.backups.schedule'), value: schedule },
          { label: t('admin.backups.retentionLabel'), value: retention },
        ]}
      />
    </section>
  )
}

/** What the last run did, in words (an error arrives as a key). */
function useRunText() {
  const { t } = useTranslation()
  const { dateTime, bytes } = useFormat()
  const keyText = useKeyText()
  return (run: BackupRunFieldsFragment): { text: string; failed: boolean } | null => {
    if (run.running) return { text: run.startedAt ? t('admin.backups.running', { time: dateTime(run.startedAt) }) : t('admin.backups.runningNow'), failed: false }
    if (run.error) return { text: t('admin.backups.lastFailed', { reason: keyText(run.error, {}) ?? run.error }), failed: true }
    if (run.file && run.finishedAt) return { text: t('admin.backups.lastWritten', { file: run.file, time: dateTime(run.finishedAt), size: bytes(run.bytes ?? 0) }), failed: false }
    return null
  }
}

/**
 * Back up now: the server writes it in the background (it can take longer than a request may), so the page asks how it is getting on
 * until it is done, outside the cache (an answer of only the run would replace the whole overview there).
 */
function RunNow({ initial, onFinished }: { initial: BackupRunFieldsFragment; onFinished: () => void }) {
  const { t } = useTranslation()
  const runText = useRunText()
  const [runBackup, { loading: starting }] = useMutation(RunBackupDocument)
  // Each wait for a run is a round of its own (a poller mounted afresh), so no answer of an earlier round is taken for this one's.
  const [round, setRound] = useState(initial.running ? 1 : 0)
  // What the polls said last; before any, the overview's run.
  const [polled, setPolled] = useState<BackupRunFieldsFragment>()
  const [error, setError] = useState<unknown>()
  const running = round > 0

  const start = async () => {
    setError(undefined)
    try {
      await runBackup()
      // Started now, or one was under way already: either way that is the backup to wait for.
      setPolled({ ...(polled ?? initial), running: true, startedAt: null })
      setRound((n) => n + 1)
    } catch (e) {
      setError(e)
    }
  }

  const status = runText(polled ?? initial)
  return (
    <section aria-labelledby="backups-run">
      <Typography id="backups-run" component="h2" variant="h5" sx={{ mb: 1 }}>
        {t('admin.backups.runTitle')}
      </Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary', mb: 1.5 }}>
        {t('admin.backups.runHint')}
      </Typography>
      <Button variant="soft" loading={starting || running} onClick={() => void start()}>
        <DatabaseBackup size={16} aria-hidden />
        {t('admin.backups.runNow')}
      </Button>
      <div role="status">
        {status && (
          <Typography variant="body2" className={running ? 'tk-pulse' : undefined} sx={{ mt: 1, color: status.failed ? 'error.softText' : undefined }}>
            {status.text}
          </Typography>
        )}
      </div>
      {error !== undefined && <ErrorMessage error={error} />}
      {running && (
        <RunPoll
          key={round}
          onRun={(latest) => {
            setPolled(latest)
            if (!latest.running) {
              setRound(0)
              onFinished()
            }
          }}
        />
      )}
    </section>
  )
}

/** Asks how the run is getting on, outside the cache (an answer of only the run would replace the whole overview there). */
function RunPoll({ onRun }: { onRun: (run: BackupRunFieldsFragment) => void }) {
  const { data } = useQuery(BackupRunDocument, { fetchPolicy: 'no-cache', pollInterval: BACKUP_POLL_MS })
  const report = useEffectEvent(onRun)
  useEffect(() => {
    if (data) report(data.backups.run)
  }, [data])
  return null
}

function UploadBackup({ maxUploadMb, onUploaded }: { maxUploadMb: number; onUploaded: () => void }) {
  const { t } = useTranslation()
  const { bytes } = useFormat()
  const input = useRef<HTMLInputElement>(null)
  const [busy, setBusy] = useState(false)
  const [status, setStatus] = useState<string>()
  const [error, setError] = useState<unknown>()

  const send = async (file: File) => {
    setBusy(true)
    setError(undefined)
    setStatus(t('admin.backups.uploading', { file: file.name, size: bytes(file.size) }))
    try {
      const stored = await uploadBackup(file)
      setStatus(t('admin.backups.uploaded', { file: stored.file }))
      onUploaded()
    } catch (e) {
      setStatus(undefined)
      setError(e)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section aria-labelledby="backups-upload">
      <Typography id="backups-upload" component="h2" variant="h5" sx={{ mb: 1 }}>
        {t('admin.backups.uploadTitle')}
      </Typography>
      <Typography id="backups-upload-hint" variant="body2" sx={{ color: 'text.secondary', mb: 1.5 }}>
        {t('admin.backups.uploadHint', { max: maxUploadMb })}
      </Typography>
      <input
        ref={input}
        type="file"
        accept=".zip,application/zip"
        hidden
        tabIndex={-1}
        aria-hidden
        data-testid="backup-file"
        onChange={(e) => {
          const file = e.target.files?.[0]
          e.target.value = '' // choosing the same file again must still fire
          if (file) void send(file)
        }}
      />
      <Button variant="soft" loading={busy} aria-describedby="backups-upload-hint" onClick={() => input.current?.click()}>
        <Upload size={16} aria-hidden />
        {t('admin.backups.upload')}
      </Button>
      <div role="status">{status && <Typography variant="body2" sx={{ mt: 1 }}>{status}</Typography>}</div>
      {error !== undefined && <ErrorMessage error={error} />}
    </section>
  )
}

/** What a backup holds, in words: the release and database it came from, how many rows, whether it is encrypted (for which key) and has the photos. */
function useContentsText() {
  const { t } = useTranslation()
  const { number } = useFormat()
  return (point: RestorePoint): string =>
    [
      t('admin.backups.madeBy', { version: point.appVersion, provider: point.provider }),
      t('admin.backups.rows', { count: point.rows, rows: number(point.rows) }),
      point.encrypted ? (point.currentRecipient ? t('admin.backups.encryptedCurrent') : t('admin.backups.encryptedOther')) : t('admin.backups.plain'),
      point.photos === 'LEFT_OUT' ? t('admin.backups.withoutPhotos') : t('admin.backups.withPhotos'),
    ].join(' · ')
}

function RestorePoints({ backups, onRestoring }: { backups: BackupsQuery['backups']; onRestoring: (point: RestorePoint) => void }) {
  const { t } = useTranslation()
  const { dateTime, bytes } = useFormat()
  const contentsText = useContentsText()
  const [error, setError] = useState<unknown>()
  const [restore, setRestore] = useState<RestorePoint>()

  return (
    <section aria-labelledby="backups-points">
      <Typography id="backups-points" component="h2" variant="h5" sx={{ mb: 1 }}>
        {t('admin.backups.pointsTitle')}
      </Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary', mb: 1.5 }}>
        {t('admin.backups.pointsHint')}
      </Typography>
      {error !== undefined && <ErrorMessage error={error} />}
      {backups.restorePoints.length === 0 ? (
        <Typography>{t('admin.backups.none', { folder: backups.folder })}</Typography>
      ) : (
        <SurfaceTable>
          <TableHead>
            <TableRow>
              <TableCell>{t('admin.backups.colMade')}</TableCell>
              <TableCell>{t('admin.backups.colContents')}</TableCell>
              <TableCell>{t('admin.backups.colSize')}</TableCell>
              <TableCell>
                <span style={visuallyHidden}>{t('grid.actions')}</span>
              </TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {backups.restorePoints.map((p) => {
              const made = dateTime(p.createdAt)
              return (
                <TableRow key={p.file}>
                  <TableCell component="th" scope="row">
                    <Typography variant="body2">{made}</Typography>
                    <Typography variant="caption" sx={{ color: 'text.secondary', wordBreak: 'break-all' }}>
                      {[p.file, p.safetyNet && t('admin.backups.safetyNet'), p.uploaded && t('admin.backups.uploadedTag')].filter(Boolean).join(' · ')}
                    </Typography>
                  </TableCell>
                  <TableCell>
                    <Typography variant="body2">{contentsText(p)}</Typography>
                    {!p.sameMigration && (
                      <Typography variant="caption" sx={{ color: 'warning.softText' }}>
                        {t('admin.backups.otherMigration', { version: p.appVersion })}
                      </Typography>
                    )}
                  </TableCell>
                  <TableCell>{bytes(p.bytes)}</TableCell>
                  <TableCell align="right">
                    <Stack direction="row" sx={{ gap: 1, justifyContent: 'flex-end', flexWrap: 'wrap' }}>
                      <Button size="small" variant="soft" aria-label={t('admin.backups.downloadAria', { made })} onClick={() => downloadBackup(p.file).catch(setError)}>
                        <Download size={16} aria-hidden />
                        {t('admin.backups.download')}
                      </Button>
                      {p.sameMigration && (
                        <Button size="small" variant="soft" color="error" aria-label={t('admin.backups.restoreAria', { made })} onClick={() => setRestore(p)}>
                          <RotateCcw size={16} aria-hidden />
                          {t('admin.backups.restore')}
                        </Button>
                      )}
                    </Stack>
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </SurfaceTable>
      )}
      <RestoreDialog point={restore} onClose={() => setRestore(undefined)} onRestoring={onRestoring} />
    </section>
  )
}

/** The confirmation of a restore, with the passphrase (or the identity) of an encrypted backup. Nothing is written when it is wrong. */
function RestoreDialog({ point, onClose, onRestoring }: { point: RestorePoint | undefined; onClose: () => void; onRestoring: (point: RestorePoint) => void }) {
  const { t } = useTranslation()
  const { dateTime } = useFormat()
  // The dialog's content stays while it closes, so the last point is kept for the exit.
  const [shown, setShown] = useState(point)
  if (point && point !== shown) setShown(point)
  const [busy, setBusy] = useState(false)
  return (
    <DialogFrame
      open={point !== undefined}
      onClose={onClose}
      busy={busy}
      role="alertdialog"
      title={t('admin.backups.restoreTitle', { made: shown ? dateTime(shown.createdAt) : '' })}
      description={t('admin.backups.restoreDescription')}
    >
      {shown && <RestoreForm key={shown.file} point={shown} onBusy={setBusy} onRestoring={onRestoring} />}
    </DialogFrame>
  )
}

function RestoreForm({ point, onBusy, onRestoring }: { point: RestorePoint; onBusy: (busy: boolean) => void; onRestoring: (point: RestorePoint) => void }) {
  const { t } = useTranslation()
  const [restoreBackup] = useMutation(RestoreBackupDocument, { fetchPolicy: 'no-cache' })
  const [key, setKey] = useState<'passphrase' | 'identity'>('passphrase')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>()

  const submit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const secret = String(new FormData(e.currentTarget).get('secret') ?? '')
    setBusy(true)
    onBusy(true)
    setError(undefined)
    try {
      await restoreBackup({
        variables: { input: { file: point.file, passphrase: point.encrypted && key === 'passphrase' ? secret : null, identity: point.encrypted && key === 'identity' ? secret.trim() : null } },
      })
      onRestoring(point)
    } catch (err) {
      setError(err)
      setBusy(false)
      onBusy(false)
    }
  }

  return (
    <Form onSubmit={(e) => void submit(e)}>
      <Stack sx={{ gap: 1.5 }}>
        {point.encrypted && (
          <>
            <Segmented
              label={t('admin.backups.keyKind')}
              value={key}
              onChange={setKey}
              options={[
                { value: 'passphrase', label: t('admin.backups.keyPassphrase') },
                { value: 'identity', label: t('admin.backups.keyIdentity') },
              ]}
            />
            <Field
              key={key}
              name="secret"
              required
              label={key === 'passphrase' ? t('admin.backups.passphrase') : t('admin.backups.identity')}
              hint={key === 'passphrase' ? t('admin.backups.passphraseHint') : t('admin.backups.identityHint')}
            >
              <FieldInput type="password" autoComplete="off" />
            </Field>
          </>
        )}
        {error !== undefined && <ErrorMessage error={error} />}
        <DialogButtons>
          <DialogCancel autoFocus={!point.encrypted} disabled={busy} />
          <Button type="submit" color="error" loading={busy}>
            {t('admin.backups.restoreConfirm')}
          </Button>
        </DialogButtons>
      </Stack>
    </Form>
  )
}

/**
 * After a restore was asked for: the server stops, takes the backup in as it starts and comes back with another generation of the data.
 * The page asks the server who it is until then (past the device's own answers) and reloads once the data is the restored one, so nothing
 * on screen is from before. A server back without the restore (it was refused at start) and one that stays away are said so.
 */
function RestartWait({ point, generation }: { point: RestorePoint; generation: string | undefined }) {
  const { t } = useTranslation()
  const { dateTime } = useFormat()
  const client = useApolloClient()
  const [state, setState] = useState<'waiting' | 'slow' | 'notRestored'>('waiting')

  useEffect(() => {
    let stopped = false
    let timer: ReturnType<typeof setTimeout> | undefined
    const started = Date.now()
    const ask = async () => {
      try {
        const { data } = await client.query({ query: SessionDocument, fetchPolicy: 'network-only', context: { offline: 'bypass' } })
        const now = data?.session.instance?.generation
        if (now && now !== generation) {
          stopped = true
          navigation.reload()
          return
        }
        // Answered with the data as it was and nothing waiting any more: the restore was refused as the app started.
        if (data && !data.notices.some((n) => n.code === 'RESTORE_PENDING')) {
          setState('notRestored')
          stopped = true
          return
        }
      } catch {
        // the server is away: it is restarting
      }
      if (Date.now() - started > RESTART_SLOW_MS) setState((s) => (s === 'waiting' ? 'slow' : s))
      if (!stopped) timer = setTimeout(() => void ask(), BACKUP_POLL_MS)
    }
    // At once too: until the app stops, the restore waiting (the notice) says it has not happened yet.
    void ask()
    return () => {
      stopped = true
      clearTimeout(timer)
    }
  }, [client, generation])

  if (state === 'notRestored') return <WarningMessage>{t('admin.backups.notRestored')}</WarningMessage>
  return (
    <Stack role="status" sx={{ gap: 1 }}>
      <Typography className="tk-pulse">{t('admin.backups.restarting', { made: dateTime(point.createdAt) })}</Typography>
      {state === 'slow' && <Typography variant="body2" sx={{ color: 'text.secondary' }}>{t('admin.backups.restartSlow')}</Typography>}
    </Stack>
  )
}

/** A key set for encrypted backups: made on the server from a passphrase (or one made up), shown once, then set as two settings. */
function EncryptionSetup({ recipient }: { recipient: string | null }) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  return (
    <section aria-labelledby="backups-encryption">
      <Typography id="backups-encryption" component="h2" variant="h5" sx={{ mb: 1 }}>
        {t('admin.backups.encryptionTitle')}
      </Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary', mb: 1.5 }}>
        {recipient ? t('admin.backups.encryptionOn', { recipient: shortRecipient(recipient) }) : t('admin.backups.encryptionOff')}
      </Typography>
      <Button variant="soft" onClick={() => setOpen(true)}>
        <KeyRound size={16} aria-hidden />
        {t('admin.backups.makeKeys')}
      </Button>
      <DialogFrame open={open} onClose={() => setOpen(false)} maxWidth={560} title={t('admin.backups.keysTitle')} description={t('admin.backups.keysDescription')}>
        <KeySetForm replacing={recipient !== null} />
      </DialogFrame>
    </section>
  )
}

function KeySetForm({ replacing }: { replacing: boolean }) {
  const { t } = useTranslation()
  const [prepare] = useMutation(PrepareBackupEncryptionDocument, { fetchPolicy: 'no-cache' })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>()
  const [keys, setKeys] = useState<KeySet>()

  if (keys) return <KeySetShown keys={keys} />

  const submit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const passphrase = String(new FormData(e.currentTarget).get('passphrase') ?? '')
    setBusy(true)
    setError(undefined)
    try {
      const made = await prepare({ variables: { passphrase: passphrase === '' ? null : passphrase } })
      if (made.data) setKeys(made.data.prepareBackupEncryption)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Form onSubmit={(e) => void submit(e)}>
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

function KeySetShown({ keys }: { keys: KeySet }) {
  const { t } = useTranslation()
  const [saved, setSaved] = useState(false)
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
      <Typography variant="body2">{t('admin.backups.keysSettings')}</Typography>
      <Box component="pre" sx={(theme) => ({ m: 0, p: 1.5, borderRadius: '8px', backgroundColor: theme.vars.palette.neutral.soft, whiteSpace: 'pre-wrap', wordBreak: 'break-all', fontSize: '0.85em' })}>
        {`Backups__Encryption__Recipient=${keys.recipient}\nBackups__Encryption__Salt=${keys.salt}`}
      </Box>
      <FormControlLabel control={<Checkbox checked={saved} onChange={(e) => setSaved(e.target.checked)} />} label={t('admin.backups.keysSaved')} />
      <DialogButtons>
        <DialogCancel label={t('admin.backups.keysDone')} disabled={!saved} />
      </DialogButtons>
    </Stack>
  )
}
