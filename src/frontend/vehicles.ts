import type { DistanceUnit, FuelType, VolumeUnit } from './gql/generated.ts'

/** Compile-time guards: adding a value to the backend makes these fail until the UI knows it. */
const FUEL_TYPE_SET = { PETROL: true, DIESEL: true, LPG: true } satisfies Record<FuelType, true>
const DISTANCE_UNIT_SET = { KILOMETERS: true, MILES: true } satisfies Record<DistanceUnit, true>
const VOLUME_UNIT_SET = { LITERS: true, US_GALLONS: true, IMPERIAL_GALLONS: true } satisfies Record<VolumeUnit, true>

export const FUEL_TYPES = Object.keys(FUEL_TYPE_SET) as FuelType[]
export const DISTANCE_UNITS = Object.keys(DISTANCE_UNIT_SET) as DistanceUnit[]
export const VOLUME_UNITS = Object.keys(VOLUME_UNIT_SET) as VolumeUnit[]
