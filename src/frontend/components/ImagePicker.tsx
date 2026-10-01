import { Button, Flex, Text } from '@radix-ui/themes'
import { ImagePlus, Trash2 } from 'lucide-react'
import { useId, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useErrorText } from '../i18n/errors.ts'
import { deleteImage, uploadImage } from '../pictures/upload.ts'
import { resizeImage } from '../pictures/resizeImage.ts'
import { ErrorMessage } from '../messages.tsx'

/**
 * Choose, replace or remove a picture. The file is made small in the browser first (and square for profile pictures); the
 * server still checks what it really is. Progress and results are announced to screen readers.
 */
export function ImagePicker({
  preview,
  hasImage,
  path,
  maxEdge,
  square,
  disabled,
  onChanged,
}: {
  preview: ReactNode
  hasImage: boolean
  path: string
  maxEdge: number
  square?: boolean
  disabled?: boolean
  onChanged: () => void | Promise<unknown>
}) {
  const { t } = useTranslation()
  const errorText = useErrorText()
  const input = useRef<HTMLInputElement>(null)
  const hintId = useId()
  const [status, setStatus] = useState<string>()
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)

  async function run(work: () => Promise<void>, done: string) {
    setBusy(true)
    setError(undefined)
    setStatus(t('image.uploading'))
    try {
      await work()
      await onChanged()
      setStatus(done)
    } catch (e) {
      setStatus(undefined)
      setError(e)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Flex direction={{ initial: 'column', sm: 'row' }} gap="4" align={{ initial: 'start', sm: 'center' }}>
      {preview}
      <Flex direction="column" gap="2" align="start">
        <input
          ref={input}
          type="file"
          accept="image/jpeg,image/png,image/webp"
          hidden
          tabIndex={-1}
          aria-hidden
          onChange={(e) => {
            const file = e.target.files?.[0]
            e.target.value = '' // choosing the same file again must still fire
            if (file) void run(async () => void (await uploadImage(path, await resizeImage(file, { maxEdge, square }))), t('image.uploaded'))
          }}
        />
        <Flex gap="2" wrap="wrap">
          <Button size="3" variant="soft" disabled={disabled || busy} aria-describedby={hintId} onClick={() => input.current?.click()}>
            <ImagePlus size={16} aria-hidden />
            {hasImage ? t('image.change') : t('image.choose')}
          </Button>
          {hasImage && (
            <Button size="3" variant="soft" color="red" disabled={disabled || busy} onClick={() => void run(() => deleteImage(path), t('image.removed'))}>
              <Trash2 size={16} aria-hidden />
              {t('image.remove')}
            </Button>
          )}
        </Flex>
        <Text id={hintId} size="1" color="gray">
          {t('image.hint')}
        </Text>
        <div role="status" aria-label={t('a11y.uploadStatus')}>
          {status && <Text size="2">{status}</Text>}
        </div>
        {error !== undefined && <ErrorMessage>{errorText(error)}</ErrorMessage>}
      </Flex>
    </Flex>
  )
}
