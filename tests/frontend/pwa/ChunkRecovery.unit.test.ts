import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { CHUNK_RELOAD_GUARD_MS, CHUNK_RELOAD_KEY, isChunkLoadError, recoverFromChunkError, serverAnswers } from '../../../src/frontend/pwa/chunkRecovery.ts'

let reload: ReturnType<typeof vi.fn<() => void>>

beforeEach(() => {
  reload = vi.fn<() => void>()
})
afterEach(() => vi.unstubAllGlobals())

const answers = async () => true
const silent = async () => false
const recover = (over: Parameters<typeof recoverFromChunkError>[0] = {}) =>
  recoverFromChunkError({ reload, probe: answers, behind: false, url: '/trash', now: 1_000_000, ...over })

it('knows a page\'s code that could not be loaded, in every browser\'s words', () => {
  for (const message of [
    'Failed to fetch dynamically imported module: https://tankstat.example/assets/TrashPage-4Y4PGAq8.js', // Chromium
    'error loading dynamically imported module: https://tankstat.example/assets/TrashPage-4Y4PGAq8.js', // Firefox
    'Importing a module script failed.', // Safari
    'Unable to preload CSS for /assets/index-abc.css', // Vite
  ])
    expect(isChunkLoadError(new TypeError(message))).toBe(true)

  expect(isChunkLoadError(new Error('boom'))).toBe(false)
  expect(isChunkLoadError('Importing a module script failed.')).toBe(true)
  expect(isChunkLoadError(null)).toBe(false)
})

it('reloads the same page once to get the current version, and not again for it within a minute', async () => {
  expect(await recover()).toBe('reloading')
  expect(reload).toHaveBeenCalledOnce()
  expect(JSON.parse(sessionStorage.getItem(CHUNK_RELOAD_KEY)!)).toEqual({ url: '/trash', at: 1_000_000 })

  expect(await recover({ now: 1_000_000 + CHUNK_RELOAD_GUARD_MS - 1 })).toBe('failed') // the reload did not help: no loop
  expect(await recover({ url: '/import', now: 1_000_001 })).toBe('reloading') // another page
  expect(await recover({ now: 1_000_000 + CHUNK_RELOAD_GUARD_MS })).toBe('reloading') // much later
  expect(reload).toHaveBeenCalledTimes(3)
})

it('offline, reloads only when a newer service worker can answer; otherwise the page is not kept on this device yet', async () => {
  expect(await recover({ probe: silent })).toBe('notOfflineYet')
  expect(reload).not.toHaveBeenCalled()

  const probe = vi.fn(silent)
  expect(await recover({ probe, behind: true })).toBe('reloading')
  expect(reload).toHaveBeenCalledOnce()
  expect(probe).not.toHaveBeenCalled() // the newer worker answers the reload: the server is not needed
})

it('never reloads when it cannot remember having done so', async () => {
  const storage = { getItem: () => null, setItem: () => { throw new Error('denied') } }

  expect(await recover({ storage })).toBe('failed')
  expect(reload).not.toHaveBeenCalled()
})

it('a remembered reload it cannot read counts as none', async () => {
  sessionStorage.setItem(CHUNK_RELOAD_KEY, '{not json')

  expect(await recover()).toBe('reloading')
})

it('a reload would reach the server when sw.js answers past every cache, whatever its status but a gateway\'s', async () => {
  const fetch = vi.fn(async () => new Response(null, { status: 200 }))
  vi.stubGlobal('fetch', fetch)
  expect(await serverAnswers()).toBe(true)
  expect(fetch).toHaveBeenCalledWith('/sw.js', expect.objectContaining({ method: 'HEAD', cache: 'no-store' }))

  fetch.mockResolvedValueOnce(new Response(null, { status: 404 })) // the development server has no worker: still the server
  expect(await serverAnswers()).toBe(true)
  fetch.mockResolvedValueOnce(new Response(null, { status: 503 })) // a proxy saying the app is down
  expect(await serverAnswers()).toBe(false)
  fetch.mockRejectedValueOnce(new TypeError('Failed to fetch'))
  expect(await serverAnswers()).toBe(false)
})

it('a reload would not reach the server when the app or the browser knows it is offline: nothing is even asked', async () => {
  const fetch = vi.fn(async () => new Response(null, { status: 200 }))
  vi.stubGlobal('fetch', fetch)

  connectivity.failed()
  expect(await serverAnswers()).toBe(false)
  connectivity.succeeded()
  vi.spyOn(navigator, 'onLine', 'get').mockReturnValueOnce(false)
  expect(await serverAnswers()).toBe(false)
  expect(fetch).not.toHaveBeenCalled()
})
