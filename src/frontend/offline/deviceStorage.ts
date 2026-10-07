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
}

export interface DeviceStore {
  readonly user: string
  get(key: string): Promise<Snapshot | undefined>
  put(snapshot: Snapshot): Promise<void>
  /** Keeps the `keep` most recent snapshots and removes the others. */
  prune(keep: number): Promise<void>
  /** The vehicles and logs downloaded for the offline window (see `pull.ts`). */
  readonly rows: RowStore
  close(): void
}

export type LogKind = 'refuelings' | 'expenses'

/** A log as the offline feed sent it (every field the grids, the details and the trash show), and when this device took it. */
export interface LogRow {
  id: string
  vehicleId: string
  date: string
  deletedAt?: string | null
  /** When this device stored it (Date.now()): a full download removes what it did not bring again. */
  pulledAt: number
  [field: string]: unknown
}

/** A vehicle as the home list sent it: what the logs need of it (its name, units and the access to its logs). */
export interface VehicleRow {
  id: string
  name: string
  logAccess: string
  [field: string]: unknown
}

/** How far this device got with a vehicle's download (see `pull.ts`). */
export interface PullCursor {
  vehicleId: string
  /** The window's start the logs were downloaded from: null all of them, false none. */
  from: string | null | false
  /** The server's watermark of the last complete download: the next one asks for what changed since. */
  watermark: string | null
  /** A download that is not finished goes on from here. */
  next: string | null
  /** At least one full download finished: the device can answer for this vehicle's logs. */
  complete: boolean
  /** When the full download under way began: what it did not bring again is removed at its end. */
  fullStartedAt: number | null
}

export interface RowStore {
  logs(kind: LogKind, vehicleId: string): Promise<LogRow[]>
  allLogs(kind: LogKind): Promise<LogRow[]>
  log(kind: LogKind, id: string): Promise<LogRow | undefined>
  putLogs(kind: LogKind, rows: LogRow[]): Promise<void>
  deleteLogs(kind: LogKind, ids: string[]): Promise<void>
  vehicles(): Promise<VehicleRow[]>
  putVehicles(rows: VehicleRow[]): Promise<void>
  /** The vehicle, its logs and its cursor. */
  dropVehicle(id: string): Promise<void>
  cursor(vehicleId: string): Promise<PullCursor | undefined>
  cursors(): Promise<PullCursor[]>
  putCursor(cursor: PullCursor): Promise<void>
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

function openDb(factory: IDBFactory, name: string, version: number, upgrade: (db: IDBDatabase, from: number) => void): Promise<IDBDatabase> {
  const request = factory.open(name, version)
  request.onupgradeneeded = (event) => upgrade(request.result, event.oldVersion)
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error ?? new Error('IndexedDB could not be opened'))
    request.onblocked = () => reject(new Error('IndexedDB is blocked'))
  })
}

