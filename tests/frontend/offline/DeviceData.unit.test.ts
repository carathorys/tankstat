import { IDBFactory } from 'fake-indexeddb'
import type { DocumentNode } from 'graphql'
import { describe, expect, it, vi } from 'vitest'
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
