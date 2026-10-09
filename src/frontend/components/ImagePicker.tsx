import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { ImagePlus, Trash2 } from 'lucide-react'
import { useId, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '../messages.tsx'
import { isConnectionFailure, OfflineError } from '../offline/errors.ts'
import { useConnectivity } from '../offline/useConnectivity.ts'
import { resizeImage } from '../pictures/resizeImage.ts'
import { deleteImage, uploadImage } from '../pictures/upload.ts'

/**
 * Choose, replace or remove a picture. The file is made small in the browser first (and square for profile pictures); the
 * server still checks what it really is. Progress and results are announced to screen readers. A picture is sent at once, so while the
 * server is out of reach the buttons are off and say why (nothing is kept to send later), and an upload that loses the connection says so
 * calmly.
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
  const input = useRef<HTMLInputElement>(null)
  const hintId = useId()
  const [status, setStatus] = useState<string>()
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)
  const { reachable } = useConnectivity()
  const off = disabled || busy || !reachable

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
      setError(isConnectionFailure(e) ? new OfflineError() : e) // the request got no answer: the same calm note as when it was known
    } finally {
      setBusy(false)
    }
  }

  return (
    <Stack direction={{ xs: 'column', md: 'row' }} sx={{ gap: 2, alignItems: { xs: 'flex-start', md: 'center' } }}>
      {preview}
      <Stack sx={{ gap: 1, alignItems: 'flex-start' }}>
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
        <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
          <Button size="large" variant="soft" disabled={off} aria-describedby={hintId} onClick={() => input.current?.click()}>
            <ImagePlus size={16} aria-hidden />
            {hasImage ? t('image.change') : t('image.choose')}
          </Button>
          {hasImage && (
            <Button size="large" variant="soft" color="error" disabled={off} aria-describedby={hintId} onClick={() => void run(() => deleteImage(path), t('image.removed'))}>
              <Trash2 size={16} aria-hidden />
              {t('image.remove')}
            </Button>
          )}
        </Stack>
        <Typography id={hintId} variant="caption" sx={{ color: 'text.secondary' }}>
          {reachable ? t('image.hint') : t('image.needsServer')}
        </Typography>
        <div role="status" aria-label={t('a11y.uploadStatus')}>
          {status && <Typography variant="body2">{status}</Typography>}
        </div>
        {error !== undefined && <ErrorMessage error={error} />}
      </Stack>
    </Stack>
  )
}
