import { indexedDbStorage, memoryStorage, type DeviceStorage, type DeviceStore, type RowStore, type Snapshot } from './deviceStorage.ts'

/** Whose data the device keeps when sign-in is off (`Auth:Mode=None`): every visitor is the same anonymous user. */
export const ANONYMOUS_USER = 'anonymous'

/** At most this many answers are kept per user; the oldest go first. What was viewed bounds it, not how much data there is. */
export const MAX_SNAPSHOTS = 300
const PRUNE_EVERY = 25

/** Which history the server's data belonged to when this device downloaded it (`Session.instance.generation`); kept with the answers. */
export const GENERATION_KEY = 'meta:generation'

/** Asked once per page load and needed to open the app offline: never pruned, however old. */
const spared = (key: string) => key.startsWith('Session:') || key.startsWith('UiSettings:') || key === GENERATION_KEY

/** The tabs of this site tell each other who the server said is signed in (the cookies, and so the account, are shared). */
export const TABS_CHANNEL = 'tankstat-offline'

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
let tabs: BroadcastChannel | null = null
const elsewhere = new Set<() => void>()
const listeners = new Set<() => void>()
/**
 * Told whenever another user's data (or none) is open, or what it held was removed: what is read from it must be read again (`outbox.ts`,
 * `keptPictures.ts`).
 */
const storeListeners = new Set<() => void>()
/** Told when the server's history changed: before the device drops what it holds (to set it aside), and after (to download afresh). */
type GenerationListener = (phase: 'before' | 'after', change: { from: string; to: string }) => Promise<void> | void
const generationListeners = new Set<GenerationListener>()

let generationTail: Promise<unknown> = Promise.resolve()

/** One check of the server's history at a time: in this tab by chaining, across the tabs of this site by a Web Lock (where there is one). */
function oneGenerationCheckAtATime<T>(work: () => Promise<T>): Promise<T> {
  const run = () => {
    const locks = globalThis.navigator?.locks
    return locks ? (locks.request('tankstat-generation', work) as Promise<T>) : work()
  }
  const result = generationTail.then(run, run)
  generationTail = result.catch(() => undefined)
  return result
}

async function openFor(user: string | null) {
  if (store?.user === user) return
  store?.close()
  store = null
  if (user !== null) store = await storage.open(user)
  storeListeners.forEach((listener) => listener())
}

/**
 * Another tab heard from the server that someone else is signed in now (or nobody): what this tab gets from now on may be that account's,
 * so nothing is kept until this tab's own `Session` answer says whose it is (the listeners ask it again).
 */
function listenToOtherTabs() {
  if (tabs || typeof BroadcastChannel === 'undefined') return
  tabs = new BroadcastChannel(TABS_CHANNEL)
  tabs.onmessage = (event: MessageEvent<{ type?: string; user?: string | null }>) => {
    if (event.data?.type !== 'signedIn' || event.data.user === undefined || event.data.user === (store?.user ?? null)) return
    confirmed = false
    elsewhere.forEach((listener) => listener())
  }
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
    listenToOtherTabs()
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
    tabs?.postMessage({ type: 'signedIn', user })
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

  /** Told whenever another user's data (or none) is open, or what it held was removed (`removeAll`). */
  onStoreChange(listener: () => void): () => void {
    storeListeners.add(listener)
    return () => {
      storeListeners.delete(listener)
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

  /** Told when another tab heard that someone else is signed in: ask the server whose this tab is. */
  onSignedInElsewhere(listener: () => void): () => void {
    elsewhere.add(listener)
    return () => {
      elsewhere.delete(listener)
    }
  },

  /** The kept answer; with `doc` (`documentId`), only one kept for that version of the query (an older build's may lack fields). */
  async read(key: string, doc?: string): Promise<Snapshot | undefined> {
    await switching
    const kept = await store?.get(key).catch(() => undefined)
    return kept && doc && kept.doc && kept.doc !== doc ? undefined : kept
  },

  keep(key: string, data: unknown, at = Date.now(), doc?: string): Promise<void> {
    const write = (async () => {
      await switching
      if (!store || !confirmed) return
      const snapshot = { key, data, at, doc }
      try {
        await store.put(snapshot)
      } catch (error) {
        if ((error as { name?: unknown } | null)?.name !== 'QuotaExceededError') throw error
        // The site's storage is full: the older half goes, and the answer is kept after all.
        await store.prune(MAX_SNAPSHOTS / 2, spared)
        await store.put(snapshot)
      }
      if (++puts % PRUNE_EVERY === 0) await store.prune(MAX_SNAPSHOTS, spared)
    })()
    writes = Promise.allSettled([writes, write])
    return write
  },

  /**
   * Removes everything this device keeps for the signed-in user (the next download brings their window again), the rows set aside when
   * the server's history changed too, and tells what read from it (the pictures on screen are its own until then).
   */
  async removeAll(): Promise<void> {
    await switching
    await writes
    if (!store || !confirmed) return
    await store.clear()
    await store.rows.clearFound()
    storeListeners.forEach((listener) => listener())
  },

  /**
   * The server said which history its data belongs to (every `Session` answer): the first one seen is adopted as it is; another one than
   * the one kept means a restore replaced the data, so what this device downloaded refers to ids and versions of another history. Then the
   * listeners set it aside, everything downloaded and every kept answer goes (never the changes waiting, their photos, nor the rows set
   * aside), the new generation is kept, and the listeners download afresh. One check at a time, in this tab and across the tabs (a
   * `Session` answer reaches every open tab at about the same time): each reads the kept generation inside its turn, so the second finds
   * the new one kept and does nothing, and never wipes what the first just downloaded.
   */
  generationSeen(generation: string | undefined): Promise<'same' | 'adopted' | 'changed'> {
    if (!generation) return Promise.resolve('same')
    return oneGenerationCheckAtATime(async () => {
      await switching
      if (!store || !confirmed) return 'same'
      const kept = (await store.get(GENERATION_KEY).catch(() => undefined))?.data
      if (kept === generation) return 'same'
      if (typeof kept !== 'string') {
        await store.put({ key: GENERATION_KEY, data: generation, at: Date.now() })
        return 'adopted'
      }
      const change = { from: kept, to: generation }
      for (const listener of generationListeners) await listener('before', change)
      await writes
      await store.clear()
      await store.put({ key: GENERATION_KEY, data: generation, at: Date.now() })
      storeListeners.forEach((listener) => listener())
      for (const listener of generationListeners) await listener('after', change)
      return 'changed'
    })
  },

  onGenerationChange(listener: GenerationListener): () => void {
    generationListeners.add(listener)
    return () => {
      generationListeners.delete(listener)
    }
  },

  /** Tests: waits until the answers on their way to the device are kept. */
  async settled(): Promise<void> {
    await switching
    await writes
  },

  /** Tests: a fresh, empty device. */
  reset(chosen: DeviceStorage = memoryStorage()): void {
    store?.close()
    tabs?.close()
    tabs = null
    elsewhere.clear()
    storage = chosen
    store = null
    confirmed = false
    switching = Promise.resolve()
    writes = Promise.resolve()
    puts = 0
    listeners.clear()
    generationListeners.clear()
    storeListeners.forEach((listener) => listener())
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
