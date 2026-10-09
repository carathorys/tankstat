import { IDBFactory, IDBObjectStore } from 'fake-indexeddb'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { Change } from '../../../src/frontend/offline/changes.ts'
import { deviceData } from '../../../src/frontend/offline/deviceData.ts'
import { indexedDbStorage, memoryStorage, type KeptPhoto, type LogRow, type PullCursor } from '../../../src/frontend/offline/deviceStorage.ts'

afterEach(() => {
  vi.restoreAllMocks()
})

const log = (id: string, vehicleId: string, fields: Partial<LogRow> = {}): LogRow => ({ id, vehicleId, date: '2026-09-01', pulledAt: 1, ...fields })
const cursor = (vehicleId: string): PullCursor => ({ vehicleId, from: null, watermark: 'w', next: null, complete: true, fullStartedAt: null })
const change = (id: string, seq: number): Change => ({ id, seq, createdAt: seq, entity: 'refuelings', action: 'add', vehicleId: 'v1', targetId: id })
const photo = (key: string): KeptPhoto => ({ key, vehicleId: 'v1', type: 'image/jpeg', bytes: new Uint8Array([1, 2, 3]).buffer, createdAt: 1 })

/** Opens a database the way an older build (or another tab) did, outside the app's code. */
function openRaw(factory: IDBFactory, name: string, version: number, upgrade: (db: IDBDatabase) => void = () => undefined): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = factory.open(name, version)
    request.onupgradeneeded = () => upgrade(request.result)
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error)
    request.onblocked = () => reject(new Error('blocked'))
  })
}

/** Lets the database's own tasks run (fake-indexeddb schedules them as macrotasks). */
const tasks = () => new Promise<void>((resolve) => setTimeout(resolve, 10))

