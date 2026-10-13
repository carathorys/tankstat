import { useApolloClient, useMutation, useQuery } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import Link from '@mui/material/Link'
import FormControlLabel from '@mui/material/FormControlLabel'
import Stack from '@mui/material/Stack'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import { Archive, DatabaseBackup, Download, Image, ImageOff, KeyRound, LifeBuoy, Lock, LockOpen, RotateCcw, Trash2, Upload, type LucideIcon } from 'lucide-react'
import { useEffect, useEffectEvent, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link as RouterLink } from 'react-router'
import { ConfirmDialog } from '../components/ConfirmDialog.tsx'
import { DefinitionList } from '../components/DefinitionList.tsx'
import { Loading } from '../components/Loading.tsx'
import { Segmented } from '../components/Segmented.tsx'
import { SurfaceTable } from '../components/SurfaceTable.tsx'
import { visuallyHidden } from '../components/visuallyHidden.ts'
import { DialogButtons, DialogCancel, DialogFrame } from '../dialogs/DialogFrame.tsx'
import { DialogTrigger } from '../dialogs/DialogTrigger.tsx'
import { useDialogState } from '../dialogs/useDialogState.ts'
import { Field } from '../forms/Field.tsx'
import { FieldInput } from '../forms/FieldInput.tsx'
import { Form } from '../forms/Form.tsx'
import {
  BackupRunDocument,
  BackupsDocument,
  DeleteBackupDocument,
  PrepareBackupEncryptionDocument,
  RestoreBackupDocument,
  RunBackupDocument,
  SessionDocument,
  SystemSettingsDocument,
  UpdateSystemSettingsDocument,
  type BackupRunFieldsFragment,
  type BackupsQuery,
  type PrepareBackupEncryptionMutation,
} from '../gql/generated.ts'
import { useKeyText } from '../i18n/errors.ts'
import { useFormat } from '../i18n/format.ts'
import { ErrorMessage, SuccessMessage, WarningMessage } from '../messages.tsx'
import { navigation } from '../navigation.ts'
import { connectivity } from '../offline/connectivity.ts'
import { classifyFailure } from '../offline/errors.ts'
import { PROBE_TIMEOUT_MS, withDeadline } from '../offline/runtime.ts'
import { ApiError } from '../pictures/ApiError.ts'
import { downloadBackup, uploadBackup } from './backupFiles.ts'
import { useToast } from '../toast/toastContext.ts'
import { ProblemList } from './ProblemList.tsx'
import { problemsOf } from './settingsText.ts'

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
  const [restoring, setRestoring] = useState<{ point: RestorePoint; generation: string | undefined }>()
  // Not asked while a restore restarts the server (the answer would be of the data before it, worked out by a server just starting).
  const { data, error, refetch } = useQuery(BackupsDocument, { fetchPolicy: 'cache-and-network', skip: restoring !== undefined })
  const { data: session } = useQuery(SessionDocument)

  if (restoring) return <RestartWait point={restoring.point} generation={restoring.generation} />
  if (error && !data) return <ErrorMessage error={error} />
  if (!data) return <Loading label={t('admin.backups.loading')} />

  const backups = data.backups
  return (
    <Stack sx={{ gap: 3 }}>
      <Overview backups={backups} />
      <RunNow initial={backups.run} onFinished={() => void refetch()} />
      <UploadBackup maxUploadMb={backups.settings.maxUploadMb} onUploaded={() => void refetch()} />
      <RestorePoints
        backups={backups}
        onDeleted={() => void refetch()}
        onRestoring={(point) => setRestoring({ point, generation: session?.session.instance?.generation ?? undefined })}
      />
      <EncryptionSetup recipient={backups.settings.recipient ?? null} />
    </Stack>
  )
}

/**
 * The schedule and what retention keeps, in words: the days and times are the server's (its zone, which is named, may not be the
 * browser's), the next run the browser's.
 */
