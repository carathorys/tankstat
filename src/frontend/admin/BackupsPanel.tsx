import { useMutation, useQuery } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Link from '@mui/material/Link'
import Stack from '@mui/material/Stack'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import { Archive, DatabaseBackup, Download, FolderOpen, Image, ImageOff, KeyRound, LifeBuoy, Lock, LockKeyholeOpen, LockOpen, Pin, PinOff, RotateCcw, Trash2, Upload, type LucideIcon } from 'lucide-react'
import { useEffect, useEffectEvent, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link as RouterLink } from 'react-router'
import { ConfirmDialog } from '../components/ConfirmDialog.tsx'
import { DefinitionList } from '../components/DefinitionList.tsx'
import { Loading } from '../components/Loading.tsx'
import { SurfaceTable } from '../components/SurfaceTable.tsx'
import { visuallyHidden } from '../components/visuallyHidden.ts'
import {
  BackupRunDocument,
  BackupsDocument,
  DeleteBackupDocument,
  HoldBackupDocument,
  RunBackupDocument,
  SessionDocument,
  type BackupRunFieldsFragment,
  type BackupsQuery,
  type RestoreTargetFieldsFragment,
} from '../gql/generated.ts'
import { useKeyText } from '../i18n/errors.ts'
import { useFormat } from '../i18n/format.ts'
import { ErrorMessage } from '../messages.tsx'
import { connectivity } from '../offline/connectivity.ts'
import { COARSE, standaloneLink } from '../theme/components.ts'
import { BackupDetailsDialog } from './backupDetails.tsx'
import { downloadBackup } from './backupFiles.ts'
import { secretsInPlain, useHeldText } from './backupMarks.ts'
import { Code, EncryptionSetup } from './encryption.tsx'
import { shortRecipient } from './recipient.ts'
import { UploadBackup } from './upload.tsx'
import { BACKUP_POLL_MS, RestartWait, RestoreDialog, RestoreRiskLines } from './restore.tsx'
import { useToast } from '../toast/toastContext.ts'

type RestorePoint = BackupsQuery['backups']['restorePoints'][number]

/**
 * The Backups page of the administration: what the server holds and where its backups go, the backups in the folder (written here,
 * uploaded, or copied in by hand), a backup written now, an upload and a download, a restore (the server restarts, and the page reloads
 * once it is back with the restored data), and a key set for encrypted backups.
 */
export function BackupsPanel() {
  const { t } = useTranslation()
  const [restoring, setRestoring] = useState<{ point: RestoreTargetFieldsFragment; generation: string | undefined }>()
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
        onChanged={() => refetch()}
        onRestoring={(point) => setRestoring({ point, generation: session?.session.instance?.generation ?? undefined })}
      />
      <EncryptionSetup recipient={backups.settings.recipient ?? null} refresh={BackupsDocument} />
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
          { label: t('admin.backups.environmentLabel'), value: settings.environment === 'MERGE' ? t('admin.backups.environmentMerge') : t('admin.backups.environmentRecord') },
          {
            label: t('systemSettings.fields.Backups__Secrets'),
            value: !settings.secrets ? t('admin.backups.secretsLeftOut') : settings.recipient ? t('admin.backups.secretsIncluded') : t('admin.backups.secretsIncludedPlain'),
          },
        ]}
      />
      <Link component={RouterLink} to="/admin/settings?tab=backups" sx={{ display: 'inline-block', mt: 1, ...standaloneLink }}>
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


/**
 * A property of a backup as an icon whose meaning is its tooltip and its name (read by a screen reader as an image of that name); it takes
 * the focus, so the tooltip is shown from the keyboard too, and a tap shows it at once on a touch screen, where the icon is a 44 px target.
 * The colour only repeats what the icon and its name say.
 */
