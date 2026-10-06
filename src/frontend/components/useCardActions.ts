import { useApolloClient, useMutation } from '@apollo/client/react'
import type { ExpenseValues } from '../ExpenseFormDialog.tsx'
import { AddExpenseDocument, LogRefuelingDocument, MarkRecurringExpenseDoneDocument, VehicleCardDocument } from '../gql/generated.ts'
import type { DoneValues } from '../RecurringDoneDialog.tsx'
import type { RefuelingValues } from '../RefuelingFormDialog.tsx'
import type { Saved } from './usePhotoQueue.ts'

/** What the photo session needs to know about a log that was just saved. */
const savedFrom = (log: { id: string; photos: readonly unknown[] } | undefined): Saved => (log ? { id: log.id, photoCount: log.photos.length } : undefined)

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
  const [markDone] = useMutation(MarkRecurringExpenseDoneDocument)
  const refresh = () => client.query({ query: VehicleCardDocument, variables: { id: vehicleId }, fetchPolicy: 'network-only' }).then(() => undefined, () => undefined)
  return {
    refuel: async (values: RefuelingValues, photoIds: string[]): Promise<Saved> => {
      const logged = await logRefueling({ variables: { input: { ...values, vehicleId, photoIds } } })
      await refresh()
      return savedFrom(logged.data?.logRefueling)
    },
    expense: async (values: ExpenseValues, photoIds: string[]): Promise<Saved> => {
      const added = await addExpense({ variables: { input: { ...values, vehicleId, photoIds } } })
      await refresh()
      return savedFrom(added.data?.addExpense)
    },
    done: async (scheduleId: string, values: DoneValues) => {
      await markDone({ variables: { input: { ...values, id: scheduleId } } })
      await refresh()
    },
  }
}
