import { useEffect, useSyncExternalStore } from 'react'
import { deviceData } from './deviceData.ts'

/** The address the server gives a picture at (`MediaUrls.Image`): the image id, 32 hex digits. */
const MEDIA = /^\/media\/([0-9a-f]{32})$/

/** The image id of a picture's address, or null for anything else (a `blob:` preview, another site). */
export const pictureIdOf = (url: string | null | undefined): string | null => url?.match(MEDIA)?.[1] ?? null

/** Which pictures the open account's data holds (null: not read yet). */
let known: Set<string> | null = null
let knownLoad: Promise<void> | null = null
/** Object URLs of the kept pictures shown so far, by image id: the same one every time, revoked when the picture or the account goes. */
const urls = new Map<string, string>()
const pending = new Set<string>()
/** Listed as kept, but gone when read (another tab's download removed it): the server's address is shown instead. */
const missing = new Set<string>()
/** Bumped whenever another account's data (or none) is open: what is still on its way belongs to the one before. */
let generation = 0
let version = 0
const listeners = new Set<() => void>()

let notifying: ReturnType<typeof setTimeout> | null = null

/** Tells the screens once for everything that changed in this task and the next (each picture read arrives in a task of its own). */
function changed() {
  version++
  notifying ??= setTimeout(() => {
    notifying = null
    listeners.forEach((listener) => listener())
  }, 0)
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

const currentVersion = () => version

function revoke(ids: Iterable<string>) {
  for (const id of ids) {
    const url = urls.get(id)
    if (url) URL.revokeObjectURL(url)
    urls.delete(id)
  }
}

/** Counts the reads of which pictures are kept: only the latest one's answer counts (a download may have kept more meanwhile). */
let reads = 0

function loadKnown() {
  const gen = generation
  knownLoad ??= (async () => {
    const read = ++reads
    const rows = await deviceData.rows()
    const ids = new Set(rows ? await rows.pictureIds().catch(() => []) : [])
    if (gen !== generation || read !== reads) return
    known = ids
    revoke([...urls.keys()].filter((id) => !ids.has(id))) // removed by a download since they were shown
    changed()
  })()
}

async function loadPicture(id: string) {
  const gen = generation
  pending.add(id)
  try {
    const picture = await (await deviceData.rows())?.picture(id)
    if (gen !== generation) return
    if (picture) urls.set(id, URL.createObjectURL(new Blob([picture.bytes], { type: picture.type })))
    else missing.add(id)
  } catch {
    if (gen === generation) missing.add(id)
  } finally {
    if (gen === generation) pending.delete(id)
    changed()
  }
}

/**
 * Where to show a picture from: its kept copy when the open account's data holds one (online too: no request, and no "sign in first"
 * once the access cookie ran out), else the server's address as it is (offline too: the browser's own cache may still have it).
 * Undefined while the device is being asked whether it holds a copy (a moment), so a kept picture is not also loaded from the server.
 */
function resolve(url: string | null | undefined): string | null | undefined {
  if (!url) return null
  const id = pictureIdOf(url)
  if (!id) return url
  const kept = urls.get(id)
  if (kept) return kept
  if (known === null) return undefined
  return known.has(id) && !missing.has(id) ? undefined : url
}

function ensure(url: string | null | undefined) {
  const id = pictureIdOf(url)
  if (!id) return
  if (known === null) loadKnown()
  else if (!urls.has(id) && known.has(id) && !missing.has(id) && !pending.has(id)) void loadPicture(id)
}

// Another account's data, or none (signed out): its pictures are no longer shown, and nothing of the one before is kept in memory.
deviceData.onStoreChange(() => {
  generation++
  revoke([...urls.keys()])
  known = null
  knownLoad = null
  pending.clear()
  missing.clear()
  changed()
})

/**
 * The pictures this device keeps for offline use, in the open account's data (`deviceStorage.ts`, store `pictures`): the downloaded
 * vehicles' pictures, their owners' avatars and the user's own, kept by the download (`pull.ts`). Per account like the rest of the
 * device data, so another account on the device never sees them, and *Remove offline data* removes them.
 */
export const keptPictures = {
  /** The download kept or removed some: which are kept is read again. */
  refresh(): void {
    known = null
    knownLoad = null
    missing.clear()
    loadKnown() // revokes what is no longer kept
    changed()
  },
}

/**
 * The address to show a picture at (see `resolve`): its kept copy, else the server's address; null while the device is asked whether it
 * holds a copy, and when there is no picture.
 */
export function usePictureSrc(url: string | null | undefined): { src: string | null; waiting: boolean } {
  const seen = useSyncExternalStore(subscribe, currentVersion)
  const resolved = resolve(url)
  useEffect(() => ensure(url), [url, seen]) // again after every change: the ids read, a picture read, another account's data
  return { src: resolved ?? null, waiting: resolved === undefined }
}
