import { IDBFactory } from 'fake-indexeddb'
import type { DocumentNode } from 'graphql'
import { describe, expect, it } from 'vitest'
import * as documents from '../../../src/frontend/gql/generated.ts'
import { ANONYMOUS_USER, deviceData } from '../../../src/frontend/offline/deviceData.ts'
import { indexedDbStorage, memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import { userOf } from '../../../src/frontend/offline/offlineLink.ts'
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
