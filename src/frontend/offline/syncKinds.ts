import type { ParkedChangeFieldsFragment, SyncChangeKind } from '../gql/generated.ts'
import type { Change, ChangeAction, ChangeEntity } from './changes.ts'

/** What a change the server knows by its kind (LOG_REFUELING, ...) is on this device: the entity and the action, as the outbox says. */
export const SYNC_KINDS: Record<SyncChangeKind, [ChangeEntity, ChangeAction]> = {
  LOG_REFUELING: ['refuelings', 'add'],
  UPDATE_REFUELING: ['refuelings', 'update'],
  DELETE_REFUELING: ['refuelings', 'trash'],
  RESTORE_REFUELING: ['refuelings', 'restore'],
  ADD_EXPENSE: ['expenses', 'add'],
  UPDATE_EXPENSE: ['expenses', 'update'],
  DELETE_EXPENSE: ['expenses', 'trash'],
  RESTORE_EXPENSE: ['expenses', 'restore'],
  ADD_RECURRING_EXPENSE: ['recurring', 'add'],
  UPDATE_RECURRING_EXPENSE: ['recurring', 'update'],
  DELETE_RECURRING_EXPENSE: ['recurring', 'trash'],
  MARK_RECURRING_EXPENSES_DONE: ['recurring', 'markDone'],
  ADD_VEHICLE: ['vehicles', 'add'],
  UPDATE_VEHICLE: ['vehicles', 'update'],
  DELETE_VEHICLE: ['vehicles', 'trash'],
  RESTORE_VEHICLE: ['vehicles', 'restore'],
}

/** The ChangeInput field of each kind (LOG_REFUELING: logRefueling). */
const fieldOf = (kind: SyncChangeKind) => kind.toLowerCase().replace(/_([a-z])/g, (_, c: string) => c.toUpperCase())

/** The key of the words for a kind of change (`sync.kinds.refuelings.add`: "New refuelling"). */
export const syncKindKey = (entity: ChangeEntity, action: ChangeAction) => `sync.kinds.${entity}.${action}` as 'sync.kinds.refuelings.add'

export const kindKeyOf = (kind: string): ReturnType<typeof syncKindKey> | null =>
  kind in SYNC_KINDS ? syncKindKey(...SYNC_KINDS[kind as SyncChangeKind]) : null

/**
 * A change the server parked, as a change of this device (the inverse of `push.ts` `toChangeInput`), so it is named and shown like one
 * that waits here. `change` is the ChangeInput as the server keeps it (JSON; absent operations left out or null).
 */
export function fromParked(parked: Pick<ParkedChangeFieldsFragment, 'id' | 'kind' | 'vehicleId' | 'targetId' | 'change' | 'receivedAt'>): Change {
  const [entity, action] = SYNC_KINDS[parked.kind]
  let envelope: Record<string, unknown> = {}
  try {
    envelope = JSON.parse(parked.change) as Record<string, unknown>
  } catch {
    // shown by its kind alone
  }
  const operation = envelope[fieldOf(parked.kind)]
  const input = operation && typeof operation === 'object' ? (operation as Record<string, unknown>) : undefined
  const targetId = parked.targetId ?? (typeof input?.id === 'string' ? input.id : typeof operation === 'string' ? operation : parked.id)
  return {
    id: parked.id,
    seq: 0,
    createdAt: Date.parse(parked.receivedAt),
    entity,
    action,
    vehicleId: parked.vehicleId ?? (typeof input?.vehicleId === 'string' ? input.vehicleId : entity === 'vehicles' ? targetId : ''),
    targetId,
    ...(action === 'markDone' ? { targetIds: (input?.ids as string[] | undefined) ?? [] } : {}),
    ...(input ? { input } : {}),
    expectedVersion: typeof envelope.expectedVersion === 'number' ? envelope.expectedVersion : null,
  }
}

/**
 * Whether applying a parked change anyway can work: not when the sender's own try found what it changes gone, or they may not (those can
 * only be discarded). Someone else deciding (the owner of the vehicle) is not the sender: what the sender could no longer see (their
 * access ended) may well be there for them, so the server decides. Everything else is tried as they would online; a rule that still
 * refuses says so.
 */
export const canForce = (reasonKey: string | null | undefined, bySender = true) =>
  !!reasonKey && !reasonKey.startsWith('auth.') && (!bySender || !reasonKey.endsWith('.notFound'))
