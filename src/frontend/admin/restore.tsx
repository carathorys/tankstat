import { useApolloClient, useMutation } from '@apollo/client/react'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import FormControlLabel from '@mui/material/FormControlLabel'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Segmented } from '../components/Segmented.tsx'
import { DialogButtons, DialogCancel, DialogFrame } from '../dialogs/DialogFrame.tsx'
import { DialogTrigger } from '../dialogs/DialogTrigger.tsx'
import { useDialogState } from '../dialogs/useDialogState.ts'
import { Field } from '../forms/Field.tsx'
import { FieldInput } from '../forms/FieldInput.tsx'
import { Form } from '../forms/Form.tsx'
import { RestoreBackupDocument, SessionDocument, type RestoreTargetFieldsFragment } from '../gql/generated.ts'
import { useFormat } from '../i18n/format.ts'
import { ErrorMessage, WarningMessage } from '../messages.tsx'
import { navigation } from '../navigation.ts'
import { classifyFailure } from '../offline/errors.ts'
import { PROBE_TIMEOUT_MS, withDeadline } from '../offline/runtime.ts'
import { restoreRisks, type RestoreRisk } from './restoreRisks.ts'

/** How often the page asks how a backup that is being written is getting on, and whether the server is back after a restore. */
export const BACKUP_POLL_MS = 2_000

/** After this long without the server back, the page says what may keep it away (no restart policy, a refused restore). */
export const RESTART_SLOW_MS = 60_000

/** What a restore needs of a restore point (`RestoreTargetFields`), as the Backups page and the setup page list them. */
type RestoreTarget = RestoreTargetFieldsFragment

/**
 * The confirmation of a restore, opened by its row's button, with the passphrase (or the identity) of an encrypted backup. It names the
 * file as well as the time, since two backups can share a minute (a copy of one, a safety net written just after it). Nothing is written
 * when the key is wrong. `code`: the one-time code from the server's log, while the setup waits (no administrator exists to ask).
 */
export function RestoreDialog({
  point,
  trigger,
  code,
  onRestoring,
}: {
  point: RestoreTarget
  trigger: ReactNode
  code?: string
  onRestoring: (point: RestoreTarget) => void
}) {
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
        <RestoreForm point={point} code={code} busy={busy} onBusy={setBusy} onRestoring={onRestoring} />
      </DialogFrame>
    </>
  )
}

/**
 * A backup's risks in its row, before anyone opens its restore (#140): a line each, said in words ("Refused", "Risk") and not by colour alone,
 * the dialog then says each in full.
 */
export function RestoreRiskLines({ point }: { point: RestoreTarget }) {
  const { t } = useTranslation()
  return restoreRisks(point).map((risk) => (
    <Typography key={risk.key} variant="caption" sx={{ display: 'block', color: risk.tier === 'blocker' ? 'error.main' : 'warning.softText', minWidth: '12rem' }}>
      {t(risk.tier === 'blocker' ? 'admin.backups.riskRefused' : 'admin.backups.riskRisk')}: {t(`admin.backups.risks.${risk.key}.short`, { names: risk.names })}
    </Typography>
  ))
}

/**
 * What the backup brings of its server's settings, and what its restore can break here, before anyone accepts it (#140): its settings file and
 * the variables its server's environment set, then what refuses the restore (set it first) and the risks it carries, each in full.
 */
