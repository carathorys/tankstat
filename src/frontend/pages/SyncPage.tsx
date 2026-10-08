import { useApolloClient } from '@apollo/client/react'
import Card from '@mui/material/Card'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Button from '@mui/material/Button'
import { ConfirmDialog } from '../components/ConfirmDialog.tsx'
import { Loading } from '../components/Loading.tsx'
import type { VolumeUnit } from '../gql/generated.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { useFormat } from '../i18n/format.ts'
import { discardChange } from '../offline/submitChange.ts'
import type { ParkedChange } from '../offline/push.ts'
import { useConnectivity } from '../offline/useConnectivity.ts'
import { usePushState } from '../offline/usePushState.ts'
import { useDescribed, useWaitingChanges, type WaitingChange } from '../offline/waitingChanges.ts'
import { useKeyText } from '../i18n/errors.ts'

/**
 * Waiting to sync: the changes made on this device that have not reached the server, per vehicle, in the order they were made, each with
 * what it does, its values and when it was saved. A change can be taken back (it is never sent); a vehicle added here takes its entries
 * along.
 */
export function SyncPage() {
  const { t } = useTranslation()
  const { dateTime } = useFormat()
  usePageTitle(t('sync.title'))
  const groups = useWaitingChanges()
  const { reachable } = useConnectivity()
  const { engine, state } = usePushState()
  const heading = useRef<HTMLHeadingElement>(null)
  const [status, setStatus] = useState('')
  const last = state.last
  const notApplied = useDescribed(last?.parked.map((p) => p.change) ?? [], last?.at)
  const reasons = new Map(last?.parked.map((p) => [p.change.id, p]) ?? [])

  return (
    <section aria-labelledby="page-title">
      <Typography id="page-title" ref={heading} tabIndex={-1} component="h1" variant="h3" sx={{ mb: 1 }}>
        {t('sync.title')}
      </Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary', mb: 2 }}>
        {t('sync.description')}
      </Typography>
      <Stack direction="row" sx={{ gap: 1.5, alignItems: 'center', flexWrap: 'wrap', mb: 2 }}>
        {engine && (
          <Button size="large" loading={state.status === 'syncing'} disabled={!reachable || !groups?.length} onClick={() => void engine.run()}>
            {t('sync.syncNow')}
          </Button>
        )}
        <Typography variant="body2" role="status">
          {state.status === 'syncing'
            ? t('sync.syncing')
            : last
              ? last.interrupted
                ? t('sync.interrupted')
                : t('sync.lastSync', { time: dateTime(new Date(last.at).toISOString()), applied: last.applied, parked: last.parked.length })
              : ''}
        </Typography>
      </Stack>
      {!groups && <Loading />}
      {groups?.length === 0 && <Typography>{t('sync.empty')}</Typography>}
      <Stack sx={{ gap: 3 }}>
        {groups?.map((group) => (
          <Group
            key={group.vehicleId}
            name={group.vehicleName ?? t('sync.unknownVehicle')}
            volumeUnit={group.volumeUnit as VolumeUnit | null}
            items={group.items}
            onRemoved={() => {
              setStatus(t('sync.removed'))
              heading.current?.focus() // the button that had the focus is gone
            }}
          />
        ))}
      </Stack>
      <Typography variant="body2" role="status" sx={{ mt: 2 }}>
        {status}
      </Typography>
      {notApplied && notApplied.length > 0 && (
        <Stack component="section" aria-labelledby="not-applied" sx={{ gap: 1.5, mt: 3 }}>
          <div>
            <Typography id="not-applied" component="h2" variant="h5">
              {t('sync.notApplied')}
            </Typography>
            <Typography variant="body2" sx={{ color: 'text.secondary' }}>
              {t('sync.notAppliedHint')}
            </Typography>
          </div>
          {notApplied.map((group) => (
            <Group key={group.vehicleId} name={group.vehicleName ?? t('sync.unknownVehicle')} volumeUnit={group.volumeUnit as VolumeUnit | null} items={group.items} reasons={reasons} />
          ))}
        </Stack>
      )}
    </section>
  )
}

