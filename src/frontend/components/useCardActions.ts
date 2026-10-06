import { useMutation } from '@apollo/client/react'
import type { ExpenseValues } from '../ExpenseFormDialog.tsx'
import { AddExpenseDocument, LogRefuelingDocument, MarkRecurringExpenseDoneDocument, VehicleCardDocument, type AccessLevel } from '../gql/generated.ts'
import type { DoneValues } from '../RecurringDoneDialog.tsx'
import type { RefuelingValues } from '../RefuelingFormDialog.tsx'
import type { Saved } from './usePhotoQueue.ts'

/** Who may add logs to a vehicle: the same rule the vehicle page applies to its tabs. */
export const canLogFor = (v: { canEdit: boolean; logAccess: AccessLevel }) => v.canEdit || v.logAccess === 'EDIT' || v.logAccess === 'DELETE'

/**
 * The quick actions of a home card. Each one asks the server again for this vehicle's card only (`VehicleCard`): Apollo merges it into the
 * normalised `Vehicle:<id>`, so the card updates in place and the pages the infinite scroll already loaded stay. Never refetch `Welcome` from
 * a card: it reruns the first page and drops the rest. The dialogs load `LogDefaults` afresh on every opening, so nothing else is refetched.
 */
export function useCardActions(vehicleId: string) {
  const refresh = { refetchQueries: [{ query: VehicleCardDocument, variables: { id: vehicleId } }], awaitRefetchQueries: true }
  const [logRefueling] = useMutation(LogRefuelingDocument, refresh)
  const [addExpense] = useMutation(AddExpenseDocument, refresh)
  const [markDone] = useMutation(MarkRecurringExpenseDoneDocument, refresh)
  return {
    refuel: async (values: RefuelingValues, photoIds: string[]): Promise<Saved> => {
      const logged = await logRefueling({ variables: { input: { ...values, vehicleId, photoIds } } })
      return logged.data ? { id: logged.data.logRefueling.id, photoCount: logged.data.logRefueling.photos.length } : undefined
    },
    expense: async (values: ExpenseValues, photoIds: string[]): Promise<Saved> => {
      const added = await addExpense({ variables: { input: { ...values, vehicleId, photoIds } } })
      return added.data ? { id: added.data.addExpense.id, photoCount: added.data.addExpense.photos.length } : undefined
    },
    done: (scheduleId: string, values: DoneValues) => markDone({ variables: { input: { ...values, id: scheduleId } } }),
  }
}
