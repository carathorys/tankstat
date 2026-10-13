import { IDBFactory } from 'fake-indexeddb'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { deviceData, GENERATION_KEY } from '../../../src/frontend/offline/deviceData.ts'
import { indexedDbStorage, memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import { compare, foundRows, inSendOrder, keptAnswers, type FoundRow } from '../../../src/frontend/offline/foundRows.ts'
import { generationOf } from '../../../src/frontend/offline/offlineLink.ts'
import { snapshotKey } from '../../../src/frontend/offline/snapshotPolicy.ts'

const row = (over: Partial<FoundRow>): FoundRow => ({
  id: 'r1', entity: 'refuelings', vehicleId: 'v1', generation: 'g1', version: 2, values: {}, title: '', setAsideAt: 1, ...over,
})

describe('generationOf', () => {
  it('reads the generation of a Session answer, and nothing from an older one', () => {
    expect(generationOf({ session: { mode: 'NONE', user: null, instance: { generation: 'g1', restorePoint: null } } })).toBe('g1')
    expect(generationOf({ session: { mode: 'NONE', user: null } })).toBeUndefined()
    expect(generationOf(null)).toBeUndefined()
  })
})

describe('deviceData.generationSeen', () => {
  it('adopts the first generation without dropping anything, and drops the downloaded rows and kept answers when it changes, never the changes waiting nor the rows set aside', async () => {
    deviceData.reset(memoryStorage())
    await deviceData.signedIn('alice')
    const rows = (await deviceData.writableRows())!
    await rows.putVehicles([{ id: 'v1', name: 'Octavia', logAccess: 'DELETE' }])
    await deviceData.keep('Welcome:{}', { kept: true })
    await rows.putChanges([{ id: 'c1', seq: 1, createdAt: 1, entity: 'vehicles', action: 'update', vehicleId: 'v1', targetId: 'v1', input: { name: 'Mine' } }])
    const phases: string[] = []
    deviceData.onGenerationChange(async (phase, { from, to }) => {
      phases.push(`${phase}:${from}>${to}`)
      if (phase === 'before') await rows.putFound([row({ id: 'v1', entity: 'vehicles', generation: 'g1' })])
    })

    expect(await deviceData.generationSeen(undefined)).toBe('same') // an older server
    expect(await deviceData.generationSeen('g1')).toBe('adopted')
    expect(await rows.vehicles()).toHaveLength(1)
    expect(await deviceData.generationSeen('g1')).toBe('same')
    expect(phases).toEqual([])

    expect(await deviceData.generationSeen('g2')).toBe('changed')

    expect(phases).toEqual(['before:g1>g2', 'after:g1>g2'])
    expect(await rows.vehicles()).toEqual([])
    expect(await deviceData.read('Welcome:{}')).toBeUndefined()
    expect((await deviceData.read(GENERATION_KEY))?.data).toBe('g2')
    expect(await rows.changes()).toHaveLength(1) // what the user did stays
    expect(await rows.found()).toHaveLength(1) // what was set aside stays
    expect(await deviceData.generationSeen('g2')).toBe('same') // a second tab seeing the same answer does nothing more

    await deviceData.removeAll() // Remove offline data: the rows set aside go too
    expect(await rows.found()).toEqual([])
    expect(await rows.changes()).toHaveLength(1)
  })

  it('a database of the version before gets the place for the rows set aside', async () => {
    const factory = new IDBFactory()
    const old = await new Promise<IDBDatabase>((resolve, reject) => {
      const request = factory.open('tankstat-offline-alice', 5)
      request.onupgradeneeded = () => {
        const db = request.result
        db.createObjectStore('snapshots', { keyPath: 'key' }).createIndex('at', 'at')
        db.createObjectStore('vehicles', { keyPath: 'id' })
        db.createObjectStore('refuelings', { keyPath: 'id' }).createIndex('vehicleId', 'vehicleId')
        db.createObjectStore('expenses', { keyPath: 'id' }).createIndex('vehicleId', 'vehicleId')
        db.createObjectStore('cursors', { keyPath: 'vehicleId' })
        db.createObjectStore('changes', { keyPath: 'id' })
        db.createObjectStore('photos', { keyPath: 'key' })
        db.createObjectStore('pictures', { keyPath: 'id' })
      }
      request.onsuccess = () => resolve(request.result)
      request.onerror = () => reject(request.error)
    })
    old.close()

    const store = await indexedDbStorage(factory).open('alice')
    await store.rows.putFound([row({})])
    expect(await store.rows.found()).toHaveLength(1)
    await store.clear() // the wipe spares them
    expect(await store.rows.found()).toHaveLength(1)
    await store.rows.clearFound()
    expect(await store.rows.found()).toEqual([])
  })
})

describe('deviceData.generationSeen, when answers arrive together or too early', () => {
  afterEach(() => {
    Reflect.deleteProperty(navigator, 'locks')
    vi.restoreAllMocks()
  })

  it('keeps nothing, and drops nothing, until the server said whose data this is', async () => {
    deviceData.reset(memoryStorage())
    const seen: string[] = []
    deviceData.onGenerationChange((phase) => void seen.push(phase))

    expect(await deviceData.generationSeen('g1')).toBe('same') // nobody confirmed yet: an answer may be someone else's
    await deviceData.signedIn('alice')
    expect(await deviceData.read(GENERATION_KEY)).toBeUndefined()
    expect(seen).toEqual([])
  })

  it('two answers of the new history at once wipe once: the second finds the new generation kept', async () => {
    deviceData.reset(memoryStorage())
    await deviceData.signedIn('alice')
    await deviceData.generationSeen('g1')
    const rows = (await deviceData.writableRows())!
    await rows.putVehicles([{ id: 'v1', name: 'Octavia', logAccess: 'DELETE', version: 1 }])
    const phases: string[] = []
    deviceData.onGenerationChange(async (phase) => {
      phases.push(phase)
      if (phase === 'before') await new Promise((resolve) => setTimeout(resolve, 20)) // slow enough for the second answer to arrive meanwhile
      if (phase === 'after') await rows.putVehicles([{ id: 'v1', name: 'Octavia', logAccess: 'DELETE', version: 7 }]) // the download of the new history
    })

    const results = await Promise.all([deviceData.generationSeen('g2'), deviceData.generationSeen('g2')])

    expect(results).toEqual(['changed', 'same'])
    expect(phases).toEqual(['before', 'after'])
    expect(await rows.vehicles()).toEqual([{ id: 'v1', name: 'Octavia', logAccess: 'DELETE', version: 7 }]) // not wiped again after the download
  })

  it('a tab that finds the new history kept by another tab drops its own copies of the old one, and tells what reads them', async () => {
    const storage = memoryStorage()
    deviceData.reset(storage)
    await deviceData.signedIn('alice')
    await deviceData.generationSeen('g1')
    const rows = (await deviceData.writableRows())!
    await rows.putLogs('refuelings', [{ id: 'r1', vehicleId: 'v1', date: '2026-09-01', version: 1, pulledAt: 1 }])
    expect(await rows.logs('refuelings', 'v1')).toHaveLength(1) // this tab holds a copy of them in memory now
    const other = await storage.open('alice') // another tab: the same stored data, copies of its own
    await other.clear()
    await other.put({ key: GENERATION_KEY, data: 'g2', at: 1 })
    expect(await rows.logs('refuelings', 'v1')).toHaveLength(1) // still this tab's copy of the old history
    const told = vi.fn()
    deviceData.onStoreChange(told)

    expect(await deviceData.generationSeen('g2')).toBe('same')

    expect(await rows.logs('refuelings', 'v1')).toEqual([])
    expect(told).toHaveBeenCalled()
  })

  it('drops nothing when the account was signed out while the rows were set aside: the next answer checks again', async () => {
    deviceData.reset(memoryStorage())
    await deviceData.signedIn('alice')
    await deviceData.generationSeen('g1')
    const rows = (await deviceData.writableRows())!
    await rows.putVehicles([{ id: 'v1', name: 'Octavia', logAccess: 'DELETE' }])
    deviceData.onGenerationChange((phase) => {
      if (phase === 'before') deviceData.unconfirm() // a sign-in answer came meanwhile
    })

    expect(await deviceData.generationSeen('g2')).toBe('same')

    expect(await rows.vehicles()).toHaveLength(1)
    expect((await deviceData.read(GENERATION_KEY))?.data).toBe('g1')
  })

  it('asks for the lock the tabs share, when the browser has Web Locks', async () => {
    const asked: string[] = []
    Object.defineProperty(navigator, 'locks', {
      configurable: true,
      value: { request: async (name: string, work: () => Promise<unknown>) => (asked.push(name), work()) },
    })
    deviceData.reset(memoryStorage())
    await deviceData.signedIn('alice')

    expect(await deviceData.generationSeen('g1')).toBe('adopted')

    expect(asked).toEqual(['tankstat-generation'])
  })
})

describe('compare', () => {
  const rows = [
    row({ id: 'same', version: 2 }),
    row({ id: 'newerThere', version: 2 }),
    row({ id: 'olderThere', version: 3 }),
    row({ id: 'gone', version: 1 }),
    row({ id: 'pending', vehicleId: 'v2', version: 1 }),
  ]
  const held = (_: unknown, id: string) => ({ same: 2, newerThere: 5, olderThere: 1 })[id]

  it('drops what the server has at the same or a newer version, keeps what it lacks or holds older, and waits for an unfinished download', () => {
    const { keep, drop } = compare(rows, held, (r) => r.vehicleId === 'v1')

    expect(drop).toEqual(['same', 'newerThere'])
    expect(keep.map((r) => [r.id, r.serverVersion, r.checked])).toEqual([['olderThere', 1, true], ['gone', undefined, true], ['pending', undefined, undefined]])
  })
})

describe('inSendOrder', () => {
  it('sends a vehicle before its schedules and logs, the logs oldest first', () => {
    const ordered = inSendOrder([
      row({ id: 'log-new', date: '2026-09-02' }),
      row({ id: 'schedule', entity: 'recurring' }),
      row({ id: 'log-old', date: '2026-09-01' }),
      row({ id: 'car', entity: 'vehicles' }),
    ])

    expect(ordered.map((r) => r.id)).toEqual(['car', 'schedule', 'log-old', 'log-new'])
  })
})

describe('foundRows', () => {
  it('sets the downloaded vehicles, logs and schedules aside, and after a download keeps only what the server lacks', async () => {
    deviceData.reset(memoryStorage())
    await deviceData.signedIn('alice')
    const rows = (await deviceData.writableRows())!
    await rows.putVehicles([{ id: 'v1', name: 'Octavia', logAccess: 'DELETE', version: 3, fuelType: 'PETROL' }])
    await rows.putLogs('refuelings', [
      { id: 'r1', vehicleId: 'v1', date: '2026-09-01', version: 1, pulledAt: 1, volume: 40, totalCost: 60, currency: 'EUR', odometer: 1000, isFullTank: true },
      { id: 'r2', vehicleId: 'v1', date: '2026-09-02', version: 1, pulledAt: 1, deletedAt: '2026-09-03' },
    ])
    await rows.putLogs('expenses', [{ id: 'e1', vehicleId: 'v1', date: '2026-09-05', version: 2, pulledAt: 1, title: 'Tyres', amount: 100, currency: 'EUR' }])
    await deviceData.keep(snapshotKey('RecurringExpenses', { vehicleId: 'v1' }), { vehicle: { id: 'v1', recurring: [{ id: 's1', version: 1, title: 'Oil', kind: 'TIME' }] } })
    const kept = async (name: string, variables: Record<string, unknown>) => (await deviceData.read(snapshotKey(name, variables)))?.data as Record<string, unknown> | undefined

    expect(await foundRows.setAside(rows, kept, 'g1')).toBe(4) // the trashed log is left out
    const found = await rows.found()
    expect(found.map((r) => [r.entity, r.id, r.title, r.date]).sort()).toEqual([
      ['expenses', 'e1', 'Tyres', '2026-09-05'],
      ['recurring', 's1', 'Oil', undefined],
      ['refuelings', 'r1', '', '2026-09-01'],
      ['vehicles', 'v1', 'Octavia', undefined],
    ])
    expect(found.find((r) => r.id === 'r1')?.values).toEqual({ date: '2026-09-01', volume: 40, totalCost: 60, currency: 'EUR', odometer: 1000, isFullTank: true })
    expect(found.find((r) => r.id === 'v1')?.values).toEqual({ name: 'Octavia', fuelType: 'PETROL' })

    // The new history downloaded: the vehicle at an older version, the expense back as it was, the refuelling and the schedule gone.
    await rows.clear()
    await rows.putVehicles([{ id: 'v1', name: 'Octavia', logAccess: 'DELETE', version: 1 }])
    await rows.putLogs('expenses', [{ id: 'e1', vehicleId: 'v1', date: '2026-09-05', version: 2, pulledAt: 2 }])
    await rows.putCursor({ vehicleId: 'v1', from: null, watermark: 'w', next: null, complete: true, fullStartedAt: null })
    await foundRows.reconcile(rows, async () => ({ vehicle: { id: 'v1', recurring: [] } }))

    const left = (await rows.found()).map((r) => [r.id, r.serverVersion, r.checked]).sort()
    expect(left).toEqual([['r1', undefined, true], ['s1', undefined, true], ['v1', 1, true]])
  })

  it('judges nothing a download does not cover yet, and keeps everything of a vehicle gone from the server', async () => {
    deviceData.reset(memoryStorage())
    await deviceData.signedIn('alice')
    const rows = (await deviceData.writableRows())!
    await rows.putFound([
      row({ id: 'r1', vehicleId: 'v1' }), // v1 listed, not downloaded in full yet
      row({ id: 'r2', vehicleId: 'v2' }), // v2 no longer listed at all
      row({ id: 'old', vehicleId: 'v3', date: '2026-01-10' }), // before the start of v3's window: the download says nothing of it
      row({ id: 'in', vehicleId: 'v3', date: '2026-09-10' }),
      row({ id: 's1', vehicleId: 'v3', entity: 'recurring' }), // v3's schedules: none kept, so unknown
    ])
    await rows.putVehicles([{ id: 'v1', name: 'Octavia', logAccess: 'DELETE' }, { id: 'v3', name: 'Fabia', logAccess: 'DELETE' }])
    await rows.putLogs('refuelings', [{ id: 'r1', vehicleId: 'v1', date: '2026-09-01', version: 9, pulledAt: 1 }])
    await rows.putCursor({ vehicleId: 'v3', from: '2026-08-07', watermark: 'w', next: null, complete: true, fullStartedAt: null })
    const told = vi.fn()
    deviceData.onFoundChange(told)

    await foundRows.reconcile(rows, async () => undefined)

    expect((await rows.found()).map((r) => [r.id, r.checked ?? false]).sort()).toEqual([['in', true], ['old', false], ['r1', false], ['r2', true], ['s1', false]])
    expect(told).toHaveBeenCalledOnce() // what lists or counts them reads them again
  })

  it('sets aside only what this account made: its own vehicles with their schedules, the entries it logged', async () => {
    deviceData.reset(memoryStorage())
    await deviceData.signedIn('alice')
    const rows = (await deviceData.writableRows())!
    const alice = { id: 'alice', displayName: 'Alice' }
    const bob = { id: 'bob', displayName: 'Bob' }
    await rows.putVehicles([{ id: 'mine', name: 'Octavia', logAccess: 'DELETE', owner: alice }, { id: 'shared', name: 'Superb', logAccess: 'EDIT', owner: bob }])
    await rows.putLogs('refuelings', [
      { id: 'r-alice', vehicleId: 'shared', date: '2026-09-01', version: 1, pulledAt: 1, createdBy: alice },
      { id: 'r-bob', vehicleId: 'shared', date: '2026-09-02', version: 1, pulledAt: 1, createdBy: bob },
    ])
    await deviceData.keep(snapshotKey('RecurringExpenses', { vehicleId: 'shared' }), { vehicle: { id: 'shared', recurring: [{ id: 's-bob', version: 1, title: 'Oil' }] } })

    await foundRows.setAside(rows, keptAnswers, 'g1')

    // Sent by Alice, Bob's would be made hers, and Bob could not send them any more.
    expect((await rows.found()).map((r) => r.id).sort()).toEqual(['mine', 'r-alice'])
  })
})
