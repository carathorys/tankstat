import type { ApolloClient } from '@apollo/client'
import { HealthDocument } from '../gql/generated.ts'
import { connectivity, watchConnectivity } from './connectivity.ts'
import { deviceData } from './deviceData.ts'
import type { PullEngine } from './pull.ts'

/**
 * Started once by main.tsx: asks the server again while it is out of reach, and when it is back every query on the screen is asked again
 * (what was shown may be stale, and what failed meanwhile is filled in). It also keeps the offline window downloaded (`pull.ts`): once the
 * server said who is signed in, whenever the server is back, and every hour while the app is shown.
 */
let active: { probeNow: () => Promise<void> } | null = null
let downloads: PullEngine | null = null

/** How often a page that stays open downloads what changed. */
export const PULL_EVERY_MS = 60 * 60 * 1000

/** The download of the offline window, for the screens that show or start it; null until it first ran. */
export const offlineDownload = (): PullEngine | null => downloads

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
  // The download engine is loaded when it first has something to do, not with the app's first paint.
  let pull: Promise<PullEngine> | null = null
  const engine = () =>
    (pull ??= import('./pull.ts').then(({ createPullEngine }) => {
      downloads = createPullEngine({ client })
      return downloads
    }))
  const download = () => {
    if (deviceData.user !== null && connectivity.reachable && document.visibilityState !== 'hidden') void engine().then((e) => e.run())
  }
  let wasReachable = connectivity.reachable
  const unsubscribe = connectivity.subscribe(() => {
    if (connectivity.reachable && !wasReachable) {
      void client.refetchQueries({ include: 'active' }).catch(() => undefined)
      download()
    }
    wasReachable = connectivity.reachable
  })
  const unsubscribeUser = deviceData.subscribe(download)
  // A start in a tab that was not shown downloads once it is.
  document.addEventListener('visibilitychange', download)
  const hourly = setInterval(download, PULL_EVERY_MS)
  active = watch
  return {
    probeNow: watch.probeNow,
    engine,
    stop() {
      watch.stop()
      unsubscribe()
      unsubscribeUser()
      document.removeEventListener('visibilitychange', download)
      clearInterval(hourly)
      active = null
      downloads = null
    },
  }
}
