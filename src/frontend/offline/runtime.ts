import type { ApolloClient } from '@apollo/client'
import { HealthDocument } from '../gql/generated.ts'
import { connectivity, watchConnectivity } from './connectivity.ts'
import { deviceData } from './deviceData.ts'

/**
 * Started once by main.tsx: asks the server again while it is out of reach, and when it is back every query on the screen is asked again
 * (what was shown may be stale, and what failed meanwhile is filled in).
 */
let active: { probeNow: () => Promise<void> } | null = null

/** The promise, or a failure once `ms` passed without it settling (the request itself may go on; nobody waits for it). */
export function withDeadline<T>(promise: Promise<T>, ms: number): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`No answer within ${ms} ms`)), ms)
    promise.then(resolve, reject).finally(() => clearTimeout(timer))
  })
}

/** Asks the server at once whether it can be reached again (the footer's Try again). */
export function probeServer(client: ApolloClient): Promise<unknown> {
  if (active) return active.probeNow()
  return client.query({ query: HealthDocument, fetchPolicy: 'network-only', context: { offline: 'bypass' } }).catch(() => undefined)
}

/** How long the probe waits for an answer: a server that does not answer at all (a LAN address seen from mobile data) is out of reach. */
export const PROBE_TIMEOUT_MS = 5_000

/** The screen is asked again at most this often when the server comes back (a proxy that keeps flapping must not make a loop of it). */
export const REFETCH_GAP_MS = 15_000

export function startOfflineRuntime(client: ApolloClient, now: () => number = Date.now) {
  const watch = watchConnectivity(() =>
    withDeadline(client.query({ query: HealthDocument, fetchPolicy: 'network-only', context: { offline: 'bypass' } }).then(() => true), PROBE_TIMEOUT_MS),
  )
  let wasReachable = connectivity.reachable
  let refetchedAt = Number.NEGATIVE_INFINITY
  const unsubscribe = connectivity.subscribe(() => {
    if (connectivity.reachable && !wasReachable && now() - refetchedAt >= REFETCH_GAP_MS) {
      refetchedAt = now()
      void client.refetchQueries({ include: 'active' }).catch(() => undefined)
    }
    wasReachable = connectivity.reachable
  })
  // Another tab signed someone else in (or out): this tab asks whose it is, so the screen and the offline data follow.
  const offElsewhere = deviceData.onSignedInElsewhere(() => void client.refetchQueries({ include: ['Session'] }).catch(() => undefined))
  active = watch
  return {
    probeNow: watch.probeNow,
    stop() {
      watch.stop()
      unsubscribe()
      offElsewhere()
      active = null
    },
  }
}
