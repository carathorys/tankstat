import type { ApolloClient } from '@apollo/client'
import { HealthDocument } from '../gql/generated.ts'
import { connectivity, watchConnectivity } from './connectivity.ts'

/**
 * Started once by main.tsx: asks the server again while it is out of reach, and when it is back every query on the screen is asked again
 * (what was shown may be stale, and what failed meanwhile is filled in).
 */
let active: { probeNow: () => Promise<void> } | null = null

/** Asks the server at once whether it can be reached again (the footer's Try again). */
export function probeServer(client: ApolloClient): Promise<unknown> {
  if (active) return active.probeNow()
  return client.query({ query: HealthDocument, fetchPolicy: 'network-only', context: { offline: 'bypass' } }).catch(() => undefined)
}

export function startOfflineRuntime(client: ApolloClient) {
  const watch = watchConnectivity(async () => {
    await client.query({ query: HealthDocument, fetchPolicy: 'network-only', context: { offline: 'bypass' } })
    return true
  })
  let wasReachable = connectivity.reachable
  const unsubscribe = connectivity.subscribe(() => {
    if (connectivity.reachable && !wasReachable) void client.refetchQueries({ include: 'active' }).catch(() => undefined)
    wasReachable = connectivity.reachable
  })
  active = watch
  return {
    probeNow: watch.probeNow,
    stop() {
      watch.stop()
      unsubscribe()
      active = null
    },
  }
}
