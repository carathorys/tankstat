/**
 * What this device keeps of the server's answers, so the app can show them while the server is out of reach. One database per user
 * (`tankstat-offline-<user>`, `anonymous` when sign-in is off), so another account signing in on the same device never opens it, and a
 * small one (`tankstat-offline-meta`) that remembers whose data to open at the next start. No library: IndexedDB through a few promises.
 */

/** The last answer of one query (an operation with its variables), and when it came. */
export interface Snapshot {
  key: string
  data: unknown
  at: number
  /** Which version of the query's document it answers (`documentId`): an answer kept by an older build may lack fields. */
  doc?: string
}

export interface DeviceStore {
  readonly user: string
  get(key: string): Promise<Snapshot | undefined>
  put(snapshot: Snapshot): Promise<void>
  /** Keeps the `keep` most recent snapshots and removes the others, never those `spare` names (they count towards `keep`). */
  prune(keep: number, spare?: (key: string) => boolean): Promise<void>
  close(): void
}

export interface DeviceStorage {
  open(user: string): Promise<DeviceStore>
  lastUser(): Promise<string | null>
  setLastUser(user: string | null): Promise<void>
}

const META_DB = 'tankstat-offline-meta'
const dbName = (user: string) => `tankstat-offline-${user}`

const done = <T>(request: IDBRequest<T>): Promise<T> =>
  new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error ?? new Error('IndexedDB request failed'))
  })

const committed = (tx: IDBTransaction): Promise<void> =>
  new Promise((resolve, reject) => {
    tx.oncomplete = () => resolve()
    tx.onerror = () => reject(tx.error ?? new Error('IndexedDB transaction failed'))
    tx.onabort = () => reject(tx.error ?? new Error('IndexedDB transaction aborted'))
  })

/** How long opening waits for another tab to let go of an older version of the database before giving up (it then keeps nothing). */
export const BLOCKED_WAIT_MS = 3_000

/**
 * Opens a database. Every connection closes itself when another tab needs a newer version (a new build that upgrades it), so an old tab
 * left open never blocks a new one; that old tab then keeps nothing more. A tab of a build from before this rule cannot be asked: the
 * open waits `BLOCKED_WAIT_MS` for it, then fails (the app works on, keeping nothing).
 */
function openDb(factory: IDBFactory, name: string, upgrade: (db: IDBDatabase) => void): Promise<IDBDatabase> {
  const request = factory.open(name, 1)
  request.onupgradeneeded = () => upgrade(request.result)
  return new Promise((resolve, reject) => {
    let blocked: ReturnType<typeof setTimeout> | undefined
    let gaveUp = false
    request.onsuccess = () => {
      clearTimeout(blocked)
      const db = request.result
      if (gaveUp) return db.close()
      db.onversionchange = () => db.close()
      resolve(db)
    }
    request.onerror = () => {
      clearTimeout(blocked)
      reject(request.error ?? new Error('IndexedDB could not be opened'))
    }
    request.onblocked = () => {
      blocked ??= setTimeout(() => {
        gaveUp = true
        reject(new Error('IndexedDB is blocked by another tab'))
      }, BLOCKED_WAIT_MS)
    }
  })
}

export function indexedDbStorage(factory: IDBFactory = indexedDB): DeviceStorage {
  const meta = () => openDb(factory, META_DB, (db) => db.createObjectStore('meta'))
  return {
    async open(user) {
      const db = await openDb(factory, dbName(user), (created) => created.createObjectStore('snapshots', { keyPath: 'key' }).createIndex('at', 'at'))
      return {
        user,
        // Async, so a connection closed for a newer version fails as a promise, like any other failure.
        get: async (key) => (await done(db.transaction('snapshots').objectStore('snapshots').get(key))) as Snapshot | undefined,
        async put(snapshot) {
          const tx = db.transaction('snapshots', 'readwrite')
          tx.objectStore('snapshots').put(snapshot)
          await committed(tx)
        },
        async prune(keep, spare = () => false) {
          const tx = db.transaction('snapshots', 'readwrite')
          const store = tx.objectStore('snapshots')
          // Callbacks only: a transaction may commit while a promise continuation waits.
          const counted = store.count()
          counted.onsuccess = () => {
            let surplus = counted.result - keep
            if (surplus <= 0) return
            // Oldest first: the index on `at` walks them in that order.
            const cursors = store.index('at').openCursor()
            cursors.onsuccess = () => {
              const cursor = cursors.result
              if (!cursor || surplus <= 0) return
              if (!spare(String(cursor.primaryKey))) {
                cursor.delete()
                surplus--
              }
              cursor.continue()
            }
          }
          await committed(tx)
        },
        close: () => db.close(),
      }
    },
    async lastUser() {
      const db = await meta()
      try {
        return ((await done(db.transaction('meta').objectStore('meta').get('lastUser'))) as string | undefined) ?? null
      } finally {
        db.close()
      }
    },
    async setLastUser(user) {
      const db = await meta()
      try {
        const tx = db.transaction('meta', 'readwrite')
        if (user === null) tx.objectStore('meta').delete('lastUser')
        else tx.objectStore('meta').put(user, 'lastUser')
        await committed(tx)
      } finally {
        db.close()
      }
    },
  }
}

/** The same, in memory: for a browser that refuses IndexedDB (private windows of some browsers) and for tests. Nothing survives a reload. */
export function memoryStorage(): DeviceStorage {
  const stores = new Map<string, Map<string, Snapshot>>()
  let last: string | null = null
  return {
    async open(user) {
      const rows = stores.get(user) ?? new Map<string, Snapshot>()
      stores.set(user, rows)
      return {
        user,
        get: async (key) => rows.get(key),
        put: async (snapshot) => void rows.set(snapshot.key, snapshot),
        async prune(keep, spare = () => false) {
          const surplus = rows.size - keep
          const oldest = [...rows.values()].filter((s) => !spare(s.key)).sort((a, b) => a.at - b.at)
          oldest.slice(0, Math.max(0, surplus)).forEach((s) => rows.delete(s.key))
        },
        close: () => undefined,
      }
    },
    lastUser: async () => last,
    setLastUser: async (user) => void (last = user),
  }
}