function useScheduleText() {
  const { t } = useTranslation()
  const { dateTime, list, timeOfDay, weekday } = useFormat()
  return (settings: BackupsQuery['backups']['settings']) => {
    const times = list(settings.times.map(timeOfDay))
    const when =
      settings.days.length === 7
        ? t('admin.backups.everyDay', { times, zone: settings.timeZone })
        : t('admin.backups.onDays', { days: list(settings.days.map((day) => weekday(day))), times, zone: settings.timeZone })
    const schedule =
      settings.times.length > 0 && settings.days.length > 0
        ? [when, settings.nextRun && t('admin.backups.nextRun', { time: dateTime(settings.nextRun) })].filter(Boolean).join(' · ')
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
      <Link component={RouterLink} to="/admin/settings?tab=backups" sx={{ display: 'inline-block', mt: 1 }}>
        {t('admin.backups.changeSettings')}
      </Link>
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
  // What the polls said last; before any, and again with every new answer of the overview, the overview's run.
  const [polled, setPolled] = useState<BackupRunFieldsFragment>()
  // A run this page asked for is followed until a poll says it ended, whatever an overview answer from before it began says meanwhile.
  const [waiting, setWaiting] = useState(false)
  const [seen, setSeen] = useState(initial)
  if (initial !== seen) {
    setSeen(initial)
    if (!waiting) setPolled(undefined)
  }
  const [error, setError] = useState<unknown>()
  const run = polled ?? initial
  // Followed while one is under way: the one asked for here, or one an answer says is (the cached overview, then the server's, on a later
  // visit; one another tab started), so the page never says "being written" without asking how it gets on.
  const running = waiting || run.running

  const start = async () => {
    setError(undefined)
    try {
      await runBackup()
      // Started now, or one was under way already: either way that is the backup to wait for.
      setPolled({ ...run, running: true, startedAt: null })
      setWaiting(true)
    } catch (e) {
      setError(e)
    }
  }

  const status = runText(run)
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
          onRun={(latest) => {
            setPolled(latest)
            if (!latest.running) {
              setWaiting(false)
              onFinished()
            }
          }}
        />
      )}
    </section>
  )
}

/**
 * Asks how the run is getting on, outside the cache (an answer of only the run would replace the whole overview there). Mounted afresh for
 * every wait (the button is off while one goes on), so no answer of an earlier wait counts for this one; nothing is asked from a hidden tab
 * or while the server is out of reach.
 */
