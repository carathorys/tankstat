import { Box, Button, Flex, IconButton, Text } from '@radix-ui/themes'
import { Camera, ImagePlus, RotateCw, Trash2 } from 'lucide-react'
import { useEffect, useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useErrorText } from '../i18n/errors.ts'
import { ErrorMessage } from '../messages.tsx'
import { resizeImage } from '../pictures/resizeImage.ts'
import { deleteImage, LOG_PHOTO_EDGE, logPhotoPath, logPhotosPath, MAX_LOG_PHOTOS, uploadImage, type LogKind, type ReadingPurpose } from '../pictures/upload.ts'
import type { PhotoQueue } from './usePhotoQueue.ts'

/**
 * The photos of a refueling or expense: thumbnails that open the full picture, "Take photo" (the phone's camera right away) and
 * "Add photos" (the library). For a saved log (`logId`) every change goes to the server at once; for a new one each photo is uploaded
 * right away as a draft (the `queue`) and attached when the log is saved. Progress and results are announced to screen readers.
 * With `read` (a saved log in its edit dialog while photo reading may be on), added photos are read on the server like drafts are: they
 * go up as JPEG, which every model server decodes, and `onAdded` tells the dialog which ones to wait for.
 */
export function PhotoGallery({
  kind,
  logId,
  photos,
  queue,
  readingIds,
  disabled = false,
  read,
  onAdded,
  onBusyChange,
  onChanged,
}: {
  kind: LogKind
  /** Omit while the log is being created. */
  logId?: string
  /** The log is being saved: the photos must not change meanwhile. */
  disabled?: boolean
  photos: { id: string; url: string }[]
  queue: PhotoQueue
  /** Photos the server is reading right now (photo reading): they say so under their thumbnail. */
  readingIds?: readonly string[]
  /** A saved log's added photos are read too (`jpeg`: upload them as JPEG, as long as reading is not known to be off). */
  read?: { purpose: ReadingPurpose; locale: string; jpeg: boolean }
  /** A saved log's photos that were just added, by picture id. */
  onAdded?: (ids: string[]) => void
  /** Photos of a saved log are being uploaded or removed: saving should wait for it. */
  onBusyChange?: (busy: boolean) => void
  onChanged: () => void | Promise<unknown>
}) {
  const { t } = useTranslation()
  const errorText = useErrorText()
  const camera = useRef<HTMLInputElement>(null)
  const library = useRef<HTMLInputElement>(null)
  const hintId = useId()
  const [status, setStatus] = useState<string>()
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)

  useEffect(() => onBusyChange?.(busy), [busy, onBusyChange])

  const saved = logId !== undefined
  const shown = saved
    ? photos.map((p) => ({ key: p.id, url: p.url, state: 'uploaded' as const, reading: (readingIds ?? []).includes(p.id) }))
    : queue.items.map((p) => ({ key: p.key, url: p.url, state: p.state, reading: p.id !== undefined && (readingIds ?? []).includes(p.id) }))
  const room = MAX_LOG_PHOTOS - shown.length

  async function run(work: () => Promise<void>, done: string) {
    setBusy(true)
    setError(undefined)
    setStatus(t('photos.working'))
    try {
      await work()
      setStatus(done)
    } catch (e) {
      setStatus(undefined)
      setError(e)
    } finally {
      setBusy(false)
    }
  }

  function chosen(files: File[]) {
    const list = files.slice(0, Math.max(room, 0))
    if (list.length === 0) return
    void run(async () => {
      if (saved) {
        const added: string[] = []
        try {
          for (const file of list) {
            const image = await resizeImage(file, read?.jpeg ? { maxEdge: LOG_PHOTO_EDGE, format: 'jpeg' } : { maxEdge: LOG_PHOTO_EDGE })
            added.push((await uploadImage(logPhotosPath(kind, logId, read), image)).id)
          }
        } finally {
          if (added.length > 0) onAdded?.(added)
          await onChanged() // also shows the ones that did go through when a later one failed
        }
      } else {
        const failed = await queue.add(list)
        if (failed !== undefined) throw failed // the photo stays, marked, with a way to try again
      }
    }, saved ? t('photos.added') : t('photos.ready'))
  }

  const input = (ref: React.RefObject<HTMLInputElement | null>, capture: boolean) => (
    <input
      ref={ref}
      type="file"
      accept={capture ? 'image/*' : 'image/jpeg,image/png,image/webp'}
      capture={capture ? 'environment' : undefined}
      multiple={!capture}
      hidden
      tabIndex={-1}
      aria-hidden
      data-testid={capture ? 'photo-camera' : 'photo-library'}
      onChange={(e) => {
        const files = Array.from(e.target.files ?? [])
        e.target.value = '' // choosing the same file again must still fire
        chosen(files)
      }}
    />
  )

  return (
    <Flex asChild direction="column" gap="2">
      <fieldset style={{ border: 0, padding: 0, margin: 0 }}>
        <legend>
          <Text size="2" weight="bold">
            {t('photos.title')}
          </Text>
        </legend>
        {input(camera, true)}
        {input(library, false)}
        <Flex gap="2" wrap="wrap">
          <Button type="button" size="3" variant="soft" disabled={disabled || busy || room <= 0} aria-describedby={hintId} onClick={() => camera.current?.click()}>
            <Camera size={16} aria-hidden />
            {t('photos.take')}
          </Button>
          <Button type="button" size="3" variant="soft" disabled={disabled || busy || room <= 0} aria-describedby={hintId} onClick={() => library.current?.click()}>
            <ImagePlus size={16} aria-hidden />
            {t('photos.choose')}
          </Button>
        </Flex>
        <Text id={hintId} size="1" color="gray">
          {t('photos.hint', { n: shown.length, max: MAX_LOG_PHOTOS })}
        </Text>
        {!saved && queue.failed > 0 && <Text size="1">{t('photos.failedHint')}</Text>}
        {shown.length > 0 && (
          <Flex asChild gap="3" wrap="wrap">
            <ul style={{ listStyle: 'none', padding: 0, margin: 0 }}>
              {shown.map((photo, index) => (
                <li key={photo.key}>
                  <Flex direction="column" gap="1" align="center">
                    <a href={photo.url} target="_blank" rel="noreferrer" aria-label={t('photos.open', { n: index + 1 })}>
                      <Box width="96px" height="96px" overflow="hidden" style={{ borderRadius: 'var(--radius-2)' }}>
                        <img src={photo.url} alt="" loading="lazy" style={{ width: '100%', height: '100%', objectFit: 'cover', display: 'block' }} />
                      </Box>
                    </a>
                    {photo.state === 'uploading' && (
                      <Text size="1" color="gray">
                        {t('photos.uploading')}
                      </Text>
                    )}
                    {photo.state === 'failed' && <Text size="1">{t('photos.uploadFailed')}</Text>}
                    {photo.reading && (
                      <Text size="1" color="gray">
                        {t('reading.reading')}
                      </Text>
                    )}
                    <Flex gap="2">
                      {photo.state === 'failed' && (
                        <IconButton
                          type="button"
                          size="3"
                          variant="soft"
                          disabled={disabled || busy}
                          aria-label={t('photos.retryAria', { n: index + 1 })}
                          onClick={() =>
                            void run(async () => {
                              const failed = await queue.retry(photo.key)
                              if (failed !== undefined) throw failed
                            }, t('photos.ready'))
                          }
                        >
                          <RotateCw size={16} aria-hidden />
                        </IconButton>
                      )}
                      <IconButton
                        type="button"
                        size="3"
                        variant="soft"
                        color="red"
                        disabled={disabled || busy}
                        aria-label={t('photos.removeAria', { n: index + 1 })}
                        onClick={() =>
                          void run(async () => {
                            if (saved) {
                              await deleteImage(logPhotoPath(kind, logId, photo.key))
                              await onChanged()
                            } else {
                              await queue.remove(photo.key)
                            }
                          }, t('photos.removed'))
                        }
                      >
                        <Trash2 size={16} aria-hidden />
                      </IconButton>
                    </Flex>
                  </Flex>
                </li>
              ))}
            </ul>
          </Flex>
        )}
        <div role="status" aria-label={t('a11y.uploadStatus')}>
          {status && <Text size="2">{status}</Text>}
        </div>
        {error !== undefined && <ErrorMessage>{errorText(error)}</ErrorMessage>}
      </fieldset>
    </Flex>
  )
}
