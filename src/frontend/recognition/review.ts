import type { LogValue, ReviewState } from '../gql/generated.ts'

/** Whether a polled list has logs whose photos are still being read (it then asks again until they are done). */
export const anyAwaiting = (rows: readonly { reviewState?: ReviewState }[]) => rows.some((r) => r.reviewState === 'AWAITING_PHOTOS')

/** The values of a log its photos filled in, as a lookup by form field. */
export function filledFields<F extends string>(values: readonly LogValue[] | undefined, into: Partial<Record<LogValue, F>>): Set<F> {
  return new Set((values ?? []).map((v) => into[v]).filter((f): f is F => f !== undefined))
}