function RunPoll({ onRun }: { onRun: (run: BackupRunFieldsFragment) => void }) {
  const { data } = useQuery(BackupRunDocument, {
    fetchPolicy: 'no-cache',
    pollInterval: BACKUP_POLL_MS,
    skipPollAttempt: () => document.hidden || !connectivity.reachable,
  })
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
    // Larger than the server takes (its rule exactly): said at once, before anything is sent. The server would refuse it as soon as it
    // starts to arrive, which a browser still sending may only report as a lost connection.
    if (file.size > maxUploadMb * 1024 * 1024) {
      setStatus(undefined)
      setError(new ApiError('backup.tooLarge', { maxMb: maxUploadMb }, 'The backup is larger than an upload may be.', 413))
      return
    }
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
/**
 * A property of a backup as an icon whose meaning is its tooltip and its name (read by a screen reader as an image of that name); it takes
 * the focus, so the tooltip is shown from the keyboard too. The colour only repeats what the icon and its name say.
 */
function PropertyIcon({ icon: Icon, label, tone }: { icon: LucideIcon; label: string; tone?: 'success' | 'warning' }) {
  return (
    <Tooltip title={label}>
      <Box
        component="span"
        role="img"
        tabIndex={0}
        sx={{ display: 'inline-flex', p: 0.5, borderRadius: 1, color: tone ? `${tone}.softText` : 'text.secondary', '&:focus-visible': { outline: '2px solid', outlineColor: 'primary.main' } }}
      >
        <Icon size={18} aria-hidden />
      </Box>
    </Tooltip>
  )
}

/** What kind of backup a row is, its encryption and its photos, each as an icon with its meaning. */
function useBackupIcons() {
  const { t } = useTranslation()
  return {
    kind: (p: RestorePoint) =>
      p.safetyNet ? (
        <PropertyIcon icon={LifeBuoy} label={t('admin.backups.safetyNet')} />
      ) : p.uploaded ? (
        <PropertyIcon icon={Upload} label={t('admin.backups.uploadedTag')} />
      ) : (
        <PropertyIcon icon={Archive} label={t('admin.backups.kindBackup')} />
      ),
    encryption: (p: RestorePoint) =>
      !p.encrypted ? (
        <PropertyIcon icon={LockOpen} label={t('admin.backups.plain')} />
      ) : p.currentRecipient ? (
        <PropertyIcon icon={Lock} label={t('admin.backups.encryptedCurrent')} tone="success" />
      ) : (
        <PropertyIcon icon={KeyRound} label={t('admin.backups.encryptedOther')} tone="warning" />
      ),
    photos: (p: RestorePoint) =>
      p.photos === 'LEFT_OUT' ? <PropertyIcon icon={ImageOff} label={t('admin.backups.withoutPhotos')} /> : <PropertyIcon icon={Image} label={t('admin.backups.withPhotos')} />,
  }
}

function RestorePoints({
  backups,
  onDeleted,
  onRestoring,
}: {
  backups: BackupsQuery['backups']
  onDeleted: () => void
  onRestoring: (point: RestorePoint) => void
}) {
  const { t } = useTranslation()
  const { dateTime, bytes, number } = useFormat()
  const { toast } = useToast()
  const icons = useBackupIcons()
  const [error, setError] = useState<unknown>()
  const [deleteBackup] = useMutation(DeleteBackupDocument)

  const remove = async (file: string) => {
    setError(undefined)
    try {
      await deleteBackup({ variables: { file } })
      toast(t('admin.backups.deleted', { file }))
    } catch (e) {
      setError(e)
    }
    onDeleted() // also after a refusal: a file gone since the list leaves it
  }

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
              <TableCell>{t('admin.backups.colKind')}</TableCell>
              <TableCell>{t('admin.backups.colRelease')}</TableCell>
              <TableCell align="right">{t('admin.backups.colRows')}</TableCell>
              <TableCell>{t('admin.backups.colEncryption')}</TableCell>
              <TableCell>{t('admin.backups.colPhotos')}</TableCell>
              <TableCell align="right">{t('admin.backups.colSize')}</TableCell>
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
                      {p.file}
                    </Typography>
                  </TableCell>
                  <TableCell>{icons.kind(p)}</TableCell>
                  <TableCell>
                    <Typography variant="body2" sx={{ whiteSpace: 'nowrap' }}>
                      {t('admin.backups.release', { version: p.appVersion })}
                    </Typography>
                    <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                      {p.provider}
                    </Typography>
                    {p.olderMigration && (
                      <Typography variant="caption" sx={{ display: 'block', color: 'text.secondary', minWidth: '12rem' }}>
                        {t('admin.backups.olderMigration')}
                      </Typography>
                    )}
                    {!p.sameMigration && !p.olderMigration && (
                      <Typography variant="caption" sx={{ display: 'block', color: 'warning.softText', minWidth: '12rem' }}>
                        {t('admin.backups.otherMigration', { version: p.appVersion })}
                      </Typography>
                    )}
                  </TableCell>
                  <TableCell align="right">{number(p.rows)}</TableCell>
                  <TableCell>{icons.encryption(p)}</TableCell>
                  <TableCell>{icons.photos(p)}</TableCell>
                  <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                    {bytes(p.bytes)}
                  </TableCell>
                  <TableCell align="right">
                    <Stack direction="row" sx={{ gap: 1, justifyContent: 'flex-end', flexWrap: 'wrap' }}>
                      <Button
                        size="small"
                        variant="soft"
                        aria-label={t('admin.backups.downloadAria', { made, file: p.file })}
                        onClick={() => downloadBackup(p.file).catch(setError)}
                      >
                        <Download size={16} aria-hidden />
                        {t('admin.backups.download')}
                      </Button>
                      {(p.sameMigration || p.olderMigration) && (
                        <RestoreDialog
                          point={p}
                          onRestoring={onRestoring}
                          trigger={
                            <Button size="small" variant="soft" color="error" aria-label={t('admin.backups.restoreAria', { made, file: p.file })}>
                              <RotateCcw size={16} aria-hidden />
                              {t('admin.backups.restore')}
                            </Button>
                          }
                        />
                      )}
                      <ConfirmDialog
                        title={t('admin.backups.deleteTitle', { made, file: p.file })}
                        description={t('admin.backups.deleteDescription')}
                        confirmLabel={t('admin.backups.deleteConfirm')}
                        onConfirm={() => void remove(p.file)}
                        trigger={
                          <Button size="small" variant="soft" color="error" aria-label={t('admin.backups.deleteAria', { made, file: p.file })}>
                            <Trash2 size={16} aria-hidden />
                            {t('admin.backups.delete')}
                          </Button>
                        }
                      />
                    </Stack>
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </SurfaceTable>
      )}
    </section>
  )
}

/**
 * The confirmation of a restore, opened by its row's button, with the passphrase (or the identity) of an encrypted backup. It names the
 * file as well as the time, since two backups can share a minute (a copy of one, a safety net written just after it). Nothing is written
 * when the key is wrong.
 */
function RestoreDialog({ point, trigger, onRestoring }: { point: RestorePoint; trigger: ReactNode; onRestoring: (point: RestorePoint) => void }) {
  const { t } = useTranslation()
  const { dateTime } = useFormat()
  const [open, setOpen] = useDialogState()
  const [busy, setBusy] = useState(false)
  return (
    <>
      <DialogTrigger trigger={trigger} open={open} onOpen={() => setOpen(true)} />
      <DialogFrame
        open={open}
        onClose={() => setOpen(false)}
        busy={busy}
        role="alertdialog"
        title={t('admin.backups.restoreTitle', { made: dateTime(point.createdAt), file: point.file })}
        description={[t('admin.backups.restoreDescription'), point.olderMigration && t('admin.backups.restoreOlder', { version: point.appVersion })].filter(Boolean).join(' ')}
      >
        <RestoreForm point={point} busy={busy} onBusy={setBusy} onRestoring={onRestoring} />
      </DialogFrame>
    </>
  )
}

