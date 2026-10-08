import type { ApolloClient } from '@apollo/client'
import { connectivity } from './connectivity.ts'
import { isConnectionFailure } from './errors.ts'
import type { ChangeEntity } from './changes.ts'
import { outbox, type ChangeDraft } from './outbox.ts'

export type Submitted<T> = { queued: true } | { queued: false; result: T }

/**
 * Every change of a refuelling or an expense goes through here. Kept on the device (`outbox.ts`) while the server is out of reach, when the
 * vehicle already has changes waiting (they keep their order), or when the request fails for want of the server (with the same id: should
 * the server have saved it after all, the add sent again later is answered with what it saved); otherwise sent as always, and an error
 * the server gave is the caller's to show. A kept change makes the screen ask again, so the device's own answers show it (`offlineLink.ts`).
 */
export async function submitChange<T>(client: ApolloClient, change: ChangeDraft, send: () => Promise<T>): Promise<Submitted<T>> {
  const keep = async (kept: ChangeDraft = change): Promise<Submitted<T>> => {
    await outbox.enqueue(kept)
    void client.refetchQueries({ include: 'active' }).catch(() => undefined)
    return { queued: true }
  }
  if (!connectivity.reachable || outbox.vehicleIds().has(change.vehicleId)) return keep(change)
  try {
    return { queued: false, result: await send() }
  } catch (error) {
    // The request may have reached the server (only the answer was lost): kept as sent, so nothing later folds into it.
    if (isConnectionFailure(error)) return keep({ ...change, sent: true })
    throw error
  }
}

/** Takes a waiting change back (it is never sent), and the screen shows what the server has again. */
export async function discardChange(client: ApolloClient, id: string): Promise<void> {
  await outbox.discard(id)
  void client.refetchQueries({ include: 'active' }).catch(() => undefined)
}

/** Keep: takes back the trash (or deletion) waiting for something, so it stays. */
export async function keepEntry(client: ApolloClient, entity: ChangeEntity, id: string): Promise<void> {
  const trash = outbox.changes.find((c) => c.entity === entity && c.action === 'trash' && c.targetId === id)
  if (trash) await discardChange(client, trash.id)
}