describe('the downloaded window in IndexedDB', () => {
  it('keeps the logs by vehicle, the vehicles, the cursors, the changes and the photos', async () => {
    const { rows } = await indexedDbStorage(new IDBFactory()).open('alice')
    await rows.putVehicles([{ id: 'v1', name: 'Octavia', logAccess: 'DELETE' }, { id: 'v2', name: 'Astra', logAccess: 'VIEW' }])
    await rows.putLogs('refuelings', [log('a', 'v1'), log('b', 'v1'), log('c', 'v2')])
    await rows.putLogs('expenses', [log('e', 'v1')])
    await rows.putLogs('refuelings', []) // nothing to write: nothing happens
    await rows.putCursor(cursor('v1'))
    await rows.putCursor(cursor('v2'))
    await rows.putChanges([change('second', 2), change('first', 1)])
    await rows.putPhoto(photo('local:p1'))

    expect((await rows.logs('refuelings', 'v1')).map((r) => r.id).sort()).toEqual(['a', 'b'])
    expect((await rows.allLogs('refuelings')).map((r) => r.id).sort()).toEqual(['a', 'b', 'c'])
    expect(await rows.log('expenses', 'e')).toMatchObject({ vehicleId: 'v1' })
    expect((await rows.vehicles()).map((v) => v.name).sort()).toEqual(['Astra', 'Octavia'])
    expect(await rows.cursor('v2')).toEqual(cursor('v2'))
    expect((await rows.changes()).map((c) => c.id)).toEqual(['first', 'second']) // in the order they were made
    expect(new Uint8Array((await rows.photo('local:p1'))!.bytes)).toEqual(new Uint8Array([1, 2, 3]))

    await rows.deleteLogs('refuelings', ['a', 'unknown']) // one the device never had is no matter
    await rows.deleteLogs('refuelings', [])
    expect((await rows.logs('refuelings', 'v1')).map((r) => r.id)).toEqual(['b']) // the copy in memory follows

    await rows.dropVehicle('v1')
    expect(await rows.logs('refuelings', 'v1')).toEqual([])
    expect(await rows.logs('expenses', 'v1')).toEqual([])
    expect((await rows.vehicles()).map((v) => v.id)).toEqual(['v2'])
    expect((await rows.cursors()).map((c) => c.vehicleId)).toEqual(['v2'])

    await rows.deleteChanges(['first'])
    await rows.deletePhotos(['local:p1'])
    expect((await rows.changes()).map((c) => c.id)).toEqual(['second'])
    expect(await rows.photos()).toEqual([])
  })

  it('removing the data keeps the changes waiting for the server and their photos', async () => {
    const store = await indexedDbStorage(new IDBFactory()).open('alice')
    await store.put({ key: 'Welcome:{}', data: 1, at: 1 })
    await store.rows.putVehicles([{ id: 'v1', name: 'Octavia', logAccess: 'DELETE' }])
    await store.rows.putLogs('refuelings', [log('a', 'v1')])
    expect(await store.rows.logs('refuelings', 'v1')).toHaveLength(1) // read once, so held in memory too
    await store.rows.putCursor(cursor('v1'))
    await store.rows.putChanges([change('c1', 1)])
    await store.rows.putPhoto(photo('local:p1'))

    await store.clear()

    expect(await store.get('Welcome:{}')).toBeUndefined()
    expect(await store.rows.vehicles()).toEqual([])
    expect(await store.rows.logs('refuelings', 'v1')).toEqual([])
    expect(await store.rows.cursors()).toEqual([])
    expect((await store.rows.changes()).map((c) => c.id)).toEqual(['c1'])
    expect((await store.rows.photos()).map((p) => p.key)).toEqual(['local:p1'])
  })

  it('pruning leaves alone what fits, and never removes what it is told to spare', async () => {
    const store = await indexedDbStorage(new IDBFactory()).open('alice')
    for (const at of [1, 2, 3, 4]) await store.put({ key: at === 1 ? 'Session:{}' : `k${at}`, data: at, at })

    await store.prune(10)
    expect(await store.get('k2')).toBeDefined() // nothing to make room for

    await store.prune(2, (key) => key.startsWith('Session:'))
    expect(await store.get('Session:{}')).toBeDefined() // the oldest, but spared
    expect(await store.get('k2')).toBeUndefined()
    expect(await store.get('k3')).toBeUndefined()
    expect(await store.get('k4')).toBeDefined()
  })

  it('a write that fails fails as a promise, and nothing of it is kept', async () => {
    const store = await indexedDbStorage(new IDBFactory()).open('alice')
    await store.put({ key: 'k1', data: 1, at: 1 })

    await expect(store.prune(0, () => { throw new Error('spare check failed') })).rejects.toBeDefined()
    expect(await store.get('k1')).toBeDefined()

    const put = IDBObjectStore.prototype.put
    vi.spyOn(IDBObjectStore.prototype, 'put').mockImplementation(function (this: IDBObjectStore, ...args: Parameters<IDBObjectStore['put']>) {
      const request = put.apply(this, args)
      this.transaction.abort() // the browser gave up on the transaction
      return request
    })
    await expect(store.put({ key: 'k2', data: 2, at: 2 })).rejects.toThrow('IndexedDB transaction failed')
    vi.restoreAllMocks()
    expect(await store.get('k2')).toBeUndefined()
  })

  it('a read that fails is answered as nothing kept', async () => {
    deviceData.reset(indexedDbStorage(new IDBFactory()))
    await deviceData.signedIn('alice')
    await deviceData.keep('Welcome:{}', { n: 1 })
    expect((await deviceData.read('Welcome:{}'))?.data).toEqual({ n: 1 })

    const get = IDBObjectStore.prototype.get
    vi.spyOn(IDBObjectStore.prototype, 'get').mockImplementation(function (this: IDBObjectStore, ...args: Parameters<IDBObjectStore['get']>) {
      const request = get.apply(this, args)
      this.transaction.abort()
      return request
    })

    expect(await deviceData.read('Welcome:{}')).toBeUndefined()
  })

  it('when the storage is full, the older answers go and the new one is kept after all', async () => {
    deviceData.reset(indexedDbStorage(new IDBFactory()))
    await deviceData.signedIn('alice')
    await deviceData.keep('Session:{}', 'session', 1)
    await deviceData.keep('Welcome:{"skip":0}', 'old', 2)

    const put = IDBObjectStore.prototype.put
    let full = true
    vi.spyOn(IDBObjectStore.prototype, 'put').mockImplementation(function (this: IDBObjectStore, ...args: Parameters<IDBObjectStore['put']>) {
      const request = put.apply(this, args)
      if (full) {
        full = false
        // What a browser does when a write would go over the site's quota: the transaction aborts with a QuotaExceededError.
        ;(this.transaction as unknown as { _abort(name: string): void })._abort('QuotaExceededError')
      }
      return request
    })
    await deviceData.keep('Welcome:{"skip":1}', 'new', 3)

    expect((await deviceData.read('Welcome:{"skip":1}'))?.data).toBe('new')
    expect((await deviceData.read('Session:{}'))?.data).toBe('session')
  })
})

