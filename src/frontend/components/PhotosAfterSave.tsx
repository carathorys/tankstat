import { Button, Dialog, Flex, Text } from '@radix-ui/themes'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useErrorText } from '../i18n/errors.ts'
import { ErrorMessage } from '../messages.tsx'
import type { LogKind } from '../pictures/upload.ts'
import { PhotoGallery } from './PhotoGallery.tsx'
import type { PhotoQueue } from './usePhotoQueue.ts'

/**
 * Shown in place of the form when a new log was saved but some of its photos could not be sent. The log exists, so it must not be saved
 * again; the photos that went through are listed, and the ones that did not stay in the queue and can be sent again.
 */
export function PhotosAfterSave({
  kind,
  logId,
  failure,
  photos,
  queue,
  onChanged,
}: {
  kind: LogKind
  logId: string
  failure: unknown
  photos: { id: string; url: string }[]
  queue: PhotoQueue
  onChanged: () => void | Promise<unknown>
}) {
  const { t } = useTranslation()
  const errorText = useErrorText()
  const [error, setError] = useState<unknown>(failure)
  const [busy, setBusy] = useState(false)

  async function retry() {
    setBusy(true)
    setError(await queue.uploadAll(kind, logId))
    await onChanged()
    setBusy(false)
  }

  return (
    <Flex direction="column" gap="3">
      {error !== undefined && <ErrorMessage>{`${t('photos.partialFailure')} ${errorText(error)}`}</ErrorMessage>}
      {queue.items.length > 0 && (
        <Flex align="center" gap="3" wrap="wrap">
          <Text size="2">{t('photos.unsent', { n: queue.items.length })}</Text>
          <Button type="button" variant="soft" disabled={busy} onClick={() => void retry()}>
            {t('photos.retry')}
          </Button>
        </Flex>
      )}
      <PhotoGallery kind={kind} logId={logId} photos={photos} queue={queue} onChanged={onChanged} />
      <Flex justify="end">
        <Dialog.Close>
          <Button type="button">{t('photos.done')}</Button>
        </Dialog.Close>
      </Flex>
    </Flex>
  )
}
