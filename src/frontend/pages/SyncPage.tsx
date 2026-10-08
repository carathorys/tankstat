import { useApolloClient, useMutation } from '@apollo/client/react'
import Card from '@mui/material/Card'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Button from '@mui/material/Button'
import { ConfirmDialog } from '../components/ConfirmDialog.tsx'
import { Loading } from '../components/Loading.tsx'
import { ResolveSyncChangeDocument, type DistanceUnit, type ParkedChangeFieldsFragment, type SyncResolveAction, type VolumeUnit } from '../gql/generated.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { useFormat } from '../i18n/format.ts'
import { discardChange } from '../offline/submitChange.ts'
import { canForce, fromParked } from '../offline/syncKinds.ts'
import { useParkedChanges } from '../offline/useParkedChanges.ts'
import { useConnectivity } from '../offline/useConnectivity.ts'
import { usePushState } from '../offline/usePushState.ts'
import { useDescribed, useWaitingChanges, type WaitingChange } from '../offline/waitingChanges.ts'
import { useErrorText, useKeyText } from '../i18n/errors.ts'
import { canEdit } from '../offline/changes.ts'
import { EditChange } from './sync/EditChange.tsx'

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
  const { parked } = useParkedChanges()
  const parkedById = new Map(parked?.map((p) => [p.id, p]) ?? [])
  const notApplied = useDescribed(parked?.map(fromParked) ?? [], parked)
  const resolved = (message: string) => {
    setStatus(message)
    heading.current?.focus() // the item may be gone, its buttons with it
  }

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
            units={{ distance: group.distanceUnit as DistanceUnit | null, volume: group.volumeUnit as VolumeUnit | null }}
            items={group.items}
            onEdited={() => setStatus(t('sync.edited'))}
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
      {!reachable ? (
        <Typography variant="body2" sx={{ color: 'text.secondary', mt: 3 }}>
          {t('sync.parkedOffline')}
        </Typography>
      ) : (
        parked && parked.length > 0 && (
          <Stack component="section" aria-labelledby="not-applied" sx={{ gap: 1.5, mt: 3 }}>
            <div>
              <Typography id="not-applied" component="h2" variant="h5">
                {t('sync.notApplied')}
              </Typography>
              <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                {t('sync.notAppliedHint')}
              </Typography>
            </div>
            {notApplied?.map((group) => {
              // The server names the vehicle (the device may never have downloaded it).
              const vehicle = group.items.map((i) => parkedById.get(i.change.id)?.vehicle).find(Boolean)
              return (
                <Group
                  key={group.vehicleId}
                  name={vehicle?.name ?? group.vehicleName ?? t('sync.unknownVehicle')}
                  units={{
                    distance: (vehicle?.distanceUnit ?? group.distanceUnit) as DistanceUnit | null,
                    volume: (vehicle?.volumeUnit ?? group.volumeUnit) as VolumeUnit | null,
                  }}
                  items={group.items}
                  parked={parkedById}
                  onResolved={resolved}
                />
              )
            })}
          </Stack>
        )
      )}
    </section>
  )
}

function Group({
  name,
  units,
  items,
  onEdited,
  onRemoved,
  parked,
  onResolved,
}: {
  name: string
  units: Units
  items: WaitingChange[]
  onEdited?: () => void
  onRemoved?: () => void
  /** Changes the server parked: listed with why, who sent them, and Apply anyway / Discard instead of Remove. */
  parked?: Map<string, Parked>
  onResolved?: (message: string) => void
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
          <Item key={item.change.id} item={item} units={units} onEdited={onEdited} onRemoved={onRemoved} parked={parked?.get(item.change.id)} onResolved={onResolved} />
        ))}
      </Stack>
    </Stack>
  )
}

