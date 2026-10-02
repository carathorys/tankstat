import { useCallback, useEffect, useRef, useState } from 'react'
import { resizeImage } from '../pictures/resizeImage.ts'
import { LOG_PHOTO_EDGE, logPhotosPath, MAX_LOG_PHOTOS, uploadImage, type LogKind } from '../pictures/upload.ts'

/** What the add/edit dialog of a log gets back from its caller after saving: for a new log its id, so the photos chosen meanwhile can be sent right after. */
export type Saved = { id: string } | void

interface QueuedPhoto {
  key: string
  blob: Blob
  url: string
}

export interface PhotoQueue {
  items: QueuedPhoto[]
  add: (files: File[]) => Promise<void>
  remove: (key: string) => void
  clear: () => void
  /** Uploads the queued photos one by one to a log that was just saved; the ones that went through leave the queue. Resolves to the first error, if any. */
  uploadAll: (kind: LogKind, logId: string) => Promise<unknown>
}

/**
 * Photos chosen for a log that does not exist yet (the add dialog). They are made small right away, shown as previews, and sent once
 * the log has been saved, so taking the picture and saving the log is one step for the user.
 */
export function usePhotoQueue(): PhotoQueue {
  const [items, setItems] = useState<QueuedPhoto[]>([])
  const urls = useRef(new Set<string>())

  useEffect(() => {
    const created = urls.current
    return () => created.forEach((u) => URL.revokeObjectURL(u))
  }, [])

  const forget = (photo: QueuedPhoto) => {
    URL.revokeObjectURL(photo.url)
    urls.current.delete(photo.url)
  }

  const add = useCallback(async (files: File[]) => {
    const made: QueuedPhoto[] = []
    for (const file of files) {
      const blob = await resizeImage(file, { maxEdge: LOG_PHOTO_EDGE })
      const url = URL.createObjectURL(blob)
      urls.current.add(url)
      made.push({ key: crypto.randomUUID(), blob, url })
    }
    setItems((current) => [...current, ...made].slice(0, MAX_LOG_PHOTOS))
  }, [])

  const remove = useCallback(
    (key: string) => {
      items.filter((p) => p.key === key).forEach(forget)
      setItems(items.filter((p) => p.key !== key))
    },
    [items],
  )

  const clear = useCallback(() => {
    items.forEach(forget)
    setItems([])
  }, [items])

  const uploadAll = useCallback(
    async (kind: LogKind, logId: string) => {
      let remaining = items
      try {
        for (const photo of items) {
          await uploadImage(logPhotosPath(kind, logId), photo.blob)
          forget(photo)
          remaining = remaining.filter((p) => p !== photo)
        }
        return undefined
      } catch (error) {
        return error
      } finally {
        setItems(remaining)
      }
    },
    [items],
  )

  return { items, add, remove, clear, uploadAll }
}
