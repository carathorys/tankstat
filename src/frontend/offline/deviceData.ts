import { indexedDbStorage, memoryStorage, type DeviceStorage, type DeviceStore, type RowStore, type SignedOutHere, type Snapshot } from './deviceStorage.ts'

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
/** Told when the rows set aside changed (`foundRows.ts`): what lists or counts them reads them again. */
const foundListeners = new Set<() => void>()

let generationTail: Promise<unknown> = Promise.resolve()
/**
 * The history this tab last saw kept for the open user's data: when another tab started afresh for a new one, what this tab holds in
 * memory (the copies of the logs) is of the old history and goes too.
 */
let seenGeneration: string | undefined

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
  seenGeneration = undefined
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
    // Signed out in another tab: the account's database is being deleted, so this tab lets go of it at once (and reads nothing more of it).
    if (event.data.user === null) switching = switching.then(() => openFor(null)).catch(() => undefined)
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
   * Signing out (#162): everything this device keeps for the open account goes, its whole database (the answers, the downloaded window,
   * the pictures, the changes waiting and their photos, the rows set aside), nobody's data is open, and nobody is the last user. What read
   * from it is told (the outbox, the pictures, the counts), and so are the other tabs. A database another tab will not let go of is left
   * with a warning: the sign-out itself still happens.
   */
  signOut(): Promise<void> {
    switching = switching.then(async () => {
      await writes
      const user = store?.user ?? null
      store?.close()
      store = null
      confirmed = false
      seenGeneration = undefined
      if (user !== null) await storage.delete(user).catch((error) => console.warn('The offline data of the account that signed out cannot be removed.', error))
      await storage.setLastUser(null).catch(() => undefined)
      storeListeners.forEach((listener) => listener())
      foundListeners.forEach((listener) => listener())
      listeners.forEach((listener) => listener())
    })
    tabs?.postMessage({ type: 'signedIn', user: null })
    return switching
  },

  /** Whether this device was signed out while the server could not end its session (`SignedOutHere`); null when it was not. */
  signedOutHere(): Promise<SignedOutHere | null> {
    return storage.signedOutHere().catch(() => null)
  },

  setSignedOutHere(signedOut: SignedOutHere | null): Promise<void> {
    return storage.setSignedOutHere(signedOut).catch((error) => console.warn('This device cannot remember that it signed out.', error))
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
    foundListeners.forEach((listener) => listener())
  },

  /**
   * The server said which history its data belongs to (every `Session` answer, and every download before it writes anything): the first
   * one seen is adopted as it is; another one than the one kept means a restore replaced the data, so what this device downloaded refers
   * to ids and versions of another history. Then the listeners set it aside (one that cannot throws, and nothing goes), everything
   * downloaded and every kept answer goes (never the changes waiting, their photos, nor the rows set aside), the new generation is kept,
   * and the listeners download afresh. One check at a time, in this tab and across the tabs (a `Session` answer reaches every open tab at
   * about the same time): each reads the kept generation inside its turn, so the second finds the new one kept and does nothing, and never
   * wipes what the first just downloaded; a tab that finds it so drops its own copies in memory of the old history.
   */
  generationSeen(generation: string | undefined): Promise<'same' | 'adopted' | 'changed'> {
    if (!generation) return Promise.resolve('same')
    return oneGenerationCheckAtATime(async () => {
      await switching
      const open = store
      if (!open || !confirmed) return 'same'
      const kept = (await open.get(GENERATION_KEY).catch(() => undefined))?.data
      if (kept === generation) {
        // Another tab started afresh for this history: the copies of the logs this tab holds in memory are of the old one.
        if (seenGeneration !== undefined && seenGeneration !== generation) {
          open.rows.forget()
          storeListeners.forEach((listener) => listener())
        }
        seenGeneration = generation
        return 'same'
      }
      if (typeof kept !== 'string') {
        await open.put({ key: GENERATION_KEY, data: generation, at: Date.now() })
        seenGeneration = generation
        return 'adopted'
      }
      const change = { from: kept, to: generation }
      for (const listener of generationListeners) await listener('before', change)
      // Signed out, or someone else signed in, while the rows were set aside: nothing is dropped (the next answer checks again).
      if (store !== open || !confirmed) return 'same'
      await writes
      await open.clear()
      await open.put({ key: GENERATION_KEY, data: generation, at: Date.now() })
      seenGeneration = generation
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

  /** Told when the rows set aside changed (`foundRows.ts`): what lists or counts them reads them again. */
  onFoundChange(listener: () => void): () => void {
    foundListeners.add(listener)
    return () => {
      foundListeners.delete(listener)
    }
  },

  /** The rows set aside changed (a download judged them, or the person sent or forgot some). */
  foundChanged(): void {
    foundListeners.forEach((listener) => listener())
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
    generationTail = Promise.resolve()
    seenGeneration = undefined
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
