import { useQuery } from '@apollo/client/react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { RecognitionStatusDocument } from '../gql/generated.ts'
import { resizeImage } from '../pictures/resizeImage.ts'
import { deleteImage, LOG_PHOTO_EDGE, MAX_LOG_PHOTOS, photoDraftPath, photoDraftsPath, uploadImage, type ReadingPurpose } from '../pictures/upload.ts'

/** What the add/edit dialog of a log gets back from its caller after saving a new log: its id and how many photos it ended up with. */
/** What a save made: the log and how many photos it took; `queued` when it was kept on the device for the server (`offline/outbox.ts`). */
export type Saved = { id: string; photoCount: number; queued?: boolean } | void

export interface QueuedPhoto {
  key: string
  /** A preview of the resized picture (it stays on screen while it is uploaded and after). */
  url: string
  state: 'uploading' | 'uploaded' | 'failed'
  error?: unknown
  /** The draft's id once the upload went through. */
  id?: string
  /** When the upload went through (Date.now()). */
  uploadedAt?: number
}

interface Entry extends QueuedPhoto {
  blob: Blob
}

/** What the photo session needs to know about a log that was just saved (null or undefined: nothing was logged). */
export const savedFrom = (log: { id: string; photos: readonly unknown[] } | null | undefined): Saved => (log ? { id: log.id, photoCount: log.photos.length } : undefined)

export interface PhotoQueue {
  items: QueuedPhoto[]
  /** Makes the photos smaller and uploads them one by one; resolves to the first upload error, if any (the photo stays, marked failed). */
  add: (files: File[]) => Promise<unknown>
  /** Uploads a failed photo again; resolves to the error if it fails again. */
  retry: (key: string) => Promise<unknown>
  remove: (key: string) => Promise<void>
  /** The ids of the uploaded drafts, in the order they were picked: what the save attaches. */
  ids: string[]
  /** The uploaded drafts with when they arrived (photo reading waits a while for each). */
  uploaded: { id: string; at: number }[]
  /** True while photos are being made smaller or uploaded; saving should wait for it, or they would be left out. */
  busy: boolean
  /** How many photos could not be uploaded (they are not attached unless tried again). */
  failed: number
  /** The log was saved: its drafts are its photos now, so they are only forgotten here. */
  forget: () => void
  /** The dialog was closed without saving: the uploaded drafts are deleted (if that fails they expire on the server). */
  discard: () => void
}

/**
 * Photos picked for a log that does not exist yet (the add dialog). Each one is made small and uploaded right away as a draft of the
 * vehicle, so the server has it before the log is saved and nothing is lost when saving fails; the save then attaches the drafts.
 * With `reading`, the server also reads each photo (when photo reading is on).
 */
export function usePhotoQueue(vehicleId: string, reading?: { purpose: ReadingPurpose; locale: string }): PhotoQueue {
  const purpose = reading?.purpose
  const locale = reading?.locale
  // A photo that is read on the server goes as JPEG, which every model server decodes (not all of them WebP); where the server says
  // reading is off, the smaller WebP stays. The dialog asks the same question, so this is answered from the cache; until it is, JPEG.
  const status = useQuery(RecognitionStatusDocument, { skip: !reading })
  const jpeg = reading !== undefined && status.data?.recognitionStatus.available !== false
  const [items, setItems] = useState<Entry[]>([])
  const current = useRef<Entry[]>([])
  const [preparing, setPreparing] = useState(0)
  const urls = useRef(new Set<string>())
  // Bumped by forget()/discard(): work that finishes after the dialog was closed must not bring photos back into the next one.
  const generation = useRef(0)
  const nextKey = useRef(0)

  useEffect(() => {
    const created = urls.current
    return () => created.forEach((u) => URL.revokeObjectURL(u))
  }, [])

  const update = useCallback((change: (entries: Entry[]) => Entry[]) => {
    current.current = change(current.current)
    setItems(current.current)
  }, [])

  const patch = useCallback((key: string, values: Partial<Entry>) => update((entries) => entries.map((e) => (e.key === key ? { ...e, ...values } : e))), [update])

  const release = (entry: Entry) => {
    URL.revokeObjectURL(entry.url)
    urls.current.delete(entry.url)
  }

  const dropDraft = (id: string) => void deleteImage(photoDraftPath(id)).catch(() => undefined) // a draft left behind expires on its own

  const upload = useCallback(
    async (key: string, blob: Blob, started: number) => {
      patch(key, { state: 'uploading', error: undefined })
      try {
        const { id } = await uploadImage(photoDraftsPath(vehicleId, purpose && locale ? { purpose, locale } : undefined), blob)
        if (started !== generation.current || !current.current.some((e) => e.key === key)) dropDraft(id) // closed or removed meanwhile
        else patch(key, { state: 'uploaded', id, uploadedAt: Date.now() })
        return undefined
      } catch (error) {
        if (started === generation.current) patch(key, { state: 'failed', error })
        return error
      }
    },
    [patch, vehicleId, purpose, locale],
  )

  const add = useCallback(
    async (files: File[]) => {
      const started = generation.current
      let firstError: unknown
      setPreparing((n) => n + 1)
      try {
        for (const file of files) {
          const blob = await resizeImage(file, jpeg ? { maxEdge: LOG_PHOTO_EDGE, format: 'jpeg' } : { maxEdge: LOG_PHOTO_EDGE })
          if (started !== generation.current || current.current.length >= MAX_LOG_PHOTOS) break
          const url = URL.createObjectURL(blob)
          urls.current.add(url)
          // Not crypto.randomUUID(): it only exists in secure contexts, and the app is also served over plain HTTP.
          const key = `photo-${nextKey.current++}`
          update((entries) => [...entries, { key, url, blob, state: 'uploading' }])
          firstError ??= await upload(key, blob, started)
        }
      } finally {
        setPreparing((n) => n - 1)
      }
      return firstError
    },
    [update, upload, jpeg],
  )

  const retry = useCallback(
    async (key: string) => {
      const entry = current.current.find((e) => e.key === key)
      return entry ? upload(key, entry.blob, generation.current) : undefined
    },
    [upload],
  )

  const remove = useCallback(
    async (key: string) => {
      const entry = current.current.find((e) => e.key === key)
      if (!entry) return
      update((entries) => entries.filter((e) => e.key !== key))
      release(entry)
      if (entry.id) dropDraft(entry.id) // one still uploading is dropped when its upload finishes
    },
    [update],
  )

  const forget = useCallback(() => {
    generation.current++
    current.current.forEach(release)
    update(() => [])
  }, [update])

  const discard = useCallback(() => {
    current.current.forEach((e) => e.id && dropDraft(e.id))
    forget()
  }, [forget])

  return {
    items,
    add,
    retry,
    remove,
    ids: items.flatMap((e) => (e.state === 'uploaded' && e.id ? [e.id] : [])),
    uploaded: items.flatMap((e) => (e.state === 'uploaded' && e.id ? [{ id: e.id, at: e.uploadedAt ?? 0 }] : [])),
    busy: preparing > 0 || items.some((e) => e.state === 'uploading'),
    failed: items.filter((e) => e.state === 'failed').length,
    forget,
    discard,
  }
}