describe('opening the IndexedDB data', () => {
  it('upgrades the database of an older build and keeps what it held', async () => {
    const factory = new IDBFactory()
    const first = await openRaw(factory, 'tankstat-offline-alice', 1, (db) => {
      db.createObjectStore('snapshots', { keyPath: 'key' }).createIndex('at', 'at')
    })
    await new Promise<void>((resolve) => {
      const tx = first.transaction('snapshots', 'readwrite')
      tx.objectStore('snapshots').put({ key: 'Welcome:{}', data: 'from build 1', at: 1 })
      tx.oncomplete = () => resolve()
    })
    first.close()

    const store = await indexedDbStorage(factory).open('alice')
    expect((await store.get('Welcome:{}'))?.data).toBe('from build 1')
    await store.rows.putPhoto(photo('local:p1'))
    expect(await store.rows.photos()).toHaveLength(1)

    // A build that had the changes but not the photos (3).
    const third = await openRaw(factory, 'tankstat-offline-bob', 3, (db) => {
      db.createObjectStore('snapshots', { keyPath: 'key' }).createIndex('at', 'at')
      db.createObjectStore('vehicles', { keyPath: 'id' })
      db.createObjectStore('refuelings', { keyPath: 'id' }).createIndex('vehicleId', 'vehicleId')
      db.createObjectStore('expenses', { keyPath: 'id' }).createIndex('vehicleId', 'vehicleId')
      db.createObjectStore('cursors', { keyPath: 'vehicleId' })
      db.createObjectStore('changes', { keyPath: 'id' })
    })
    third.close()
    const bob = await indexedDbStorage(factory).open('bob')
    await bob.rows.putChanges([change('c1', 1)])
    await bob.rows.putPhoto(photo('local:p2'))
    expect((await bob.rows.changes()).map((c) => c.id)).toEqual(['c1'])
    expect((await bob.rows.photos()).map((p) => p.key)).toEqual(['local:p2'])
  })

  it('a database a newer build upgraded cannot be opened by an older one, and the app keeps nothing', async () => {
    const factory = new IDBFactory()
    ;(await openRaw(factory, 'tankstat-offline-alice', 99)).close()
    const storage = indexedDbStorage(factory)
    await storage.setLastUser('alice')

    await expect(storage.open('alice')).rejects.toBeDefined()

    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    await deviceData.boot(storage)
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('Offline data cannot be opened'), expect.anything())
    expect(await deviceData.rows()).toBeNull()
  })

  it('gives up on a tab of an old build that will not let go, and lets go itself when that tab does', async () => {
    const factory = new IDBFactory()
    const oldTab = await openRaw(factory, 'tankstat-offline-alice', 1, (db) => {
      db.createObjectStore('snapshots', { keyPath: 'key' }).createIndex('at', 'at')
    }) // no onversionchange: it never closes by itself
    const waits: number[] = []
    vi.spyOn(globalThis, 'setTimeout').mockImplementation(((fn: () => void, ms?: number) => {
      waits.push(ms ?? 0)
      fn() // the wait is over at once
      return 0
    }) as unknown as typeof setTimeout)

    await expect(indexedDbStorage(factory).open('alice')).rejects.toThrow('IndexedDB is blocked by another tab')
    expect(waits).toEqual([3_000])
    vi.restoreAllMocks()

    oldTab.close() // the open goes through now, too late: the connection closes at once
    await tasks()
    const newer = await openRaw(factory, 'tankstat-offline-alice', 5) // a later build is not blocked: nothing holds the database open
    expect(newer.version).toBe(5)
    newer.close()
  })
})

describe('the same in memory', () => {
  it('pruning without anything to spare keeps the most recent answers', async () => {
    const store = await memoryStorage().open('alice')
    for (const at of [3, 1, 2]) await store.put({ key: `k${at}`, data: at, at })

    await store.prune(1)

    expect(await store.get('k3')).toBeDefined()
    expect(await store.get('k1')).toBeUndefined()
    expect(await store.get('k2')).toBeUndefined()
  })

  it('removing the data keeps the changes waiting and their photos, and the user\'s data is there when opened again', async () => {
    const storage = memoryStorage()
    const store = await storage.open('alice')
    await store.put({ key: 'Welcome:{}', data: 1, at: 1 })
    await store.rows.putLogs('refuelings', [log('a', 'v1')])
    await store.rows.putChanges([change('c1', 1)])
    await store.rows.putPhoto(photo('local:p1'))

    await store.clear()

    const again = await storage.open('alice')
    expect(await again.get('Welcome:{}')).toBeUndefined()
    expect(await again.rows.allLogs('refuelings')).toEqual([])
    expect((await again.rows.changes()).map((c) => c.id)).toEqual(['c1'])
    expect((await again.rows.photos()).map((p) => p.key)).toEqual(['local:p1'])
  })
})