export function indexedDbStorage(factory: IDBFactory = indexedDB): DeviceStorage {
  const meta = () => openDb(factory, META_DB, 1, (db) => db.createObjectStore('meta'))
  return {
    async open(user) {
      const db = await openDb(factory, dbName(user), 2, (created, from) => {
        if (from < 1) created.createObjectStore('snapshots', { keyPath: 'key' }).createIndex('at', 'at')
        if (from < 2) {
          // The downloaded window (2): vehicles, their logs by vehicle, and how far each vehicle's download got.
          created.createObjectStore('vehicles', { keyPath: 'id' })
          created.createObjectStore('refuelings', { keyPath: 'id' }).createIndex('vehicleId', 'vehicleId')
          created.createObjectStore('expenses', { keyPath: 'id' }).createIndex('vehicleId', 'vehicleId')
          created.createObjectStore('cursors', { keyPath: 'vehicleId' })
        }
      })
      return {
        user,
        rows: rowStore({
          refuelings: idbTable<LogRow>(db, 'refuelings'),
          expenses: idbTable<LogRow>(db, 'expenses'),
          vehicles: idbTable<VehicleRow>(db, 'vehicles'),
          cursors: idbTable<PullCursor>(db, 'cursors'),
        }),
        get: (key) => done(db.transaction('snapshots').objectStore('snapshots').get(key)) as Promise<Snapshot | undefined>,
        async put(snapshot) {
          const tx = db.transaction('snapshots', 'readwrite')
          tx.objectStore('snapshots').put(snapshot)
          await committed(tx)
        },
        async prune(keep) {
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
              cursor.delete()
              surplus--
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
  const memoryTables = new Map<string, Tables>()
  let last: string | null = null
  return {
    async open(user) {
      const rows = stores.get(user) ?? new Map<string, Snapshot>()
      stores.set(user, rows)
      const tables = memoryTables.get(user) ?? {
        refuelings: memoryTable<LogRow>('id'),
        expenses: memoryTable<LogRow>('id'),
        vehicles: memoryTable<VehicleRow>('id'),
        cursors: memoryTable<PullCursor>('vehicleId'),
      }
      memoryTables.set(user, tables)
      return {
        user,
        rows: rowStore(tables),
        get: async (key) => rows.get(key),
        put: async (snapshot) => void rows.set(snapshot.key, snapshot),
        async prune(keep) {
          const oldest = [...rows.values()].sort((a, b) => a.at - b.at)
          oldest.slice(0, Math.max(0, oldest.length - keep)).forEach((s) => rows.delete(s.key))
        },
        close: () => undefined,
      }
    },
    lastUser: async () => last,
    setLastUser: async (user) => void (last = user),
  }
}

/** One object store, by its key and (for logs) by vehicle. */
interface Table<T> {
  all(): Promise<T[]>
  byVehicle(vehicleId: string): Promise<T[]>
  get(key: string): Promise<T | undefined>
  put(rows: T[]): Promise<void>
  delete(keys: string[]): Promise<void>
}

interface Tables {
  refuelings: Table<LogRow>
  expenses: Table<LogRow>
  vehicles: Table<VehicleRow>
  cursors: Table<PullCursor>
}

function idbTable<T>(db: IDBDatabase, name: string): Table<T> {
  const read = () => db.transaction(name).objectStore(name)
  async function write(apply: (store: IDBObjectStore) => void) {
    const tx = db.transaction(name, 'readwrite')
    apply(tx.objectStore(name))
    await committed(tx)
  }
  return {
    all: () => done(read().getAll()) as Promise<T[]>,
    byVehicle: (vehicleId) => done(read().index('vehicleId').getAll(vehicleId)) as Promise<T[]>,
    get: (k) => done(read().get(k)) as Promise<T | undefined>,
    put: (rows) => (rows.length === 0 ? Promise.resolve() : write((store) => rows.forEach((row) => store.put(row)))),
    delete: (keys) => (keys.length === 0 ? Promise.resolve() : write((store) => keys.forEach((k) => store.delete(k)))),
  }
}

function memoryTable<T>(key: string): Table<T> {
  const rows = new Map<string, T>()
  const keyOf = (row: T) => (row as Record<string, string>)[key]
  return {
    all: async () => [...rows.values()],
    byVehicle: async (vehicleId) => [...rows.values()].filter((row) => (row as { vehicleId?: string }).vehicleId === vehicleId),
    get: async (k) => rows.get(k),
    put: async (list) => list.forEach((row) => rows.set(keyOf(row), structuredClone(row))),
    delete: async (keys) => keys.forEach((k) => rows.delete(k)),
  }
}

/**
 * The downloaded window over its tables, with a copy of each vehicle's logs in memory once read, so paging and sorting a grid of a few
 * thousand rows never waits for IndexedDB; every write goes through and refreshes the copy.
 */
function rowStore(tables: Tables): RowStore {
  const cache = new Map<string, LogRow[]>()
  const cacheKey = (kind: LogKind, vehicleId: string) => `${kind}:${vehicleId}`
  async function logs(kind: LogKind, vehicleId: string) {
    const key = cacheKey(kind, vehicleId)
    const cached = cache.get(key)
    if (cached) return cached
    const loaded = await tables[kind].byVehicle(vehicleId)
    cache.set(key, loaded)
    return loaded
  }
  const forget = (kind: LogKind, vehicleIds: Iterable<string>) => {
    for (const id of vehicleIds) cache.delete(cacheKey(kind, id))
  }
  return {
    logs,
    allLogs: (kind) => tables[kind].all(),
    log: (kind, id) => tables[kind].get(id),
    async putLogs(kind, rows) {
      await tables[kind].put(rows)
      forget(kind, new Set(rows.map((r) => r.vehicleId)))
    },
    async deleteLogs(kind, ids) {
      const gone = (await Promise.all(ids.map((id) => tables[kind].get(id)))).flatMap((row) => (row ? [row.vehicleId] : []))
      await tables[kind].delete(ids)
      forget(kind, new Set(gone))
    },
    vehicles: () => tables.vehicles.all(),
    putVehicles: (rows) => tables.vehicles.put(rows),
    async dropVehicle(id) {
      for (const kind of ['refuelings', 'expenses'] as const) {
        await tables[kind].delete((await tables[kind].byVehicle(id)).map((row) => row.id))
        forget(kind, [id])
      }
      await tables.vehicles.delete([id])
      await tables.cursors.delete([id])
    },
    cursor: (vehicleId) => tables.cursors.get(vehicleId),
    cursors: () => tables.cursors.all(),
    putCursor: (cursor) => tables.cursors.put([cursor]),
  }
}
