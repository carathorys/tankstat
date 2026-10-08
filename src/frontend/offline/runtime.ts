import type { ApolloClient } from '@apollo/client'
import { HealthDocument } from '../gql/generated.ts'
import { connectivity, watchConnectivity } from './connectivity.ts'
import { deviceData } from './deviceData.ts'
import { outbox } from './outbox.ts'
import type { PullEngine } from './pull.ts'
import type { PushEngine } from './push.ts'

/**
 * Started once by main.tsx: asks the server again while it is out of reach, and when it is back every query on the screen is asked again
 * (what was shown may be stale, and what failed meanwhile is filled in). It also sends the changes kept on this device (`push.ts`) and
 * keeps the offline window downloaded (`pull.ts`): once the server said who is signed in, whenever the server is back, every hour while
 * the app is shown, and soon after a change is kept while the server is reachable.
 */
let active: { probeNow: () => Promise<void> } | null = null
let loadEngine: (() => Promise<PullEngine>) | null = null
let loadPush: (() => Promise<PushEngine>) | null = null

/** How soon after a change is kept (while the server is reachable) it is sent. */
export const PUSH_AFTER_MS = 1000

/** Sending the changes kept on this device, for the screens that show or start it (loaded on first use); null before the runtime started. */
export const offlineSync = (): Promise<PushEngine> | null => loadPush?.() ?? null

/** Tests: the sync engine the screens get, without starting the runtime. */
export function provideOfflineSync(engine: PushEngine | null) {
  loadPush = engine ? () => Promise.resolve(engine) : null
}

/** How often a page that stays open downloads what changed. */
export const PULL_EVERY_MS = 60 * 60 * 1000

/** Showing the tab again downloads only when the last download is at least this old. */
export const SHOWN_GAP_MS = 15 * 60 * 1000

/** The download of the offline window, for the screens that show or start it (loaded on first use); null before the runtime started. */
export const offlineDownload = (): Promise<PullEngine> | null => loadEngine?.() ?? null

/** Tests: the engine the screens get, without starting the runtime's probes and timers. */
export function provideOfflineDownload(engine: PullEngine | null) {
  loadEngine = engine ? () => Promise.resolve(engine) : null
}

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
  // The download engine is loaded when it first has something to do, not with the app's first paint.
  let pull: Promise<PullEngine> | null = null
  const engine = () => (pull ??= import('./pull.ts').then(({ createPullEngine }) => createPullEngine({ client })))
  loadEngine = engine
  // Sending goes before downloading (it ends with a download of its own): what was changed here reaches the server first.
  let push: Promise<PushEngine> | null = null
  const pusher = () => (push ??= import('./push.ts').then(({ createPushEngine }) => createPushEngine({ client, pull: () => engine().then((e) => e.run()) })))
  loadPush = pusher
  let downloadedAt = Number.NEGATIVE_INFINITY
  const download = () => {
    if (deviceData.user === null || !connectivity.reachable || document.visibilityState === 'hidden') return
    downloadedAt = now()
    void (outbox.changes.length > 0 ? pusher().then((e) => e.run()) : engine().then((e) => e.run()))
  }
  // Shown again (an app switched back to on a phone): only when the last download is a while ago, not on every switch.
  const onShown = () => {
    if (now() - downloadedAt >= SHOWN_GAP_MS) download()
  }
  // A change kept while the server is reachable (its vehicle had changes waiting, or a request failed) goes soon after.
  let soon: ReturnType<typeof setTimeout> | undefined
  const unsubscribeOutbox = outbox.subscribe(() => {
    clearTimeout(soon)
    if (outbox.changes.length > 0 && connectivity.reachable) soon = setTimeout(download, PUSH_AFTER_MS)
  })
  let wasReachable = connectivity.reachable
  let refetchedAt = Number.NEGATIVE_INFINITY
  const unsubscribe = connectivity.subscribe(() => {
    if (connectivity.reachable && !wasReachable && now() - refetchedAt >= REFETCH_GAP_MS) {
      refetchedAt = now()
      void client.refetchQueries({ include: 'active' }).catch(() => undefined)
      download()
    }
    wasReachable = connectivity.reachable
  })
  // Another tab signed someone else in (or out): this tab asks whose it is, so the screen and the offline data follow.
  const offElsewhere = deviceData.onSignedInElsewhere(() => void client.refetchQueries({ include: ['Session'] }).catch(() => undefined))
  const unsubscribeUser = deviceData.subscribe(download)
  // A start in a tab that was not shown downloads once it is.
  document.addEventListener('visibilitychange', onShown)
  const hourly = setInterval(download, PULL_EVERY_MS)
  active = watch
  return {
    probeNow: watch.probeNow,
    engine,
    stop() {
      watch.stop()
      unsubscribe()
      offElsewhere()
      unsubscribeUser()
      unsubscribeOutbox()
      clearTimeout(soon)
      document.removeEventListener('visibilitychange', onShown)
      clearInterval(hourly)
      active = null
      loadEngine = null
      loadPush = null
    },
  }
}
