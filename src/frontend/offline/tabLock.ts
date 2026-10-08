/**
 * Runs `work` unless another tab of this site is running work under the same name right now: with several tabs open, the syncs they start
 * by themselves (at start, when the server is back, hourly) then happen once, not once per tab. Without the Web Locks API (older
 * browsers, tests) the work simply runs; a sync started twice is harmless, only wasteful (the server answers a change sent again with
 * what it did the first time).
 */
export async function inOneTab(name: string, work: () => Promise<unknown>, locks: LockManager | undefined = globalThis.navigator?.locks): Promise<void> {
  if (!locks) {
    await work()
    return
  }
  await locks.request(name, { ifAvailable: true }, async (lock) => {
    if (lock) await work()
  })
}

let persisting: Promise<unknown> | null = null

/**
 * Asks the browser once to keep this site's storage when space runs low (it may decide on its own, or ask the person): the changes
 * waiting for the server and the photos kept for them live there. Nothing depends on the answer.
 */
export function keepStorage(storage: StorageManager | undefined = globalThis.navigator?.storage): void {
  persisting ??= storage?.persist ? storage.persist().catch(() => false) : Promise.resolve(false)
}
