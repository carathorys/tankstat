import type { ApolloClient } from '@apollo/client'
import { HealthDocument } from '../gql/generated.ts'
import { PROBE_TIMEOUT_MS, withDeadline } from '../offline/runtime.ts'

/** How the page waits for a restarted server: a probe every `interval` ms, the old host given `downFor` ms to go, the new one `upFor` ms to answer. Tests shorten them. */
export const RESTART_WAIT = { interval: 1_000, downFor: 10_000, upFor: 90_000 }

export type RestartOutcome = 'back' | 'timeout'

const pause = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms))

/**
 * Waits for the server to go away and come back after a restart: first until a probe fails (the host that answered the request has
 * stopped; or `downFor` passed, a restart so quick the probes missed it), then until a probe answers again, `upFor` at most.
 * A probe answers true when the server did; it never throws.
 */
export async function waitForRestart(probe: () => Promise<boolean>, wait = RESTART_WAIT, sleep = pause, now: () => number = Date.now): Promise<RestartOutcome> {
  const started = now()
  while (now() - started < wait.downFor) {
    await sleep(wait.interval)
    if (!(await probe())) break
  }
  const gone = now()
  while (now() - gone < wait.upFor) {
    await sleep(wait.interval)
    if (await probe()) return 'back'
  }
  return 'timeout'
}

/**
 * The probe a restart wait asks: `Health` past the device (an answer the device kept would say the server is there), with a deadline per
 * question, as the runtime's own, so a request nobody answers never holds the wait past `downFor` and `upFor`.
 */
export const healthProbe = (client: ApolloClient) => () =>
  withDeadline(client.query({ query: HealthDocument, fetchPolicy: 'network-only', context: { offline: 'bypass' } }), PROBE_TIMEOUT_MS).then(
    () => true,
    () => false,
  )
