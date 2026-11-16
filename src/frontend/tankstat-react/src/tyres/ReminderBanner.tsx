import Alert from '@mui/material/Alert'
import AlertTitle from '@mui/material/AlertTitle'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import { ArrowLeftRight } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { DistanceUnit } from '../gql/generated.ts'
import { useFormat } from '../i18n/format.ts'
import { ErrorMessage } from '../messages.tsx'
import { SwapDialog } from './SwapDialog.tsx'
import { reminderMonthOf } from './tyreMonths.ts'
import type { Tyres, TyreSetInfo } from './tyres.ts'
import { useReminderDismiss } from './useReminderDismiss.ts'

/**
 * The swap reminder on the Tyres tab (#292): a note per set in storage whose month has come, above the sets, said in words (a status
 * region, nothing by colour alone). Whoever may log gets *Swap tyres* (the swap dialog with the set chosen) and *Not this season*;
 * anyone else only reads it. Whether a set is due is the server's answer (`TyreSetInfo.reminder`), cleared on the device by a waiting swap
 * or dismissal (`replay.ts`).
 */
export function ReminderBanners({ tyres, vehicleId, unit, canLog }: { tyres: Tyres; vehicleId: string; unit: DistanceUnit; canLog: boolean }) {
  const due = tyres.sets.filter((s) => s.reminder && !s.onTheCar && !s.set.isRetired)
  // A note that leaves (dismissed, or its set swapped on) takes the focused button with it: the focus goes to its set's card, which now
  // says what became of the reminder, instead of falling off the page.
  const ids = due.map((s) => s.set.id).join(' ')
  const shown = useRef<string[]>([])
  useEffect(() => {
    const now = ids ? ids.split(' ') : []
    const gone = shown.current.filter((id) => !now.includes(id))
    shown.current = now
    if (gone.length === 0 || (document.activeElement && document.activeElement !== document.body)) return
    document.querySelector<HTMLElement>(`[data-set-card="${gone[0]}"]`)?.focus()
  }, [ids])
  return (
    <div role="status">
      {due.length > 0 && (
        <Stack sx={{ gap: 1 }}>
          {due.map((info) => (
            <ReminderBanner key={info.set.id} info={info} tyres={tyres} vehicleId={vehicleId} unit={unit} canLog={canLog} />
          ))}
        </Stack>
      )}
    </div>
  )
}

function ReminderBanner({ info, tyres, vehicleId, unit, canLog }: { info: TyreSetInfo; tyres: Tyres; vehicleId: string; unit: DistanceUnit; canLog: boolean }) {
  const { t } = useTranslation()
  const format = useFormat()
  const dismiss = useReminderDismiss(vehicleId)
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)
  const month = reminderMonthOf(info.set)
  const { set } = info
  return (
    <Alert
      severity="info"
      icon={<ArrowLeftRight size={20} aria-hidden />}
      data-reminder={set.id}
      sx={{ alignItems: 'flex-start', '& .MuiAlert-message': { minWidth: 0, flex: 1 } }}
    >
      <AlertTitle>{t(`tyres.reminder.title.${set.season}`)}</AlertTitle>
      {t('tyres.reminder.text', { set: set.name, month: month ? format.month(month) : '' })}
      {canLog && (
        <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap', mt: 1 }}>
          <SwapDialog
            vehicleId={vehicleId}
            unit={unit}
            tyres={tyres}
            setId={set.id}
            trigger={
              <Button size="small" aria-label={t('tyres.reminder.swapTyresAria', { set: set.name })}>
                {t('tyres.swap')}
              </Button>
            }
          />
          <Button
            size="small"
            variant="soft"
            disabled={busy}
            aria-label={t('tyres.reminder.dismissAria', { set: set.name })}
            onClick={async () => {
              setBusy(true)
              setError(undefined)
              try {
                await dismiss(set.id, info.reminder!.season)
              } catch (err) {
                setError(err)
              } finally {
                setBusy(false)
              }
            }}
          >
            {t('tyres.reminder.dismiss')}
          </Button>
        </Stack>
      )}
      {error !== undefined && <ErrorMessage error={error} />}
    </Alert>
  )
}
