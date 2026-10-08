import { useApolloClient } from '@apollo/client/react'
import type { ChangeEntity } from './changes.ts'
import type { ChangeDraft } from './outbox.ts'
import { submitChange, type Submitted } from './submitChange.ts'
import { uuidV4 } from './uuid.ts'

/**
 * Changes of a vehicle's refuellings, expenses or schedules (or of the vehicle itself) through `submitChange`: kept on the device while the
 * server is out of reach, sent as always otherwise. `version` is the one the screen showed: the change was made from it.
 */
export function useLogChange(entity: ChangeEntity, vehicleId: string) {
  const client = useApolloClient()
  const submit = <T>(change: Omit<ChangeDraft, 'entity' | 'vehicleId'>, send: () => Promise<T>): Promise<Submitted<T>> =>
    submitChange(client, { ...change, entity, vehicleId }, send)
  return {
    add: <T>(id: string, input: Record<string, unknown>, send: () => Promise<T>) => submit({ id, action: 'add', targetId: id, input }, send),
    update: <T>(id: string, version: number | undefined, input: Record<string, unknown>, send: () => Promise<T>) =>
      submit({ id: uuidV4(), action: 'update', targetId: id, input, expectedVersion: version }, send),
    trash: <T>(id: string, version: number | undefined, send: () => Promise<T>) => submit({ id: uuidV4(), action: 'trash', targetId: id, expectedVersion: version }, send),
    restore: <T>(id: string, version: number | undefined, send: () => Promise<T>) => submit({ id: uuidV4(), action: 'restore', targetId: id, expectedVersion: version }, send),
    /** A service visit of schedules (`entity` 'recurring'): one change for all of them; it names the expense it logs, if any. */
    markDone: <T>(input: { ids: string[]; expenseId?: string } & Record<string, unknown>, send: () => Promise<T>) => {
      const id = uuidV4()
      return submit({ id, action: 'markDone', targetId: input.expenseId ?? id, targetIds: input.ids, input }, send)
    },
  }
}

/** `submitChange` for a change whose vehicle is known only when it is made (a new vehicle, a row of the trash). */
export function useSubmitChange() {
  const client = useApolloClient()
  return <T>(change: ChangeDraft, send: () => Promise<T>) => submitChange(client, change, send)
}
