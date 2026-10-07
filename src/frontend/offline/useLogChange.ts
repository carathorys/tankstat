import { useApolloClient } from '@apollo/client/react'
import type { LogEntity } from './changes.ts'
import type { ChangeDraft } from './outbox.ts'
import { submitChange, type Submitted } from './submitChange.ts'
import { uuidV4 } from './uuid.ts'

/**
 * Changes of refuellings and expenses through `submitChange`: kept on the device while the server is out of reach, sent as always
 * otherwise. `version` is the log's as the screen showed it: the change was made from it.
 */
export function useLogChange(entity: LogEntity, vehicleId: string) {
  const client = useApolloClient()
  const submit = <T>(change: Omit<ChangeDraft, 'entity' | 'vehicleId'>, send: () => Promise<T>): Promise<Submitted<T>> =>
    submitChange(client, { ...change, entity, vehicleId }, send)
  return {
    add: <T>(id: string, input: Record<string, unknown>, send: () => Promise<T>) => submit({ id, action: 'add', targetId: id, input }, send),
    update: <T>(id: string, version: number | undefined, input: Record<string, unknown>, send: () => Promise<T>) =>
      submit({ id: uuidV4(), action: 'update', targetId: id, input, expectedVersion: version }, send),
    trash: <T>(id: string, version: number | undefined, send: () => Promise<T>) => submit({ id: uuidV4(), action: 'trash', targetId: id, expectedVersion: version }, send),
    restore: <T>(id: string, version: number | undefined, send: () => Promise<T>) => submit({ id: uuidV4(), action: 'restore', targetId: id, expectedVersion: version }, send),
  }
}
