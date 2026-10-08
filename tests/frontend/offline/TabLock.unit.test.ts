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
