import type { ApolloClient } from '@apollo/client'
import { ServerError, ServerParseError } from '@apollo/client/errors'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { connectivity, nextDelay, watchConnectivity } from '../../../src/frontend/offline/connectivity.ts'
import { classifyFailure, isConnectionFailure, OfflineError } from '../../../src/frontend/offline/errors.ts'
import { LOCK_RETRY_MS, PROBE_TIMEOUT_MS, probeServer, PUSH_AFTER_MS, REFETCH_GAP_MS, SHOWN_GAP_MS, startOfflineRuntime, withDeadline } from '../../../src/frontend/offline/runtime.ts'
import { appliedMutations } from '../../../src/frontend/offline/appliedMutations.ts'
import { deviceData, TABS_CHANNEL } from '../../../src/frontend/offline/deviceData.ts'
import { memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import { HealthDocument, WelcomeDocument } from '../../../src/frontend/gql/generated.ts'
import { outbox } from '../../../src/frontend/offline/outbox.ts'
import { ApiError } from '../../../src/frontend/pictures/ApiError.ts'

describe('connectivity', () => {
  it('starts as unknown, which counts as reachable, and tells its listeners about changes only', () => {
    const heard = vi.fn()
    const stop = connectivity.subscribe(heard)

    expect([connectivity.state, connectivity.reachable]).toEqual(['unknown', true])
    connectivity.failed()
    connectivity.failed()
    expect([connectivity.state, connectivity.reachable]).toEqual(['unreachable', false])
    connectivity.succeeded()
    expect(heard).toHaveBeenCalledTimes(2)
    stop()
  })

  it('waits longer and longer between probes, at most a minute', () => {
    expect([0, 1, 2, 3, 4, 9].map((n) => nextDelay(n))).toEqual([2_000, 5_000, 15_000, 30_000, 60_000, 60_000])
  })
})

describe('classifyFailure', () => {
  const response = (status: number) => new Response(null, { status })

  it('tells "nothing answered" and "a gateway answered for the app" from an answer of the app', () => {
    expect(classifyFailure(new TypeError('Failed to fetch'))).toBe('network')
    expect(classifyFailure(new ServerParseError(new Error('not JSON'), { response: response(200), bodyText: '<html>' }))).toBe('network')
    expect(classifyFailure(new ServerError('Bad Gateway', { response: response(503), bodyText: '' }))).toBe('gateway')
    expect(classifyFailure(new ApiError(undefined, {}, 'Bad Gateway', 502))).toBe('gateway')
    expect(classifyFailure(new ServerError('Internal', { response: response(500), bodyText: '' }))).toBe('answered')
    expect(classifyFailure(new OfflineError())).toBe('answered')
    expect(isConnectionFailure(new OfflineError())).toBe(true)
  })

  it('a request that got no answer in time is "nothing answered"; one the app took back itself is not', () => {
    expect(classifyFailure(new DOMException('signal timed out', 'TimeoutError'))).toBe('network')
    expect(classifyFailure(new DOMException('aborted', 'AbortError'))).toBe('answered')
  })
})

describe('startOfflineRuntime', () => {
  it('asks the screen again when the server is back, but not more often than every 15 seconds', () => {
    vi.useFakeTimers()
    let now = 0
    const refetchQueries = vi.fn(async () => [])
    const client = { query: vi.fn(() => new Promise(() => undefined)), refetchQueries } as unknown as ApolloClient
    const runtime = startOfflineRuntime(client, () => now)
    try {
      const flap = () => {
        connectivity.failed()
        connectivity.succeeded()
      }
      flap()
      now += 2_000
      flap() // a proxy that keeps answering 503 to the refetch
      expect(refetchQueries).toHaveBeenCalledTimes(1)
      now += REFETCH_GAP_MS
      flap()
      expect(refetchQueries).toHaveBeenCalledTimes(2)
    } finally {
      runtime.stop()
      vi.useRealTimers()
    }
  })

  it('downloads soon after a vehicle came in online, so the device can answer for it offline; not after other changes', async () => {
    deviceData.reset(memoryStorage())
    await deviceData.signedIn('u1') // before the start: no download of its own yet
    vi.useFakeTimers()
    const query = vi.fn((_options: { query: unknown }) => new Promise(() => undefined))
    const client = { query, refetchQueries: vi.fn(async () => []) } as unknown as ApolloClient
    const runtime = startOfflineRuntime(client)
    const downloads = () => query.mock.calls.filter(([options]) => options.query === WelcomeDocument).length
    try {
      appliedMutations.tell('UpdateVehicle')
      await vi.advanceTimersByTimeAsync(PUSH_AFTER_MS)
      expect(downloads()).toBe(0)

      appliedMutations.tell('AddVehicle')
      await vi.advanceTimersByTimeAsync(PUSH_AFTER_MS)
      await vi.waitFor(() => expect(downloads()).toBe(1))
    } finally {
      runtime.stop()
      vi.useRealTimers()
    }
  })
})

describe('withDeadline', () => {
  it('fails once the time is up, without waiting for a request that never ends', async () => {
    vi.useFakeTimers()
    try {
      const hung = withDeadline(new Promise<boolean>(() => undefined), PROBE_TIMEOUT_MS)
      vi.advanceTimersByTime(PROBE_TIMEOUT_MS)
      await expect(hung).rejects.toThrow()
      await expect(withDeadline(Promise.resolve(true), PROBE_TIMEOUT_MS)).resolves.toBe(true)
    } finally {
      vi.useRealTimers()
    }
  })
})

describe('watchConnectivity', () => {
  afterEach(() => vi.useRealTimers())

  it('while the server is out of reach it asks again with growing pauses until it answers', async () => {
    vi.useFakeTimers()
    let answers = false
    const probe = vi.fn(async () => answers)
    const watch = watchConnectivity(probe, { online: () => true })

    connectivity.failed()
    await vi.advanceTimersByTimeAsync(2_000)
    expect(probe).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(4_999)
    expect(probe).toHaveBeenCalledTimes(1) // the second pause is longer
    answers = true
    await vi.advanceTimersByTimeAsync(1)
    expect(probe).toHaveBeenCalledTimes(2)
    expect(connectivity.reachable).toBe(true)
    await vi.advanceTimersByTimeAsync(120_000)
    expect(probe).toHaveBeenCalledTimes(2) // nothing more while it answers
    watch.stop()
  })

  it('the browser going offline marks the server unreachable at once, and coming online asks at once', async () => {
    const probe = vi.fn(async () => true)
    const watch = watchConnectivity(probe, { online: () => true })

    window.dispatchEvent(new Event('offline'))
    expect(connectivity.reachable).toBe(false)
    window.dispatchEvent(new Event('online'))
    await vi.waitFor(() => expect(connectivity.reachable).toBe(true))
    expect(probe).toHaveBeenCalledTimes(1)
    watch.stop()
  })

  it('a browser that starts offline starts unreachable', () => {
    const watch = watchConnectivity(async () => false, { online: () => false, setTimer: () => setTimeout(() => undefined, 0), clearTimer: () => undefined })

    expect(connectivity.reachable).toBe(false)
    watch.stop()
  })
})

describe('the offline runtime', () => {
  /** A client whose requests all answer: Health with its status, the download with nothing (it then gives up), a sync with every change applied. */
  function answeringClient(refetch: () => Promise<unknown> = async () => []) {
    const query = vi.fn(async (options: { query: unknown }) => (options.query === HealthDocument ? { data: { health: { status: 'ok' } } } : { data: undefined }))
    const mutate = vi.fn(async (options: { variables: { input: { changes: { id: string }[] } } }) => ({
      data: { syncChanges: { results: options.variables.input.changes.map((c) => ({ id: c.id, status: 'APPLIED', reason: null })) } },
    }))
    const refetchQueries = vi.fn(refetch)
    const client = { query, mutate, refetchQueries } as unknown as ApolloClient
    const asked = (document: unknown) => query.mock.calls.filter(([options]) => options.query === document).length
    return { client, query, mutate, refetchQueries, asked }
  }

  /** Web Locks as another tab sees them: `held` names the locks that tab holds right now. */
  function otherTabLocks(held: Set<string>): LockManager {
    const request = (name: string, ...rest: unknown[]) => {
      const callback = rest.at(-1) as (lock: Lock | null) => Promise<unknown>
      const options = rest.length > 1 ? (rest[0] as LockOptions) : {}
      return callback(options.ifAvailable && held.has(name) ? null : { name, mode: 'exclusive' })
    }
    return { request, query: async () => ({ held: [], pending: [] }) } as unknown as LockManager
  }

  const refuelling = { id: 'r1', entity: 'refuelings', action: 'add', vehicleId: 'v1', targetId: 'r1', input: { id: 'r1', vehicleId: 'v1' } } as const

  beforeEach(async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => undefined) // the stub client's download gives up, and says so
    deviceData.reset(memoryStorage())
    await deviceData.signedIn('u1') // before the start: no download of its own yet
    await outbox.reload()
  })

  afterEach(async () => {
    vi.useRealTimers()
    vi.restoreAllMocks()
    deviceData.reset(memoryStorage())
    await outbox.reload()
  })

  it('Try again asks the server at once: as a plain request before the runtime runs, through its probe once it does', async () => {
    const failing = { query: vi.fn(() => Promise.reject(new TypeError('Failed to fetch'))) } as unknown as ApolloClient
    await expect(probeServer(failing)).resolves.toBeUndefined()
    expect(failing.query).toHaveBeenCalledWith(expect.objectContaining({ query: HealthDocument, context: { offline: 'bypass' } }))

    const { client, asked } = answeringClient()
    const runtime = startOfflineRuntime(client)
    try {
      connectivity.failed()
      await probeServer(client)
      expect(asked(HealthDocument)).toBe(1)
      expect(connectivity.reachable).toBe(true) // the probe marked it
    } finally {
      runtime.stop()
    }
  })

  it('a change kept while the server can be reached is sent soon after; none while it cannot', async () => {
    vi.useFakeTimers()
    const { client, mutate } = answeringClient()
    const runtime = startOfflineRuntime(client)
    try {
      connectivity.failed()
      await outbox.enqueue({ ...refuelling, id: 'offline', targetId: 'offline' })
      await vi.advanceTimersByTimeAsync(PUSH_AFTER_MS)
      expect(mutate).not.toHaveBeenCalled()
      await outbox.remove(['offline'])

      connectivity.reset()
      await outbox.enqueue(refuelling)
      await vi.advanceTimersByTimeAsync(PUSH_AFTER_MS)

      await vi.waitFor(() => expect(outbox.changes).toEqual([]))
      expect(mutate).toHaveBeenCalledTimes(1)
      expect(mutate.mock.calls[0][0].variables.input.changes.map((c) => c.id)).toEqual(['r1'])
    } finally {
      runtime.stop()
    }
  })

  it('while another tab syncs, the changes kept here are looked at again a little later; with none left, nothing more happens', async () => {
    vi.useFakeTimers()
    const held = new Set(['tankstat-sync'])
    Object.defineProperty(navigator, 'locks', { value: otherTabLocks(held), configurable: true })
    const { client, mutate, asked } = answeringClient()
    const runtime = startOfflineRuntime(client)
    try {
      await outbox.enqueue(refuelling)
      await vi.advanceTimersByTimeAsync(PUSH_AFTER_MS)
      expect(mutate).not.toHaveBeenCalled() // the other tab is at it

      held.clear()
      await vi.advanceTimersByTimeAsync(LOCK_RETRY_MS)
      await vi.waitFor(() => expect(outbox.changes).toEqual([]))
      expect(mutate).toHaveBeenCalledTimes(1)

      held.add('tankstat-sync')
      const downloads = asked(WelcomeDocument)
      appliedMutations.tell('AddVehicle')
      await vi.advanceTimersByTimeAsync(PUSH_AFTER_MS + LOCK_RETRY_MS)
      expect(asked(WelcomeDocument)).toBe(downloads) // kept from it, and nothing waits to be looked at again
    } finally {
      runtime.stop()
      delete (navigator as { locks?: unknown }).locks
    }
  })

  it('showing the tab again downloads only when the last download is a while ago', async () => {
    let now = 0
    const { client, asked } = answeringClient()
    const runtime = startOfflineRuntime(client, () => now)
    const shown = () => document.dispatchEvent(new Event('visibilitychange'))
    try {
      shown()
      await vi.waitFor(() => expect(asked(WelcomeDocument)).toBe(1))

      now += SHOWN_GAP_MS - 1
      shown()
      now += 1
      shown()
      await vi.waitFor(() => expect(asked(WelcomeDocument)).toBe(2))
    } finally {
      runtime.stop()
    }
  })

  it('a screen that fails to be asked again never breaks the runtime: when the server is back, or when another tab signed someone else in', async () => {
    await deviceData.boot(memoryStorage()) // listens to the other tabs
    const { client, refetchQueries } = answeringClient(() => Promise.reject(new Error('A query failed.')))
    const runtime = startOfflineRuntime(client)
    const otherTab = new BroadcastChannel(TABS_CHANNEL)
    try {
      connectivity.failed()
      connectivity.succeeded()
      expect(refetchQueries).toHaveBeenCalledWith({ include: 'active' })

      otherTab.postMessage({ type: 'signedIn', user: 'u2' })
      await vi.waitFor(() => expect(refetchQueries).toHaveBeenCalledWith({ include: ['Session'] }))
    } finally {
      otherTab.close()
      runtime.stop()
    }
  })
})