function RestoreSettingsNote({ point, risks }: { point: RestoreTarget; risks: RestoreRisk[] }) {
  const { t } = useTranslation()
  const environment = point.environment
  const names = environment?.settings.join(', ') ?? ''
  const blockers = risks.filter((r) => r.tier === 'blocker')
  const accepted = risks.filter((r) => r.tier === 'risk')
  // Merged, but no file went in (a plain backup whose environment set only secrets): the values are not in the backup.
  const merged = environment?.mode === 'MERGE' && point.settingsFile?.state === 'INCLUDED'
  return (
    <>
      {point.settingsFile?.state === 'INCLUDED' && <Typography variant="body2">{t('admin.backups.settingsBrings')}</Typography>}
      {environment && environment.settings.length > 0 && (
        <Typography variant="body2">{merged ? t('admin.backups.environmentMerged', { names }) : t('admin.backups.environmentRecorded', { names })}</Typography>
      )}
      {blockers.map((risk) => (
        <Alert key={risk.key} severity="error" role="alert">
          {t(`admin.backups.risks.${risk.key}.text`, { names: risk.names })} {t('admin.backups.refusedNote')}
        </Alert>
      ))}
      {accepted.length > 0 && (
        <WarningMessage>
          <Typography variant="body2" sx={{ fontWeight: 600 }}>
            {t('admin.backups.risksTitle')}
          </Typography>
          <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
            {accepted.map((risk) => (
              <li key={risk.key}>{t(`admin.backups.risks.${risk.key}.text`, { names: risk.names })}</li>
            ))}
          </Box>
        </WarningMessage>
      )}
    </>
  )
}

function RestoreForm({
  point,
  code,
  busy,
  onBusy,
  onRestoring,
}: {
  point: RestoreTarget
  code?: string
  busy: boolean
  onBusy: (busy: boolean) => void
  onRestoring: (point: RestoreTarget) => void
}) {
  const { t } = useTranslation()
  const [restoreBackup] = useMutation(RestoreBackupDocument, { fetchPolicy: 'no-cache' })
  const [key, setKey] = useState<'passphrase' | 'identity'>('passphrase')
  const [error, setError] = useState<unknown>()
  const risks = restoreRisks(point)
  const refused = risks.some((r) => r.tier === 'blocker')
  const risky = risks.some((r) => r.tier === 'risk')
  // Accepted knowingly: what the restore can break is said above, and the button waits for this.
  const [understood, setUnderstood] = useState(false)

  const submit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const secret = String(new FormData(e.currentTarget).get('secret') ?? '')
    onBusy(true)
    setError(undefined)
    try {
      await restoreBackup({
        variables: {
          input: {
            file: point.file,
            passphrase: point.encrypted && key === 'passphrase' ? secret : null,
            identity: point.encrypted && key === 'identity' ? secret.trim() : null,
            ...(code ? { code } : {}),
          },
        },
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
    <Form onSubmit={(e) => void submit(e)} refusal={error}>
      <Stack sx={{ gap: 1.5 }}>
        <RestoreSettingsNote point={point} risks={risks} />
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
        {risky && !refused && (
          <FormControlLabel
            control={<Checkbox checked={understood} disabled={busy} onChange={(e) => setUnderstood(e.target.checked)} />}
            label={t('admin.backups.risksAcknowledge')}
          />
        )}
        {error !== undefined && <ErrorMessage error={error} />}
        <DialogButtons>
          <DialogCancel autoFocus={!point.encrypted || refused} disabled={busy} />
          <Button type="submit" color="error" loading={busy} disabled={refused || (risky && !understood)}>
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
 * waiting. A server back without the restore (it was refused at start) and one that stays away are said so; `notRestored` is shown
 * beside that message (the setup page's way back to its code, which the restart made anew).
 */
export function RestartWait({ point, generation, notRestored }: { point: RestoreTarget; generation: string | undefined; notRestored?: ReactNode }) {
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

  if (state === 'notRestored')
    return (
      <Stack sx={{ gap: 1.5 }}>
        <WarningMessage>{t('admin.backups.notRestored')}</WarningMessage>
        {notRestored}
      </Stack>
    )
  return (
    <Stack ref={shown} tabIndex={-1} role="status" sx={{ gap: 1 }}>
      <Typography className="tk-pulse">{t('admin.backups.restarting', { made: dateTime(point.createdAt) })}</Typography>
      {state === 'slow' && <Typography variant="body2" sx={{ color: 'text.secondary' }}>{t('admin.backups.restartSlow')}</Typography>}
    </Stack>
  )
}
