import { describe, expect, it, vi } from 'vitest'
import { inOneTab, keepStorage } from '../../../src/frontend/offline/tabLock.ts'

/** The Web Locks API in memory: one holder per name; `ifAvailable` gets null while it is held. */
function fakeLocks(): LockManager {
  const held = new Set<string>()
  return {
    request: (async (name: string, options: LockOptions, callback: (lock: Lock | null) => Promise<unknown>) => {
      if (options.ifAvailable && held.has(name)) return callback(null)
      held.add(name)
      try {
        return await callback({ name, mode: 'exclusive' })
      } finally {
        held.delete(name)
      }
    }) as LockManager['request'],
    query: async () => ({ held: [], pending: [] }),
  }
}

describe('inOneTab', () => {
  it('runs the work once while another run under the same name is under way, and again after it', async () => {
    const locks = fakeLocks()
    let release!: () => void
    const runs: string[] = []
    const first = inOneTab('sync', () => new Promise<void>((resolve) => ((release = resolve), runs.push('first'))), locks)
    await inOneTab('sync', async () => void runs.push('second'), locks) // the other tab is still syncing
    await inOneTab('other', async () => void runs.push('other'), locks)
    release()
    await first
    await inOneTab('sync', async () => void runs.push('third'), locks)

    expect(runs).toEqual(['first', 'other', 'third'])
  })

  it('says whether the work ran here, so a run another tab kept from going can be tried again', async () => {
    const locks = fakeLocks()
    let release!: () => void
    const first = inOneTab('sync', () => new Promise<void>((resolve) => (release = resolve)), locks)
    expect(await inOneTab('sync', async () => undefined, locks)).toBe(false)
    release()
    expect(await first).toBe(true)
  })

  it('lets the other tabs go after a while, even when the work never ends', async () => {
    vi.useFakeTimers()
    try {
      const locks = fakeLocks()
      const hung = inOneTab('sync', () => new Promise<void>(() => undefined), locks, 1000) // a request that never answers
      await vi.advanceTimersByTimeAsync(1000)
      expect(await hung).toBe(true)
      expect(await inOneTab('sync', async () => undefined, locks)).toBe(true)
    } finally {
      vi.useRealTimers()
    }
  })

  it('simply runs where the browser has no Web Locks', async () => {
    const work = vi.fn(async () => undefined)
    await inOneTab('sync', work, undefined)
    expect(work).toHaveBeenCalledOnce()
  })
})

it('asks the browser to keep the storage once, whatever it answers', async () => {
  const persist = vi.fn(async () => false)
  keepStorage({ persist } as unknown as StorageManager)
  keepStorage({ persist } as unknown as StorageManager)
  expect(persist).toHaveBeenCalledOnce()
})
