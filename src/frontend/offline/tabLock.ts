/**
 * Runs `work` unless another tab of this site is running work under the same name right now: with several tabs open, the syncs they start
 * by themselves (at start, when the server is back, hourly) then happen once, not once per tab. Without the Web Locks API (older
 * browsers, tests) the work simply runs; a sync started twice is harmless, only wasteful (the server answers a change sent again with
 * what it did the first time). Resolves to whether the work ran here: false when another tab is at it (the caller may try again later).
 * The lock is let go after `holdMs` even if the work is still going (a request that never answers must not stop every other tab's sync).
 */
export async function inOneTab(
  name: string,
  work: () => Promise<unknown>,
  locks: LockManager | undefined = globalThis.navigator?.locks,
  holdMs = LOCK_HOLD_MS,
): Promise<boolean> {
  if (!locks) {
    await work()
    return true
  }
  return locks.request(name, { ifAvailable: true }, async (lock) => {
    if (!lock) return false
    let timer: ReturnType<typeof setTimeout> | undefined
    const held = new Promise<void>((resolve) => (timer = setTimeout(resolve, holdMs)))
    await Promise.race([work().catch(() => undefined), held]).finally(() => clearTimeout(timer))
    return true
  }) as Promise<boolean>
}

/** The longest one tab keeps the others from syncing by themselves. */
export const LOCK_HOLD_MS = 5 * 60 * 1000

let persisting: Promise<unknown> | null = null

/**
 * Asks the browser once to keep this site's storage when space runs low (it may decide on its own, or ask the person): the changes
 * waiting for the server and the photos kept for them live there. Nothing depends on the answer.
 */
export function keepStorage(storage: StorageManager | undefined = globalThis.navigator?.storage): void {
  persisting ??= storage?.persist ? storage.persist().catch(() => false) : Promise.resolve(false)
}
