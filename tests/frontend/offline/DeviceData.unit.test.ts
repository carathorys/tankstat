import { IDBFactory } from 'fake-indexeddb'
import type { DocumentNode } from 'graphql'
import { afterEach, describe, expect, it, vi } from 'vitest'
import * as documents from '../../../src/frontend/gql/generated.ts'
import { ANONYMOUS_USER, deviceData, MAX_SNAPSHOTS, TABS_CHANNEL } from '../../../src/frontend/offline/deviceData.ts'
import { indexedDbStorage, memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import { documentId, userOf } from '../../../src/frontend/offline/offlineLink.ts'
import { KNOWN_QUERIES, policyFor, snapshotKey } from '../../../src/frontend/offline/snapshotPolicy.ts'

describe('indexedDbStorage', () => {
  it('keeps answers per user, in a database of their own, and remembers whose to open next time', async () => {
    const storage = indexedDbStorage(new IDBFactory())
    const alice = await storage.open('alice')
    await alice.put({ key: 'Welcome:{}', data: { n: 1 }, at: 1 })
    const bob = await storage.open('bob')

    expect(await alice.get('Welcome:{}')).toEqual({ key: 'Welcome:{}', data: { n: 1 }, at: 1 })
    expect(await bob.get('Welcome:{}')).toBeUndefined()

    expect(await storage.lastUser()).toBeNull()
    await storage.setLastUser('alice')
    expect(await storage.lastUser()).toBe('alice')
    await storage.setLastUser(null)
    expect(await storage.lastUser()).toBeNull()
  })

  it('a newer version of the database is not blocked by a tab that still has the old one open', async () => {
    const factory = new IDBFactory()
    const store = await indexedDbStorage(factory).open('alice') // the old tab

    const upgraded = await new Promise<IDBDatabase>((resolve, reject) => {
      const request = factory.open('tankstat-offline-alice', 99) // a new build, in another tab
      request.onsuccess = () => resolve(request.result)
      request.onerror = () => reject(request.error)
      request.onblocked = () => reject(new Error('blocked'))
    })

    expect(upgraded.version).toBe(99)
    upgraded.close()
    await expect(store.get('Welcome:{}')).rejects.toBeDefined() // the old tab keeps nothing more, and says so as a failure
  })

  it('pruning keeps the most recent answers', async () => {
    const store = await indexedDbStorage(new IDBFactory()).open('alice')
    for (const at of [5, 1, 4, 2, 3]) await store.put({ key: `k${at}`, data: at, at })

    await store.prune(2)

    expect(await store.get('k5')).toBeDefined()
    expect(await store.get('k4')).toBeDefined()
    for (const gone of ['k1', 'k2', 'k3']) expect(await store.get(gone)).toBeUndefined()
  })

  it('opens a database of the version before with its data, and a place for the pictures', async () => {
    const factory = new IDBFactory()
    const old = await new Promise<IDBDatabase>((resolve, reject) => {
      const request = factory.open('tankstat-offline-alice', 4) // as the build before kept it
      request.onupgradeneeded = () => {
        const db = request.result
        db.createObjectStore('snapshots', { keyPath: 'key' }).createIndex('at', 'at')
        db.createObjectStore('vehicles', { keyPath: 'id' })
        db.createObjectStore('refuelings', { keyPath: 'id' }).createIndex('vehicleId', 'vehicleId')
        db.createObjectStore('expenses', { keyPath: 'id' }).createIndex('vehicleId', 'vehicleId')
        db.createObjectStore('cursors', { keyPath: 'vehicleId' })
        db.createObjectStore('changes', { keyPath: 'id' })
        db.createObjectStore('photos', { keyPath: 'key' })
        request.transaction!.objectStore('vehicles').put({ id: 'v1', name: 'Octavia', logAccess: 'DELETE' })
      }
      request.onsuccess = () => resolve(request.result)
      request.onerror = () => reject(request.error)
    })
    old.close()

    const store = await indexedDbStorage(factory).open('alice')

    expect(await store.rows.vehicles()).toEqual([{ id: 'v1', name: 'Octavia', logAccess: 'DELETE' }])
    await store.rows.putPicture({ id: 'a'.repeat(32), type: 'image/webp', bytes: new Uint8Array([1]).buffer, keptAt: 1 })
    expect(await store.rows.pictureIds()).toEqual(['a'.repeat(32)])
  })
})

describe.each([
  ['indexedDbStorage', () => indexedDbStorage(new IDBFactory())],
  ['memoryStorage', () => memoryStorage()],
])('pictures in %s', (_, storage) => {
  const picture = (id: string) => ({ id, type: 'image/webp', bytes: new Uint8Array([1, 2, 3]).buffer, keptAt: 1 })

  it('keeps them per user, lists them without reading them, and removes them', async () => {
    const device = storage()
    const alice = await device.open('alice')
    const bob = await device.open('bob')
    await alice.rows.putPicture(picture('a'.repeat(32)))
    await alice.rows.putPicture(picture('b'.repeat(32)))

    expect((await alice.rows.pictureIds()).sort()).toEqual(['a'.repeat(32), 'b'.repeat(32)])
    expect(new Uint8Array((await alice.rows.picture('a'.repeat(32)))!.bytes)).toEqual(new Uint8Array([1, 2, 3]))
    expect(await bob.rows.pictureIds()).toEqual([]) // another account never sees them

    await alice.rows.deletePictures(['a'.repeat(32)])
    expect(await alice.rows.pictureIds()).toEqual(['b'.repeat(32)])
  })

  it('Remove offline data takes them too, but never the changes waiting for the server nor their photos', async () => {
    const alice = await storage().open('alice')
    await alice.rows.putPicture(picture('a'.repeat(32)))
    await alice.rows.putChanges([{ id: 'c1', seq: 1 } as never])
    await alice.rows.putPhoto({ key: 'local:1', vehicleId: 'v1', type: 'image/webp', bytes: new Uint8Array([1]).buffer, createdAt: 1 })

    await alice.clear()

    expect(await alice.rows.pictureIds()).toEqual([])
    expect(await alice.rows.changes()).toHaveLength(1)
    expect(await alice.rows.photo('local:1')).toBeDefined()
  })
})

describe('deviceData', () => {
  it('keeps nothing until the server said who is signed in, then keeps it for that user', async () => {
    const storage = memoryStorage()
    await storage.setLastUser('alice')
    deviceData.reset(storage)
    await deviceData.boot(storage)

    await deviceData.keep('Welcome:{}', { from: 'start' })
    expect(await deviceData.read('Welcome:{}')).toBeUndefined() // opened, but not confirmed: an answer may be someone else's

    await deviceData.signedIn('alice')
    await deviceData.keep('Welcome:{}', { from: 'alice' })
    expect((await deviceData.read('Welcome:{}'))?.data).toEqual({ from: 'alice' })
  })

  it('another user gets their own data; signing out leaves none open, now and at the next start', async () => {
    const storage = memoryStorage()
    deviceData.reset(storage)
    await deviceData.signedIn('alice')
    await deviceData.keep('Welcome:{}', 'alice')

    await deviceData.signedIn('bob')
    expect(await deviceData.read('Welcome:{}')).toBeUndefined()

    await deviceData.signedIn(null)
    await deviceData.keep('Welcome:{}', 'nobody')
    expect(await deviceData.read('Welcome:{}')).toBeUndefined()
    expect(await storage.lastUser()).toBeNull()

    await deviceData.boot(storage)
    expect(await deviceData.read('Welcome:{}')).toBeUndefined()
  })

  it('signing in or out stops keeping until the server says who it is', async () => {
    deviceData.reset(memoryStorage())
    await deviceData.signedIn('alice')
    deviceData.unconfirm()

    await deviceData.keep('Welcome:{}', 'whose?')

    expect(await deviceData.read('Welcome:{}')).toBeUndefined()
  })

  it('another tab signing someone else in stops this tab keeping, and asks it whose it is', async () => {
    const storage = memoryStorage()
    deviceData.reset(storage)
    await deviceData.boot(storage)
    await deviceData.signedIn('alice')
    const asked = vi.fn()
    deviceData.onSignedInElsewhere(asked)
    const otherTab = new BroadcastChannel(TABS_CHANNEL)
    try {
      otherTab.postMessage({ type: 'signedIn', user: 'alice' }) // the same account: nothing changes
      otherTab.postMessage({ type: 'signedIn', user: 'bob' })
      await vi.waitFor(() => expect(asked).toHaveBeenCalledTimes(1))

      await deviceData.keep('Welcome:{}', "bob's, answered to this tab's cookies")
      expect(await deviceData.read('Welcome:{}')).toBeUndefined()
    } finally {
      otherTab.close()
    }
  })

  it('never prunes the session and the UI settings, and makes room when the storage is full', async () => {
    const storage = memoryStorage()
    deviceData.reset(storage)
    await deviceData.signedIn('alice')
    await deviceData.keep('Session:{}', 'session', 1)
    await deviceData.keep('UiSettings:{}', 'settings', 2)
    for (let i = 0; i < MAX_SNAPSHOTS + 25; i++) await deviceData.keep(`Welcome:{"skip":${i}}`, i, 10 + i) // pruned every 25 answers

    expect((await deviceData.read('Session:{}'))?.data).toBe('session')
    expect((await deviceData.read('UiSettings:{}'))?.data).toBe('settings')
    expect(await deviceData.read('Welcome:{"skip":0}')).toBeUndefined() // the oldest of the others went

    const store = await storage.open('alice')
    const put = store.put.bind(store)
    let full = true
    store.put = async (snapshot) => {
      if (full) {
        full = false
        throw new DOMException('full', 'QuotaExceededError')
      }
      return put(snapshot)
    }
    await deviceData.signedIn(null)
    vi.spyOn(storage, 'open').mockResolvedValue(store)
    await deviceData.signedIn('alice')
    await deviceData.keep('Welcome:{"skip":999}', 'kept after making room', 9_999)
    expect((await deviceData.read('Welcome:{"skip":999}'))?.data).toBe('kept after making room')
    expect((await deviceData.read('Session:{}'))?.data).toBe('session')
  })

  it('does not answer from what was kept for another version of the query', async () => {
    deviceData.reset(memoryStorage())
    await deviceData.signedIn('alice')
    await deviceData.keep('VehicleDetails:{"id":"v1"}', { vehicle: { id: 'v1' } }, Date.now(), 'old-build')

    expect(await deviceData.read('VehicleDetails:{"id":"v1"}', 'new-build')).toBeUndefined()
    expect((await deviceData.read('VehicleDetails:{"id":"v1"}', 'old-build'))?.data).toEqual({ vehicle: { id: 'v1' } })
    expect(documentId(documents.VehicleDetailsDocument)).toBe(documentId(documents.VehicleDetailsDocument))
    expect(documentId(documents.VehicleDetailsDocument)).not.toBe(documentId(documents.WelcomeDocument))
  })
})

describe('userOf', () => {
  it('reads whose data a Session answer is', () => {
    expect(userOf({ session: { mode: 'STANDALONE', user: { id: 'u1' } } })).toBe('u1')
    expect(userOf({ session: { mode: 'NONE', user: null } })).toBe(ANONYMOUS_USER)
    expect(userOf({ session: { mode: 'OIDC', user: null } })).toBeNull()
    expect(userOf(null)).toBeNull()
  })
})

describe('snapshotPolicy', () => {
  const queries = Object.values(documents)
    .filter((value): value is DocumentNode => typeof value === 'object' && value !== null && (value as DocumentNode).kind === 'Document')
    .flatMap((doc) => doc.definitions)
    .flatMap((d) => (d.kind === 'OperationDefinition' && d.operation === 'query' && d.name ? [d.name.value] : []))

  it('has a decision for every query the app sends, so nothing ends up on the device by accident', () => {
    expect(queries.length).toBeGreaterThan(20)
    expect(queries.filter((name) => !KNOWN_QUERIES.includes(name))).toEqual([])
  })

  it('never answers the health check from the device, and keeps nothing it does not know', () => {
    expect(policyFor('Health')).toBe('never')
    expect(policyFor('SomethingNew')).toBe('never')
    expect(policyFor('Admin')).toBe('onlineOnly')
    expect(policyFor('Welcome')).toBe('keep')
  })

  it('finds the same answer whatever order the variables come in', () => {
    expect(snapshotKey('Refuelings', { take: 10, vehicleId: 'v1', page: { b: 1, a: [2, { y: 1, x: 2 }] } })).toBe(
      snapshotKey('Refuelings', { page: { a: [2, { x: 2, y: 1 }], b: 1 }, vehicleId: 'v1', take: 10 }),
    )
    expect(snapshotKey('Refuelings', { vehicleId: 'v1' })).not.toBe(snapshotKey('Refuelings', { vehicleId: 'v2' }))
    expect(snapshotKey('Session', undefined)).toBe('Session:{}')
  })
})

describe('deviceData, when things go wrong or change', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('keeps the data in IndexedDB where the browser has it, and only in memory where it has not', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    vi.stubGlobal('indexedDB', undefined)
    await deviceData.boot()
    await deviceData.signedIn('alice')
    await deviceData.keep('Welcome:{}', 'in memory')
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('no IndexedDB'))
    expect((await deviceData.read('Welcome:{}'))?.data).toBe('in memory')

    const factory = new IDBFactory()
    vi.stubGlobal('indexedDB', factory)
    deviceData.reset()
    await deviceData.boot()
    await deviceData.signedIn('alice')
    await deviceData.keep('Welcome:{}', 'in IndexedDB')
    expect(await indexedDbStorage(factory).lastUser()).toBe('alice') // remembered for the next start
    expect((await (await indexedDbStorage(factory).open('alice')).get('Welcome:{}'))?.data).toBe('in IndexedDB')
  })

  it('works on, keeping nothing, when the data cannot be opened at start or for the user signing in', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    const storage = memoryStorage()
    vi.spyOn(storage, 'lastUser').mockRejectedValue(new Error('broken'))
    await deviceData.boot(storage)
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('cannot be opened'), expect.any(Error))
    expect(await deviceData.rows()).toBeNull()

    const failing = memoryStorage()
    vi.spyOn(failing, 'open').mockRejectedValue(new Error('broken'))
    deviceData.reset(failing)
    await expect(deviceData.signedIn('alice')).resolves.toBeUndefined()
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('cannot be switched'), expect.any(Error))
    expect(deviceData.user).toBeNull()
  })

  it('tells the download only when another user is confirmed, and the outbox whenever other data is open', async () => {
    deviceData.reset(memoryStorage())
    const confirmed = vi.fn()
    const opened = vi.fn()
    const stopConfirmed = deviceData.subscribe(confirmed)
    const stopOpened = deviceData.onStoreChange(opened)

    await deviceData.signedIn('alice')
    await deviceData.signedIn('alice')
    expect(confirmed).toHaveBeenCalledTimes(1)
    expect(opened).toHaveBeenCalledTimes(1)
    expect(deviceData.user).toBe('alice')

    stopOpened()
    await deviceData.signedIn('bob')
    expect(confirmed).toHaveBeenCalledTimes(2)
    expect(opened).toHaveBeenCalledTimes(1) // no longer listening

    await deviceData.signedIn(null)
    expect(deviceData.user).toBeNull()
    expect(confirmed).toHaveBeenCalledTimes(3)

    stopConfirmed()
    await deviceData.signedIn('carol')
    expect(confirmed).toHaveBeenCalledTimes(3)
  })

  it('downloads only into the data of a confirmed user', async () => {
    const storage = memoryStorage()
    await storage.setLastUser('alice')
    deviceData.reset(storage)
    expect(await deviceData.writableRows()).toBeNull() // nothing open

    await deviceData.boot(storage)
    expect(await deviceData.rows()).not.toBeNull() // the last user's, to answer from
    expect(await deviceData.writableRows()).toBeNull() // but not to write into before the server said whose it is

    await deviceData.signedIn('alice')
    expect(await deviceData.writableRows()).toBe(await deviceData.rows())

    await deviceData.signedIn(null) // confirmed: nobody
    expect(await deviceData.writableRows()).toBeNull()
  })

  it('removing the data waits for the answers on their way and empties the confirmed user\'s', async () => {
    deviceData.reset(memoryStorage())
    await deviceData.signedIn('alice')
    await (await deviceData.rows())!.putVehicles([{ id: 'v1', name: 'Octavia', logAccess: 'DELETE' }])
    void deviceData.keep('Welcome:{}', 'on its way')

    await deviceData.removeAll()
    await deviceData.settled()

    expect(await deviceData.read('Welcome:{}')).toBeUndefined()
    expect(await (await deviceData.rows())!.vehicles()).toEqual([])
  })

  it('works without other tabs to hear from where the browser has no BroadcastChannel', async () => {
    vi.stubGlobal('BroadcastChannel', undefined)
    const storage = memoryStorage()
    await storage.setLastUser('alice')
    deviceData.reset(storage)

    await deviceData.boot(storage)
    await deviceData.signedIn('alice')
    await deviceData.keep('Welcome:{}', 'kept')

    expect(deviceData.user).toBe('alice')
    expect((await deviceData.read('Welcome:{}'))?.data).toBe('kept')
  })

  it('answers nothing when a kept answer cannot be read, and says so when one cannot be written', async () => {
    const storage = memoryStorage()
    const store = await storage.open('alice')
    vi.spyOn(storage, 'open').mockResolvedValue(store)
    deviceData.reset(storage)
    await deviceData.signedIn('alice')
    await deviceData.keep('Welcome:{}', 'kept')

    vi.spyOn(store, 'get').mockRejectedValue(new Error('broken'))
    expect(await deviceData.read('Welcome:{}')).toBeUndefined()

    vi.spyOn(store, 'put').mockRejectedValue(new Error('disk failure'))
    await expect(deviceData.keep('Welcome:{"skip":1}', 'lost')).rejects.toThrow('disk failure')
  })

  it('removes nothing while the server has not said whose the data is', async () => {
    const storage = memoryStorage()
    deviceData.reset(storage)
    await deviceData.removeAll() // nothing open: nothing to do
    await deviceData.signedIn('alice')
    await deviceData.keep('Welcome:{}', 'kept')
    deviceData.unconfirm()

    await deviceData.removeAll()

    expect((await (await storage.open('alice')).get('Welcome:{}'))?.data).toBe('kept')
  })

  it('pays no heed to other messages of other tabs, nor to one naming whoever is signed in here', async () => {
    deviceData.reset(memoryStorage())
    await deviceData.boot(memoryStorage())
    const asked = vi.fn()
    const stop = deviceData.onSignedInElsewhere(asked)
    const otherTab = new BroadcastChannel(TABS_CHANNEL)
    try {
      otherTab.postMessage(null)
      otherTab.postMessage({ type: 'somethingElse', user: 'bob' })
      otherTab.postMessage({ type: 'signedIn' })
      otherTab.postMessage({ type: 'signedIn', user: null }) // nobody, as here
      otherTab.postMessage({ type: 'signedIn', user: 'carol' })
      await vi.waitFor(() => expect(asked).toHaveBeenCalledTimes(1)) // carol only

      stop()
      const later = vi.fn()
      deviceData.onSignedInElsewhere(later)
      otherTab.postMessage({ type: 'signedIn', user: 'dave' })
      await vi.waitFor(() => expect(later).toHaveBeenCalledTimes(1))
      expect(asked).toHaveBeenCalledTimes(1) // no longer listening
    } finally {
      otherTab.close()
    }
  })
})
