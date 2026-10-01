import type { FuelType } from './gql/generated.ts'

/** Compile-time guard: adding a fuel type to the backend makes this fail until the UI knows it. */
const FUEL_TYPE_SET = { PETROL: true, DIESEL: true, LPG: true } satisfies Record<FuelType, true>

export const FUEL_TYPES = Object.keys(FUEL_TYPE_SET) as FuelType[]
