import { afterEach, expect, it, vi } from 'vitest'
import { timedFetch } from '../../../../src/frontend/apolloClient.ts'

afterEach(() => vi.unstubAllGlobals())

function fakeFetch() {
  const fetch = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response('{}'))
  vi.stubGlobal('fetch', fetch)
  return fetch
}

it('gives a request a deadline of its own', async () => {
  const fetch = fakeFetch()

  await timedFetch('/graphql', { method: 'POST' })

  const init = fetch.mock.calls[0][1]!
  expect(init.method).toBe('POST')
  expect(init.signal).toBeInstanceOf(AbortSignal)
  expect(init.signal!.aborted).toBe(false)
})

it('still lets the caller take the request back', async () => {
  const fetch = fakeFetch()
  const caller = new AbortController()

  await timedFetch('/graphql', { signal: caller.signal })
  const signal = fetch.mock.calls[0][1]!.signal!
  expect(signal).not.toBe(caller.signal) // the caller's and the deadline, combined
  caller.abort()

  expect(signal.aborted).toBe(true)
})

it('where the browser cannot combine signals, the request goes as it was asked', async () => {
  const fetch = fakeFetch()
  vi.stubGlobal('AbortSignal', Object.assign(Object.create(AbortSignal), { any: undefined, timeout: AbortSignal.timeout }))
  const init = { method: 'POST' }

  await timedFetch('/graphql', init)

  expect(fetch).toHaveBeenCalledWith('/graphql', init)
})