function RestoreForm({
  point,
  busy,
  onBusy,
  onRestoring,
}: {
  point: RestorePoint
  busy: boolean
  onBusy: (busy: boolean) => void
  onRestoring: (point: RestorePoint) => void
}) {
  const { t } = useTranslation()
  const [restoreBackup] = useMutation(RestoreBackupDocument, { fetchPolicy: 'no-cache' })
  const [key, setKey] = useState<'passphrase' | 'identity'>('passphrase')
  const [error, setError] = useState<unknown>()

  const submit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const secret = String(new FormData(e.currentTarget).get('secret') ?? '')
    onBusy(true)
    setError(undefined)
    try {
      await restoreBackup({
        variables: { input: { file: point.file, passphrase: point.encrypted && key === 'passphrase' ? secret : null, identity: point.encrypted && key === 'identity' ? secret.trim() : null } },
      })
      onRestoring(point)
    } catch (err) {
      // The server restarts a moment after it answers: a request that went and got no answer is that restart, which the wait then follows
      // (it says so if the restore turns out not to have happened). Anything answered is a refusal, and so is an OfflineError (nothing sent).
      if (classifyFailure(err) !== 'answered') {
        onRestoring(point)
        return
      }
      setError(err)
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
 * on screen is from before: an answer without the restore waiting any more, of another generation than the answers that still had it
 * waiting. A server back without the restore (it was refused at start) and one that stays away are said so.
 */
function RestartWait({ point, generation }: { point: RestorePoint; generation: string | undefined }) {
  const { t } = useTranslation()
  const { dateTime } = useFormat()
  const client = useApolloClient()
  const [state, setState] = useState<'waiting' | 'slow' | 'notRestored'>('waiting')
  // The dialog and the row that opened it are gone: the focus goes to what took their place, which also has it read out.
  const shown = useRef<HTMLDivElement>(null)
  useEffect(() => shown.current?.focus(), [])

  useEffect(() => {
    let stopped = false
    let timer: ReturnType<typeof setTimeout> | undefined
    // The generation of the data before the restore. An answer that still has the restore waiting comes from the server before it, so it
    // says which that is (the page's own may be older: a restore made elsewhere since it loaded).
    let before = generation
    const started = Date.now()
    const ask = async () => {
      try {
        // A deadline per question, as the settings page's restart has, and never one joined to an earlier question still unanswered.
        const { data } = await withDeadline(
          client.query({ query: SessionDocument, fetchPolicy: 'network-only', context: { offline: 'bypass', queryDeduplication: false } }),
          PROBE_TIMEOUT_MS,
        )
        const now = data?.session.instance?.generation
        if (data?.notices.some((n) => n.code === 'RESTORE_PENDING')) before = now ?? before
        else if (data) {
          // Back, and nothing waits any more: restored when the data is of another generation, else refused as the app started.
          stopped = true
          if (now && now !== before) navigation.reload()
          else setState('notRestored')
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
    <Stack ref={shown} tabIndex={-1} role="status" sx={{ gap: 1 }}>
      <Typography className="tk-pulse">{t('admin.backups.restarting', { made: dateTime(point.createdAt) })}</Typography>
      {state === 'slow' && <Typography variant="body2" sx={{ color: 'text.secondary' }}>{t('admin.backups.restartSlow')}</Typography>}
    </Stack>
  )
}

/** A key set for encrypted backups: made on the server from a passphrase (or one made up), shown once, then set as two settings. */
function EncryptionSetup({ recipient }: { recipient: string | null }) {
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
        <KeySetForm replacing={recipient !== null} onLock={setLocked} />
      </DialogFrame>
    </section>
  )
}

function KeySetForm({ replacing, onLock }: { replacing: boolean; onLock: (locked: boolean) => void }) {
  const { t } = useTranslation()
  const [prepare] = useMutation(PrepareBackupEncryptionDocument, { fetchPolicy: 'no-cache' })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>()
  const [keys, setKeys] = useState<KeySet>()

  if (keys) return <KeySetShown keys={keys} onLock={onLock} />

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

function KeySetShown({ keys, onLock }: { keys: KeySet; onLock: (locked: boolean) => void }) {
  const { t } = useTranslation()
  const [saved, setSaved] = useState(false)
  // The settings page's own save: the answer is that page as the server now has it, and the Backups page says which key it encrypts for.
  const [update, { loading: using, data: used, error: refused }] = useMutation(UpdateSystemSettingsDocument, {
    update: (cache, { data }) => {
      if (data) cache.writeQuery({ query: SystemSettingsDocument, data: { systemSettings: data.updateSystemSettings } })
    },
    refetchQueries: [BackupsDocument],
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
