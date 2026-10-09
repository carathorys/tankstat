import { useMutation, useQuery } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'
import { useEffect, useId, useState, useSyncExternalStore } from 'react'
import { useTranslation } from 'react-i18next'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { Loading } from '../../components/Loading.tsx'
import { SurfaceTable } from '../../components/SurfaceTable.tsx'
import { visuallyHidden } from '../../components/visuallyHidden.ts'
import { DialogButtons, DialogCancel, DialogFrame } from '../../dialogs/DialogFrame.tsx'
import { DialogTrigger } from '../../dialogs/DialogTrigger.tsx'
import { useDialogState } from '../../dialogs/useDialogState.ts'
import { OfflineEstimatesDocument, OfflineSettingsDocument, OfflineVehiclesDocument, UpdateOfflineSettingsDocument } from '../../gql/generated.ts'
import { useFormat } from '../../i18n/format.ts'
import { useOfflineReady } from '../../pwa/offlineReady.ts'
import { ErrorMessage } from '../../messages.tsx'
import { deviceData } from '../../offline/deviceData.ts'
import { DEFAULT_RULE, fromDate } from '../../offline/offlineWindow.ts'
import { LAST_PULL_KEY, type PullEngine, type PullState } from '../../offline/pull.ts'
import { offlineDownload } from '../../offline/runtime.ts'
import { useConnectivity } from '../../offline/useConnectivity.ts'
import { useToast } from '../../toast/toastContext.ts'
import { describeWindow } from './describeWindow.ts'
import { WindowPicker } from './WindowPicker.tsx'

interface Draft {
  defaultWindow: string
  vehicles: Map<string, string>
}

/**
 * What this device downloads for offline use: the window for every vehicle and the vehicles that differ, with how many entries each
 * would bring (`logCountSince`), saved with the account; and this device's side: how much the site stores, when it last downloaded,
 * Download now, and removing what it keeps. Choosing needs the server; the device's side works offline too.
 */
export function OfflineDataPanel() {
  const { t } = useTranslation()
  const heading = useId()
  const { reachable } = useConnectivity()
  const settings = useQuery(OfflineSettingsDocument, { fetchPolicy: 'cache-and-network' })
  const vehicles = useQuery(OfflineVehiclesDocument, { fetchPolicy: 'cache-and-network' })

  return (
    <Stack component="section" aria-labelledby={heading} sx={{ gap: 1.5, mt: 3 }}>
      <div>
        <Typography component="h2" variant="h5" id={heading} sx={{ mb: 0.5 }}>
          {t('account.offline.title')}
        </Typography>
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {t('account.offline.description')}
        </Typography>
      </div>
      {!reachable && <Typography variant="body2">{t('account.offline.needsServer')}</Typography>}
      {reachable && settings.error && !settings.data && <ErrorMessage error={settings.error} />}
      {reachable && !settings.data && !settings.error && <Loading />}
      {reachable && settings.data && vehicles.data && (
        // A new answer of the server starts the editor afresh (after a save, or on another device's change seen on return).
        <WindowEditor key={JSON.stringify(settings.data.offlineSettings)} saved={settings.data.offlineSettings} vehicles={vehicles.data.myVehicles} />
      )}
      <DeviceSide />
    </Stack>
  )
}

