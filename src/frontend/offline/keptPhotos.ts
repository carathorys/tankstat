import { photoDraftsPath, uploadImage, type ReadingPurpose } from '../pictures/upload.ts'
import { KEPT } from './changes.ts'
import { deviceData } from './deviceData.ts'
import type { KeptPhoto } from './deviceStorage.ts'
import { uuidV4 } from './uuid.ts'

/** A draft uploaded for a kept photo is used again within this time: the server lets drafts expire after a day. */
export const DRAFT_REUSE_MS = 20 * 60 * 60 * 1000

const urls = new Map<string, string>()

/**
 * Photos picked while the server is out of reach (or while the vehicle has changes waiting, which keep their order): the resized picture is
 * kept in the open account's device data under a `local:` key, which stands in for a draft's id in the change it belongs to. When the
 * changes are sent (`push.ts`), each is uploaded as a draft of its vehicle right before its change, and goes once the server has the change.
 */
export const keptPhotos = {
  /** Keeps the picture; null when no account's data is open (nothing can be kept, the photo stays on screen only). */
  async keep(blob: Blob, vehicleId: string, reading?: { purpose: ReadingPurpose; locale: string }): Promise<string | null> {
    const rows = await deviceData.rows()
    if (!rows) return null
    const key = `${KEPT}${uuidV4()}`
    await rows.putPhoto({ key, vehicleId, type: blob.type, bytes: await blob.arrayBuffer(), createdAt: Date.now(), ...(reading ? { reading } : {}) })
    return key
  },

  async get(key: string): Promise<KeptPhoto | undefined> {
    return (await deviceData.rows())?.photo(key)
  },

  /** An address the screen can show the kept picture at (the same one every time; it goes when the photo goes). */
  async url(key: string): Promise<string | undefined> {
    const known = urls.get(key)
    if (known) return known
    const photo = await keptPhotos.get(key)
    if (!photo) return undefined
    const url = URL.createObjectURL(new Blob([photo.bytes], { type: photo.type }))
    urls.set(key, url)
    return url
  },

  async remove(keys: readonly string[]): Promise<void> {
    if (keys.length === 0) return
    for (const key of keys) {
      const url = urls.get(key)
      if (url) URL.revokeObjectURL(url)
      urls.delete(key)
    }
    await (await deviceData.rows())?.deletePhotos([...keys])
  },

  /**
   * The draft the kept photo is on the server as, uploaded now unless one was uploaded for it lately (a try that failed after uploading
   * must not leave drafts piling up). Undefined when the photo is gone from the device. Upload errors are the caller's.
   */
  async asDraft(key: string, now = Date.now()): Promise<string | undefined> {
    const rows = await deviceData.rows()
    const photo = await rows?.photo(key)
    if (!rows || !photo) return undefined
    if (photo.draftId && photo.draftAt && now - photo.draftAt < DRAFT_REUSE_MS) return photo.draftId
    const reading = photo.reading as { purpose: ReadingPurpose; locale: string } | undefined
    const { id } = await uploadImage(photoDraftsPath(photo.vehicleId, reading), new Blob([photo.bytes], { type: photo.type }))
    await rows.putPhoto({ ...photo, draftId: id, draftAt: now })
    return id
  },

  /** Removes the kept photos no change refers to any more (at start: nothing else can hold one then). */
  async gc(live: ReadonlySet<string>): Promise<void> {
    const rows = await deviceData.rows()
    if (!rows) return
    await keptPhotos.remove((await rows.photos()).map((p) => p.key).filter((key) => !live.has(key)))
  },
}
