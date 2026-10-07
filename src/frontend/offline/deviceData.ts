import { indexedDbStorage, memoryStorage, type DeviceStorage, type DeviceStore, type RowStore, type Snapshot } from './deviceStorage.ts'

/** Whose data the device keeps when sign-in is off (`Auth:Mode=None`): every visitor is the same anonymous user. */
export const ANONYMOUS_USER = 'anonymous'

/** At most this many answers are kept per user; the oldest go first. What was viewed bounds it, not how much data there is. */
export const MAX_SNAPSHOTS = 300
const PRUNE_EVERY = 25

let storage: DeviceStorage = memoryStorage()
let store: DeviceStore | null = null
/**
 * Whether the server said in this page's life who is signed in. Until then the store opened at start (the last user's) may answer while
 * the server is out of reach, but nothing is written into it: an answer for another account must never land in this one's data.
 */
let confirmed = false
let switching: Promise<void> = Promise.resolve()
let puts = 0
let writes: Promise<unknown> = Promise.resolve()
const listeners = new Set<() => void>()

async function openFor(user: string | null) {
  if (store?.user === user) return
  store?.close()
  store = null
  if (user !== null) store = await storage.open(user)
}

/**
 * The data this device keeps for the signed-in user (see `deviceStorage.ts`). The offline link reads and writes it; `boot` opens the last
 * user's at start and every `Session` answer says whose it is.
 */
export const deviceData = {
  /** At start, before the first request: the last user's data, so the app opens with what it showed last time. */
  async boot(chosen?: DeviceStorage): Promise<void> {
    storage = chosen ?? pickStorage()
    confirmed = false
    try {
      await openFor(await storage.lastUser())
    } catch (error) {
      console.warn('Offline data cannot be opened; the app works, but nothing is kept on this device.', error)
      storage = memoryStorage()
      store = null
    }
  },

  /** The server said who is signed in (null: nobody): their data from now on, and at the next start. */
  signedIn(user: string | null): Promise<void> {
    switching = switching.then(async () => {
      const before = confirmed ? store?.user : undefined
      await openFor(user)
      confirmed = true
      await storage.setLastUser(user)
      if (before !== store?.user) listeners.forEach((listener) => listener())
    }).catch((error) => console.warn('Offline data cannot be switched to the signed-in user.', error))
    return switching
  },

  /** The confirmed user's id, once a `Session` answer of this page said who it is. */
  get user(): string | null {
    return confirmed ? (store?.user ?? null) : null
  },

  /** Told when the server confirmed another user (or nobody): the download starts for them. */
  subscribe(listener: () => void): () => void {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },

  /** The downloaded window, to answer from (the last user's too, before the server confirmed them). */
  async rows(): Promise<RowStore | null> {
    await switching
    return store?.rows ?? null
  },

  /** The downloaded window, to download into: only the confirmed user's. */
  async writableRows(): Promise<RowStore | null> {
    await switching
    return confirmed ? (store?.rows ?? null) : null
  },

  /** Someone is signing in or out: until the server says who it is, nothing is kept. */
  unconfirm(): void {
    confirmed = false
  },

  async read(key: string): Promise<Snapshot | undefined> {
    await switching
    return store?.get(key).catch(() => undefined)
  },

  keep(key: string, data: unknown, at = Date.now()): Promise<void> {
    const write = (async () => {
      await switching
      if (!store || !confirmed) return
      await store.put({ key, data, at })
      if (++puts % PRUNE_EVERY === 0) await store.prune(MAX_SNAPSHOTS)
    })()
    writes = Promise.allSettled([writes, write])
    return write
  },

  /** Tests: waits until the answers on their way to the device are kept. */
  async settled(): Promise<void> {
    await switching
    await writes
  },

  /** Tests: a fresh, empty device. */
  reset(chosen: DeviceStorage = memoryStorage()): void {
    store?.close()
    storage = chosen
    store = null
    confirmed = false
    switching = Promise.resolve()
    writes = Promise.resolve()
    puts = 0
    listeners.clear()
  },
}

function pickStorage(): DeviceStorage {
  if (typeof indexedDB === 'undefined') {
    console.warn('This browser keeps no offline data (no IndexedDB); the app works, but only online.')
    return memoryStorage()
  }
  return indexedDbStorage()
}

export type { Snapshot }
