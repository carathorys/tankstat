import { useApolloClient, useMutation } from '@apollo/client/react'
import type { ExpenseValues } from '../ExpenseFormDialog.tsx'
import { AddExpenseDocument, LogRefuelingDocument, MarkRecurringExpensesDoneDocument, VehicleCardDocument } from '../gql/generated.ts'
import type { DoneValues } from '../RecurringDoneDialog.tsx'
import type { RefuelingValues } from '../RefuelingFormDialog.tsx'
import { useLogChange } from '../offline/useLogChange.ts'
import { uuidV4 } from '../offline/uuid.ts'
import { savedFrom, type Saved } from './usePhotoQueue.ts'

/**
 * The quick actions of a home card. After each one this vehicle's card alone is asked again (`VehicleCard`): Apollo merges the answer into
 * the normalised `Vehicle:<id>`, so the card updates in place and the pages the infinite scroll already loaded stay. Never refetch `Welcome`
 * from a card: it reruns the first page and drops the rest. The refresh comes after the mutation settled and cannot fail it: a saved log
 * must never look like an error in the dialog (a retry would log it twice). The dialogs load `LogDefaults` afresh on every opening, so
 * nothing else is refetched.
 */
export function useCardActions(vehicleId: string) {
  const client = useApolloClient()
  const [logRefueling] = useMutation(LogRefuelingDocument)
  const [addExpense] = useMutation(AddExpenseDocument)
  const [markDone] = useMutation(MarkRecurringExpensesDoneDocument)
  const refuelings = useLogChange('refuelings', vehicleId)
  const expenses = useLogChange('expenses', vehicleId)
  const refresh = () => client.query({ query: VehicleCardDocument, variables: { id: vehicleId }, fetchPolicy: 'network-only' }).then(() => undefined, () => undefined)
  return {
    // Kept on the device while the server is out of reach (`offline/submitChange.ts`); the card's figures are the server's, so a kept log
    // asks for nothing.
    refuel: async (values: RefuelingValues, photoIds: string[]): Promise<Saved> => {
      const input = { ...values, id: values.id ?? uuidV4(), vehicleId, photoIds }
      const done = await refuelings.add(input.id, input, () => logRefueling({ variables: { input } }))
      if (done.queued) return { id: input.id, photoCount: photoIds.length, queued: true }
      await refresh()
      return savedFrom(done.result.data?.logRefueling)
    },
    expense: async (values: ExpenseValues, photoIds: string[]): Promise<Saved> => {
      const input = { ...values, id: values.id ?? uuidV4(), vehicleId, photoIds }
      const done = await expenses.add(input.id, input, () => addExpense({ variables: { input } }))
      if (done.queued) return { id: input.id, photoCount: photoIds.length, queued: true }
      await refresh()
      return savedFrom(done.result.data?.addExpense)
    },
    done: async (values: DoneValues, photoIds: string[]): Promise<Saved> => {
      const done = await markDone({ variables: { input: { ...values, photoIds } } })
      await refresh()
      return savedFrom(done.data?.markRecurringExpensesDone.expense)
    },
  }
}