function Group({
  name,
  volumeUnit,
  items,
  onRemoved,
  reasons,
}: {
  name: string
  volumeUnit: VolumeUnit | null
  items: WaitingChange[]
  onRemoved?: () => void
  /** Changes the server did not apply, with why: listed with the reason instead of Remove. */
  reasons?: Map<string, ParkedChange>
}) {
  const { t } = useTranslation()
  const id = useId()
  return (
    <Stack component="section" aria-labelledby={id} sx={{ gap: 1 }}>
      <Typography component="h2" variant="h5" id={id}>
        {name}{' '}
        <Typography component="span" variant="body2" sx={{ color: 'text.secondary' }}>
          {t('sync.groupCount', { count: items.length })}
        </Typography>
      </Typography>
      <Stack component="ul" sx={{ gap: 1, listStyle: 'none', p: 0, m: 0 }}>
        {items.map((item) => (
          <Item key={item.change.id} item={item} volumeUnit={volumeUnit} onRemoved={onRemoved} parked={reasons?.get(item.change.id)} />
        ))}
      </Stack>
    </Stack>
  )
}

function Item({ item, volumeUnit, onRemoved, parked }: { item: WaitingChange; volumeUnit: VolumeUnit | null; onRemoved?: () => void; parked?: ParkedChange }) {
  const { t } = useTranslation()
  const keyText = useKeyText()
  const format = useFormat()
  const client = useApolloClient()
  const { change } = item
  // Every entity and action a change can have has its text (a schedule is deleted, not trashed; only schedules are marked done).
  const kind: string = t(`sync.kinds.${change.entity}.${change.action}` as 'sync.kinds.refuelings.add')

  // What the change carries (or what it changes), in a line: "1 Sep 2026 · 40 L · €60.00", "Oil change · €35.00", "Golf".
  const money = (amount: number | null | undefined) => (amount != null && item.currency ? format.money(amount, item.currency) : null)
  const parts = [
    item.date ? format.date(item.date) : null,
    item.name ?? item.title ?? null,
    item.titles?.filter(Boolean).length ? format.list(item.titles.filter(Boolean)) : null,
    item.volume != null && volumeUnit ? format.volume(item.volume, volumeUnit) : null,
    money(item.totalCost) ?? money(item.amount),
  ].filter((p): p is string => !!p)

  return (
    <li>
      <Card sx={{ p: 1.5 }}>
        <Stack direction="row" sx={{ gap: 1.5, alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap' }}>
          <div>
            <Typography component="h3" variant="subtitle1">
              {kind}
            </Typography>
            {parts.length > 0 && <Typography variant="body2">{parts.join(' · ')}</Typography>}
            {parked && (
              // The same words as the refusal would have had online.
              <Typography variant="body2" sx={{ color: 'warning.main' }}>
                {keyText(parked.key, parked.args) ?? parked.key}
              </Typography>
            )}
            <Typography variant="caption" sx={{ color: 'text.secondary' }}>
              {t('sync.savedAt', { time: format.dateTime(new Date(change.createdAt).toISOString()) })}
            </Typography>
          </div>
          {onRemoved && (
          <ConfirmDialog
            trigger={
              <Button variant="soft" color="error" size="large" aria-label={t('sync.removeAria', { change: parts.length ? `${kind}, ${parts.join(', ')}` : kind })}>
                {t('sync.remove')}
              </Button>
            }
            title={t('sync.removeTitle')}
            description={change.entity === 'vehicles' && change.action === 'add' ? t('sync.removeVehicleDescription') : t('sync.removeDescription')}
            confirmLabel={t('sync.remove')}
            onConfirm={() => void discardChange(client, change.id).then(onRemoved)}
          />
          )}
        </Stack>
      </Card>
    </li>
  )
}
