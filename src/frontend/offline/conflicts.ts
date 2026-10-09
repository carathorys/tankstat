import type { ApolloClient, DocumentNode } from '@apollo/client'
import {
  ExpenseChangeValuesFragmentDoc,
  RefuelingChangeValuesFragmentDoc,
  ScheduleChangeValuesFragmentDoc,
  VehicleChangeValuesFragmentDoc,
} from '../gql/generated.ts'
import { UPDATABLE, type ChangeEntity } from './changes.ts'

/** The type and the fragment of the values a change of each entity can set (`graphql/conflicts.graphql`). */
const SHAPES: Record<ChangeEntity, { typename: string; fragment: DocumentNode }> = {
  refuelings: { typename: 'Refueling', fragment: RefuelingChangeValuesFragmentDoc },
  expenses: { typename: 'Expense', fragment: ExpenseChangeValuesFragmentDoc },
  vehicles: { typename: 'Vehicle', fragment: VehicleChangeValuesFragmentDoc },
  recurring: { typename: 'RecurringExpenseInfo', fragment: ScheduleChangeValuesFragmentDoc },
}

export type ChangeValues = Record<string, unknown>

/**
 * What a change of `entity` can set, as a loaded entry holds it, in the shape of the change's input (the entries name their fields as the
 * inputs do; `units` without its `__typename`). Only the fields the entry holds: a grid row may lack some.
 */
export function valuesOf(entity: ChangeEntity, loaded: Record<string, unknown> | null | undefined): ChangeValues {
  if (!loaded) return {}
  return Object.fromEntries(
    UPDATABLE[entity].filter((field) => loaded[field] !== undefined).map((field) => [field, withoutTypename(loaded[field])]),
  )
}

const withoutTypename = (value: unknown): unknown => {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return value
  return Object.fromEntries(Object.entries(value).filter(([key]) => key !== '__typename'))
}

/**
 * The values a change kept on this device is made from: the entry as this screen holds it (the cache, filled by the dialog that loaded it
 * or the list it is in), only when it is the version the change is made from, else none (a change without a base counts every field
 * that differs from the server's as changed on both sides).
 */
export function baseOf(client: ApolloClient, entity: ChangeEntity, id: string, version: number | null | undefined): ChangeValues | undefined {
  if (version == null) return undefined
  const { typename, fragment } = SHAPES[entity]
  const cacheId = client.cache.identify({ __typename: typename, id })
  if (!cacheId) return undefined
  const loaded = client.cache.readFragment<Record<string, unknown>>({ fragment, id: cacheId, returnPartialData: true })
  if (!loaded || loaded.version !== version) return undefined
  const values = valuesOf(entity, loaded)
  return Object.keys(values).length > 0 ? values : undefined
}