function WindowEditor({ saved, vehicles }: { saved: { defaultWindow: string; vehicles: readonly { vehicleId: string; window: string }[] }; vehicles: readonly { id: string; name: string }[] }) {
  const { t } = useTranslation()
  const format = useFormat()
  const { toast } = useToast()
  // Only the windows of vehicles listed here: one the user can no longer see is left out of the set saved (the server would refuse it).
  const [own] = useState(() => saved.vehicles.filter((v) => vehicles.some((vehicle) => vehicle.id === v.vehicleId)))
  const [draft, setDraft] = useState<Draft>(() => ({ defaultWindow: saved.defaultWindow || DEFAULT_RULE, vehicles: new Map(own.map((v) => [v.vehicleId, v.window])) }))
  const [defaultValid, setDefaultValid] = useState(true)
  // Not awaited: a refresh that fails after the save must never make the save look failed (the answer below starts the editor afresh).
  const [save, { loading: saving }] = useMutation(UpdateOfflineSettingsDocument, { refetchQueries: ['OfflineSettings'] })
  const [error, setError] = useState<unknown>()
  const describe = (rule: string) => describeWindow(rule, t, format)
  const changed = draft.defaultWindow !== saved.defaultWindow || draft.vehicles.size !== own.length || own.some((v) => draft.vehicles.get(v.vehicleId) !== v.window)

  async function submit() {
    setError(undefined)
    try {
      await save({ variables: { input: { defaultWindow: draft.defaultWindow, vehicles: [...draft.vehicles].map(([vehicleId, window]) => ({ vehicleId, window })) } } })
      toast(t('account.offline.saved'))
      void offlineDownload()
        ?.then((engine) => engine.run()) // the vehicles whose window changed download now
        .catch((error: unknown) => console.warn('The offline download could not be loaded; the next one brings the new window.', error))
    } catch (e) {
      setError(e)
    }
  }

  return (
    <Stack sx={{ gap: 1.5 }}>
      <WindowPicker
        label={t('account.offline.defaultWindow')}
        rule={draft.defaultWindow}
        onChange={(rule) => {
          setDefaultValid(rule !== null)
          if (rule) setDraft({ ...draft, defaultWindow: rule })
        }}
      />
      <Typography variant="body2" sx={{ color: 'text.secondary' }}>
        {t('account.offline.estimateHint')}
      </Typography>
      {vehicles.length > 0 && (
        <SurfaceTable caption={t('account.offline.vehicles')}>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>{t('account.offline.vehicle')}</TableCell>
                <TableCell>{t('account.offline.window')}</TableCell>
                <TableCell align="right">{t('account.offline.estimate')}</TableCell>
                <TableCell>
                  <span style={visuallyHidden}>{t('account.offline.change')}</span>
                </TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {vehicles.map((vehicle) => {
                const own = draft.vehicles.get(vehicle.id)
                const rule = own ?? draft.defaultWindow
                return (
                  <TableRow key={vehicle.id}>
                    <TableCell component="th" scope="row">
                      {vehicle.name}
                    </TableCell>
                    <TableCell>{own ? describe(own) : t('account.offline.followsDefault', { window: describe(draft.defaultWindow) })}</TableCell>
                    <TableCell align="right">
                      <Estimate vehicleId={vehicle.id} rule={rule} />
                    </TableCell>
                    <TableCell align="right">
                      <VehicleWindowDialog
                        name={vehicle.name}
                        rule={own ?? 'default'}
                        onApply={(chosen) => {
                          const next = new Map(draft.vehicles)
                          if (chosen === 'default') next.delete(vehicle.id)
                          else next.set(vehicle.id, chosen)
                          setDraft({ ...draft, vehicles: next })
                        }}
                      />
                    </TableCell>
                  </TableRow>
                )
              })}
            </TableBody>
          </Table>
        </SurfaceTable>
      )}
      {error !== undefined && <ErrorMessage error={error} />}
      <Stack direction="row" sx={{ gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
        <Button variant="contained" size="large" loading={saving} disabled={!changed || !defaultValid} onClick={() => void submit()}>
          {t('account.offline.save')}
        </Button>
        {changed && <Typography variant="body2">{t('account.offline.notSavedYet')}</Typography>}
      </Stack>
    </Stack>
  )
}

/** About how many entries a vehicle's window brings: one request per start date on the page, shared by every vehicle with that date. */
function Estimate({ vehicleId, rule }: { vehicleId: string; rule: string }) {
  const { t } = useTranslation()
  const { number } = useFormat()
  const from = fromDate(rule)
  const { data } = useQuery(OfflineEstimatesDocument, { variables: { from: from || null }, skip: from === false, fetchPolicy: 'cache-first' })
  if (from === false) return <>{t('account.offline.estimateValue', { entries: number(0) })}</>
  const counts = data?.myVehicles.find((v) => v.id === vehicleId)?.logCountSince
  if (!counts) return <span aria-hidden>…</span>
  return <>{t('account.offline.estimateValue', { entries: number(counts.refuelings + counts.expenses) })}</>
}

function VehicleWindowDialog({ name, rule, onApply }: { name: string; rule: string; onApply: (rule: string) => void }) {
  const { t } = useTranslation()
  const [open, setOpen] = useDialogState()
  const [chosen, setChosen] = useState<string | null>(rule)
  return (
    <>
      <DialogTrigger
        trigger={
          <Button variant="soft" size="large" aria-label={t('account.offline.changeAria', { vehicle: name })}>
            {t('account.offline.change')}
          </Button>
        }
        open={open}
        onOpen={() => {
          setChosen(rule)
          setOpen(true)
        }}
      />
      <DialogFrame open={open} onClose={() => setOpen(false)} title={t('account.offline.changeTitle', { vehicle: name })}>
        {open && <WindowPicker label={t('account.offline.kind')} rule={rule} withDefault onChange={setChosen} />}
        <DialogButtons>
          <DialogCancel />
          <Button
            variant="contained"
            disabled={chosen === null}
            onClick={() => {
              if (chosen) onApply(chosen)
              setOpen(false)
            }}
          >
            {t('account.offline.apply')}
          </Button>
        </DialogButtons>
      </DialogFrame>
    </>
  )
}

const IDLE: PullState = { status: 'idle', vehiclesDone: 0, vehiclesTotal: 0, lastPullAt: null, interrupted: false }

/** This device: what the site stores, when it last downloaded, Download now (with its progress) and removing what it keeps. */
function DeviceSide() {
  const { t } = useTranslation()
  const { dateTime, number } = useFormat()
  const heading = useId()
  const { reachable } = useConnectivity()
  const appKept = useOfflineReady()
  const [engine, setEngine] = useState<PullEngine | null>(null)
  const [lastPull, setLastPull] = useState<number | null>(null)
  const [usage, setUsage] = useState<number | null>(null)
  const [status, setStatus] = useState('')
  const [removedAt, setRemovedAt] = useState<number | null>(null)

  useEffect(() => {
    let live = true
    void offlineDownload()
      ?.then((e) => live && setEngine(e))
      .catch(() => undefined) // not loaded (not kept on this device yet, offline): no Download now until it is
    void deviceData.read(LAST_PULL_KEY).then((kept) => live && setLastPull(typeof kept?.data === 'number' ? kept.data : null))
    void navigator.storage?.estimate?.().then((e) => live && setUsage(e.usage ?? null)).catch(() => undefined)
    return () => {
      live = false
    }
  }, [])

  const state = useSyncExternalStore(
    (listener) => engine?.subscribe(listener) ?? (() => undefined),
    () => engine?.state ?? IDLE,
  )
  const pulling = state.status === 'pulling'
  // A download of this page's life counts unless what it brought was removed since.
  const last = (state.lastPullAt !== null && (removedAt === null || state.lastPullAt > removedAt) ? state.lastPullAt : null) ?? lastPull
  const progress = pulling
    ? t('account.offline.downloading', { done: state.vehiclesDone, total: state.vehiclesTotal })
    : state.interrupted
      ? t('account.offline.interrupted')
      : status

  return (
    <Stack component="section" aria-labelledby={heading} sx={{ gap: 1, mt: 1 }}>
      <Typography component="h3" variant="subtitle1" id={heading}>
        {t('account.offline.device')}
      </Typography>
      {usage !== null && (
        <Typography variant="body2">{t('account.offline.storage', { size: number(usage / 1_000_000, { style: 'unit', unit: 'megabyte', maximumFractionDigits: 1 }) })}</Typography>
      )}
      <Typography variant="body2">{last ? t('account.offline.lastDownloaded', { time: dateTime(new Date(last).toISOString()) }) : t('account.offline.neverDownloaded')}</Typography>
      {appKept === 'ready' && <Typography variant="body2">{t('account.offline.appReady')}</Typography>}
      {appKept === 'storing' && <Typography variant="body2">{t('account.offline.appStoring')}</Typography>}
      <Stack direction="row" sx={{ gap: 1.5, flexWrap: 'wrap' }}>
        {engine && (
          <Button
            variant="soft"
            size="large"
            loading={pulling}
            disabled={!reachable}
            onClick={() => {
              setStatus('')
              void engine.run().then(() => setStatus(engine.state.interrupted ? '' : t('account.offline.downloaded')))
            }}
          >
            {t('account.offline.downloadNow')}
          </Button>
        )}
        <ConfirmDialog
          trigger={
            // Not while a download runs: it would write its remaining pages (and a finished mark) into what was just removed.
            <Button variant="soft" color="error" size="large" disabled={pulling}>
              {t('account.offline.remove')}
            </Button>
          }
          title={t('account.offline.removeTitle')}
          description={t('account.offline.removeDescription')}
          confirmLabel={t('account.offline.remove')}
          onConfirm={() =>
            void deviceData.removeAll().then(() => {
              setLastPull(null)
              setRemovedAt(Date.now())
              setStatus(t('account.offline.removed'))
            })
          }
        />
      </Stack>
      <Typography variant="body2" role="status">
        {progress}
      </Typography>
    </Stack>
  )
}
