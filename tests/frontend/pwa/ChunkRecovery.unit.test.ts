import { beforeEach, expect, it, vi } from 'vitest'
import { CHUNK_RELOAD_GUARD_MS, CHUNK_RELOAD_KEY, isChunkLoadError, recoverFromChunkError } from '../../../src/frontend/pwa/chunkRecovery.ts'

let reload: ReturnType<typeof vi.fn<() => void>>

beforeEach(() => {
  reload = vi.fn<() => void>()
})

const recover = (over: Parameters<typeof recoverFromChunkError>[0] = {}) =>
  recoverFromChunkError({ reload, reachable: true, behind: false, url: '/trash', now: 1_000_000, ...over })

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

it('reloads the same page once to get the current version, and not again for it within a minute', () => {
  expect(recover()).toBe('reloading')
  expect(reload).toHaveBeenCalledOnce()
  expect(JSON.parse(sessionStorage.getItem(CHUNK_RELOAD_KEY)!)).toEqual({ url: '/trash', at: 1_000_000 })

  expect(recover({ now: 1_000_000 + CHUNK_RELOAD_GUARD_MS - 1 })).toBe('failed') // the reload did not help: no loop
  expect(recover({ url: '/import', now: 1_000_001 })).toBe('reloading') // another page
  expect(recover({ now: 1_000_000 + CHUNK_RELOAD_GUARD_MS })).toBe('reloading') // much later
  expect(reload).toHaveBeenCalledTimes(3)
})

it('offline, reloads only when a newer service worker can answer; otherwise the page is not kept on this device yet', () => {
  expect(recover({ reachable: false })).toBe('notOfflineYet')
  expect(reload).not.toHaveBeenCalled()

  expect(recover({ reachable: false, behind: true })).toBe('reloading')
  expect(reload).toHaveBeenCalledOnce()
})

it('never reloads when it cannot remember having done so', () => {
  const storage = { getItem: () => null, setItem: () => { throw new Error('denied') } }

  expect(recover({ storage })).toBe('failed')
  expect(reload).not.toHaveBeenCalled()
})

it('a remembered reload it cannot read counts as none', () => {
  sessionStorage.setItem(CHUNK_RELOAD_KEY, '{not json')

  expect(recover()).toBe('reloading')
})