function PropertyIcon({ icon: Icon, label, tone }: { icon: LucideIcon; label: string; tone?: 'success' | 'warning' | 'error' }) {
  return (
    <Tooltip title={label} enterTouchDelay={0} leaveTouchDelay={8000}>
      <Box
        component="span"
        role="img"
        tabIndex={0}
        sx={{
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          p: 0.5,
          borderRadius: 1,
          color: tone ? `${tone}.softText` : 'text.secondary',
          '&:focus-visible': { outline: '2px solid', outlineColor: 'primary.main' },
          [COARSE]: { minWidth: 44, minHeight: 44 },
        }}
      >
        <Icon size={18} aria-hidden />
      </Box>
    </Tooltip>
  )
}

/** What kind of backup a row is, its hold, its encryption and its photos, each as an icon with its meaning. */
function useBackupIcons() {
  const { t } = useTranslation()
  const heldText = useHeldText()
  return {
    held: (p: RestorePoint) => (p.held ? <PropertyIcon icon={Pin} label={heldText(p) ?? ''} /> : null),
    kind: (p: RestorePoint) =>
      p.safetyNet ? (
        <PropertyIcon icon={LifeBuoy} label={t('admin.backups.safetyNet')} />
      ) : p.uploaded ? (
        <PropertyIcon icon={Upload} label={t('admin.backups.uploadedTag')} />
      ) : (
        <PropertyIcon icon={Archive} label={t('admin.backups.kindBackup')} />
      ),
    encryption: (p: RestorePoint) =>
      // Its secrets readable by anyone with the file: a glyph of its own, not only a colour (#140's improvements).
      secretsInPlain(p) ? (
        <PropertyIcon icon={LockKeyholeOpen} label={t('admin.backups.secretsPlainMark')} tone="error" />
      ) : !p.encrypted ? (
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
  onChanged,
  onRestoring,
}: {
  backups: BackupsQuery['backups']
  /** The list asked again, after a delete or a hold (a refusal too: a file gone since the list leaves it). */
  onChanged: () => Promise<unknown>
  onRestoring: (point: RestoreTargetFieldsFragment) => void
}) {
  const { t } = useTranslation()
  const { dateTime, bytes, number } = useFormat()
  const { toast } = useToast()
  const icons = useBackupIcons()
  const [error, setError] = useState<unknown>()
  const [deleteBackup] = useMutation(DeleteBackupDocument)
  const [holdBackup] = useMutation(HoldBackupDocument)
  // The backups whose hold is being changed: each one's Hold or Release button waits until the list is asked again (a set: two rows can
  // be held at once, and the first one's answer must not free the second's button).
  const [holding, setHolding] = useState<ReadonlySet<string>>(() => new Set())
  // The backups being deleted: their row's buttons are off until the list is asked again (a ref as well, so a second confirmation in
  // the same moment sends nothing either).
  const [deleting, setDeleting] = useState<ReadonlySet<string>>(() => new Set())
  const inFlight = useRef(new Set<string>())
  const heading = useRef<HTMLHeadingElement>(null)
  const focusAfterDelete = useRef(false)

  // Once no delete is under way any more: the row went, and with it the button that had the focus (or the confirmation gave the focus
  // back to that button while it was off), so the focus goes to the list's heading, not back to the start of the page.
  useEffect(() => {
    if (!focusAfterDelete.current || deleting.size > 0) return
    focusAfterDelete.current = false
    if (!document.activeElement || document.activeElement === document.body) heading.current?.focus()
  }, [backups.restorePoints, deleting])

  const remove = async (file: string) => {
    if (inFlight.current.has(file)) return
    inFlight.current.add(file)
    setDeleting(new Set(inFlight.current))
    setError(undefined)
    try {
      await deleteBackup({ variables: { file } })
      toast(t('admin.backups.deleted', { file }))
    } catch (e) {
      setError(e)
    }
    focusAfterDelete.current = true
    try {
      await onChanged() // also after a refusal: a file gone since the list leaves it
    } catch {
      // The list's own error says why it could not be asked again.
    }
    inFlight.current.delete(file)
    setDeleting(new Set(inFlight.current))
  }

  const hold = async (file: string, held: boolean) => {
    setHolding((files) => new Set(files).add(file))
    setError(undefined)
    try {
      await holdBackup({ variables: { file, hold: held } })
      toast(t(held ? 'admin.backups.heldToast' : 'admin.backups.releasedToast', { file }))
    } catch (e) {
      setError(e)
    }
    try {
      await onChanged()
    } catch {
      // The list's own error says why it could not be asked again.
    }
    setHolding((files) => {
      const left = new Set(files)
      left.delete(file)
      return left
    })
  }

  return (
    <section aria-labelledby="backups-points">
      <Typography ref={heading} id="backups-points" component="h2" variant="h5" tabIndex={-1} sx={{ mb: 1 }}>
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
              const busy = deleting.has(p.file)
              return (
                <TableRow key={p.file} aria-busy={busy || undefined}>
                  <TableCell component="th" scope="row">
                    <Typography variant="body2">{made}</Typography>
                    <Typography variant="caption" sx={{ color: 'text.secondary', wordBreak: 'break-all' }}>
                      {p.file}
                    </Typography>
                  </TableCell>
                  <TableCell>
                    <Stack direction="row" sx={{ gap: 0.5 }}>
                      {icons.kind(p)}
                      {icons.held(p)}
                    </Stack>
                  </TableCell>
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
                    {/* What its restore can break here, before anyone opens it (#140). */}
                    {(p.sameMigration || p.olderMigration) && <RestoreRiskLines point={p} />}
                  </TableCell>
                  <TableCell align="right">{number(p.rows)}</TableCell>
                  <TableCell>{icons.encryption(p)}</TableCell>
                  <TableCell>{icons.photos(p)}</TableCell>
                  <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                    {bytes(p.bytes)}
                  </TableCell>
                  <TableCell align="right">
                    <Stack direction="row" sx={{ gap: 1, justifyContent: 'flex-end', flexWrap: 'wrap' }}>
                      <BackupDetailsDialog
                        file={p.file}
                        createdAt={p.createdAt}
                        trigger={
                          <Button size="small" variant="soft" aria-label={t('admin.backups.openAria', { made, file: p.file })}>
                            <FolderOpen size={16} aria-hidden />
                            {t('admin.backups.open')}
                          </Button>
                        }
                      />
                      <Button
                        size="small"
                        variant="soft"
                        disabled={busy}
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
                            <Button size="small" variant="soft" color="error" disabled={busy} aria-label={t('admin.backups.restoreAria', { made, file: p.file })}>
                              <RotateCcw size={16} aria-hidden />
                              {t('admin.backups.restore')}
                            </Button>
                          }
                        />
                      )}
                      <Button
                        size="small"
                        variant="soft"
                        disabled={busy}
                        loading={holding.has(p.file)}
                        aria-label={t(p.held ? 'admin.backups.releaseHoldAria' : 'admin.backups.holdAria', { made, file: p.file })}
                        onClick={() => void hold(p.file, !p.held)}
                      >
                        {p.held ? <PinOff size={16} aria-hidden /> : <Pin size={16} aria-hidden />}
                        {t(p.held ? 'admin.backups.releaseHold' : 'admin.backups.hold')}
                      </Button>
                      {/* A held backup is never deleted (the server refuses it too): it is released first. */}
                      {p.held ? (
                        <Typography variant="caption" sx={{ color: 'text.secondary', alignSelf: 'center' }}>
                          {t('admin.backups.deleteHeld')}
                        </Typography>
                      ) : (
                        <ConfirmDialog
                          title={t('admin.backups.deleteTitle', { made, file: p.file })}
                          description={t('admin.backups.deleteDescription')}
                          confirmLabel={t('admin.backups.deleteConfirm')}
                          onConfirm={() => void remove(p.file)}
                          trigger={
                            <Button size="small" variant="soft" color="error" loading={busy} disabled={holding.has(p.file)} aria-label={t('admin.backups.deleteAria', { made, file: p.file })}>
                              <Trash2 size={16} aria-hidden />
                              {t('admin.backups.delete')}
                            </Button>
                          }
                        />
                      )}
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