function Item({
  item,
  units,
  onEdited,
  onRemoved,
  parked,
  onResolved,
}: {
  item: WaitingChange
  units: Units
  onEdited?: () => void
  onRemoved?: () => void
  parked?: Parked
  onResolved?: (message: string) => void
}) {
  const { t } = useTranslation()
  const reasonText = useReasonText()
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
    item.volume != null && units.volume ? format.volume(item.volume, units.volume) : null,
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
            {parked?.reason && (
              // The same words as the refusal would have had online.
              <Typography variant="body2" sx={{ color: 'warning.main' }}>
                {reasonText(parked)}
              </Typography>
            )}
            <Typography variant="caption" sx={{ color: 'text.secondary' }}>
              {parked
                ? t('sync.sentBy', { name: parked.submittedBy?.displayName ?? t('sync.someone'), time: format.dateTime(parked.receivedAt) })
                : t('sync.savedAt', { time: format.dateTime(new Date(change.createdAt).toISOString()) })}
            </Typography>
          </div>
          <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
          {canEdit(change) && units.distance && (parked ? parked.canResolve && onResolved : onEdited) && (
            <EditChange
              change={change}
              units={{ distance: units.distance, volume: units.volume ?? 'LITERS' }}
              label={parts.length ? `${kind}, ${parts.join(', ')}` : kind}
              parked={parked}
              onDone={(message) => (parked ? onResolved!(message) : onEdited!())}
            />
          )}
          {parked && onResolved && <ParkedActions parked={parked} label={parts.length ? `${kind}, ${parts.join(', ')}` : kind} onResolved={onResolved} />}
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
        </Stack>
      </Card>
    </li>
  )
}

type Parked = ParkedChangeFieldsFragment

/** The vehicle's units, as far as the device or the server knows them. */
type Units = { distance: DistanceUnit | null; volume: VolumeUnit | null }

/** A parked change's reason in the words of the refusal online (its arguments too). */
function useReasonText() {
  const keyText = useKeyText()
  return (parked: Parked) =>
    parked.reason ? (keyText(parked.reason.key, Object.fromEntries(parked.reason.args.map((a) => [a.name, a.value]))) ?? parked.reason.key) : ''
}

/**
 * Deciding about a parked change: apply it anyway (as the person would online: every rule applies, the version it was made from does
 * not), or discard it. Only for whoever sent it or may change the vehicle; Apply only where it can work (what it changes still exists).
 */
function ParkedActions({ parked, label, onResolved }: { parked: Parked; label: string; onResolved: (message: string) => void }) {
  const { t } = useTranslation()
  const reasonText = useReasonText()
  const errorText = useErrorText()
  const [resolve] = useMutation(ResolveSyncChangeDocument)
  if (!parked.canResolve) return null

  const run = async (action: SyncResolveAction) => {
    try {
      await resolve({ variables: { input: { id: parked.id, action } }, refetchQueries: ['ParkedChanges'], awaitRefetchQueries: true })
      onResolved(t(action === 'APPLY' ? 'sync.appliedNow' : 'sync.discarded'))
    } catch (error) {
      // Refused again: it stays, with the new reason (asked afresh).
      onResolved(t('sync.stillNotApplied', { reason: errorText(error) }))
    }
  }

  return (
    <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
      {canForce(parked.reason?.key) && (
        <ConfirmDialog
          trigger={
            <Button variant="soft" size="large" aria-label={t('sync.applyAria', { change: label })}>
              {t('sync.apply')}
            </Button>
          }
          title={t('sync.applyTitle')}
          description={t('sync.applyDescription', { reason: reasonText(parked) })}
          confirmLabel={t('sync.apply')}
          onConfirm={() => void run('APPLY')}
        />
      )}
      <ConfirmDialog
        trigger={
          <Button variant="soft" color="error" size="large" aria-label={t('sync.discardAria', { change: label })}>
            {t('sync.discard')}
          </Button>
        }
        title={t('sync.discardTitle')}
        description={t('sync.discardDescription')}
        confirmLabel={t('sync.discard')}
        onConfirm={() => void run('DISCARD')}
      />
    </Stack>
  )
}
