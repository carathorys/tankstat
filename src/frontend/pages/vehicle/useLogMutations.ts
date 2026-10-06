import { useMutation } from '@apollo/client/react'
import { savedFrom, type Saved } from '../../components/usePhotoQueue.ts'
import type { ExpenseValues } from '../../ExpenseFormDialog.tsx'
import { AddExpenseDocument, LogRefuelingDocument } from '../../gql/generated.ts'
import type { RefuelingValues } from '../../RefuelingFormDialog.tsx'

/** What a new, changed or trashed refuelling or expense makes the vehicle page ask again (only the queries that are showing). */
export const REFUELING_QUERIES = ['Refuelings', 'VehicleDetails', 'LogDefaults']
export const EXPENSE_QUERIES = ['Expenses', 'VehicleDetails', 'LogDefaults', 'ExpenseCategories']

/**
 * Adding a refuelling or an expense on the vehicle page, from its tabs and from the floating add button alike: once saved, the lists and
 * the vehicle's figures on the page are asked again (a tab that is not showing asks when it is opened).
 */
export function useLogMutations(vehicleId: string) {
  const [logRefueling] = useMutation(LogRefuelingDocument, { refetchQueries: REFUELING_QUERIES, awaitRefetchQueries: true })
  const [addExpense] = useMutation(AddExpenseDocument, { refetchQueries: EXPENSE_QUERIES, awaitRefetchQueries: true })
  return {
    refuel: async (values: RefuelingValues, photoIds: string[]): Promise<Saved> =>
      savedFrom((await logRefueling({ variables: { input: { ...values, vehicleId, photoIds } } })).data?.logRefueling),
    expense: async (values: ExpenseValues, photoIds: string[]): Promise<Saved> =>
      savedFrom((await addExpense({ variables: { input: { ...values, vehicleId, photoIds } } })).data?.addExpense),
  }
}
