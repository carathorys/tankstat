import type { ApolloClient } from '@apollo/client'
import { ServerError, ServerParseError } from '@apollo/client/errors'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { connectivity, nextDelay, watchConnectivity } from '../../../src/frontend/offline/connectivity.ts'
import { classifyFailure, isConnectionFailure, OfflineError } from '../../../src/frontend/offline/errors.ts'
import { PROBE_TIMEOUT_MS, REFETCH_GAP_MS, startOfflineRuntime, withDeadline } from '../../../src/frontend/offline/runtime.ts'
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
