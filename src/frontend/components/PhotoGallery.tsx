import { Box, Button, Flex, IconButton, Text } from '@radix-ui/themes'
import { Camera, ImagePlus, Trash2 } from 'lucide-react'
import { useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useErrorText } from '../i18n/errors.ts'
import { ErrorMessage } from '../messages.tsx'
import { resizeImage } from '../pictures/resizeImage.ts'
import { deleteImage, LOG_PHOTO_EDGE, logPhotoPath, logPhotosPath, MAX_LOG_PHOTOS, uploadImage, type LogKind } from '../pictures/upload.ts'
import type { PhotoQueue } from './usePhotoQueue.ts'

/**
 * The photos of a refueling or expense: thumbnails that open the full picture, "Take photo" (the phone's camera right away) and
 * "Add photos" (the library). For a saved log (`logId`) every change goes to the server at once; for a new one the photos wait in the
 * `queue` until the log is saved. Progress and results are announced to screen readers.
 */
export function PhotoGallery({
  kind,
  logId,
  photos,
  queue,
  onChanged,
}: {
  kind: LogKind
  /** Omit while the log is being created. */
  logId?: string
  photos: { id: string; url: string }[]
  queue: PhotoQueue
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

  const saved = logId !== undefined
  const shown = saved ? photos.map((p) => ({ key: p.id, url: p.url })) : queue.items.map((p) => ({ key: p.key, url: p.url }))
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
        try {
          for (const file of list) await uploadImage(logPhotosPath(kind, logId), await resizeImage(file, { maxEdge: LOG_PHOTO_EDGE }))
        } finally {
          await onChanged() // also shows the ones that did go through when a later one failed
        }
      } else {
        await queue.add(list)
      }
    }, saved ? t('photos.added') : t('photos.queued'))
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
          <Button type="button" size="3" variant="soft" disabled={busy || room <= 0} aria-describedby={hintId} onClick={() => camera.current?.click()}>
            <Camera size={16} aria-hidden />
            {t('photos.take')}
          </Button>
          <Button type="button" size="3" variant="soft" disabled={busy || room <= 0} aria-describedby={hintId} onClick={() => library.current?.click()}>
            <ImagePlus size={16} aria-hidden />
            {t('photos.choose')}
          </Button>
        </Flex>
        <Text id={hintId} size="1" color="gray">
          {t('photos.hint', { n: shown.length, max: MAX_LOG_PHOTOS })}
        </Text>
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
                    <IconButton
                      type="button"
                      size="3"
                      variant="soft"
                      color="red"
                      disabled={busy}
                      aria-label={t('photos.removeAria', { n: index + 1 })}
                      onClick={() =>
                        void run(async () => {
                          if (saved) {
                            await deleteImage(logPhotoPath(kind, logId, photo.key))
                            await onChanged()
                          } else {
                            queue.remove(photo.key)
                          }
                        }, t('photos.removed'))
                      }
                    >
                      <Trash2 size={16} aria-hidden />
                    </IconButton>
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
