import { useApolloClient } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { ImagePlus, Trash2 } from 'lucide-react'
import { useId, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '../messages.tsx'
import { connectivity } from '../offline/connectivity.ts'
import { isConnectionFailure, OfflineError } from '../offline/errors.ts'
import { keptPhotos } from '../offline/keptPhotos.ts'
import { outbox } from '../offline/outbox.ts'
import { submitChange } from '../offline/submitChange.ts'
import { useConnectivity } from '../offline/useConnectivity.ts'
import { uuidV4 } from '../offline/uuid.ts'
import { resizeImage } from '../pictures/resizeImage.ts'
import { deleteImage, uploadImage } from '../pictures/upload.ts'

/**
 * Choose, replace or remove a picture. The file is made small in the browser first (and square for profile pictures); the
 * server still checks what it really is. Progress and results are announced to screen readers. A picture is sent at once. A vehicle's
 * (`keep`) is kept on this device instead while the server is out of reach, while the vehicle has changes waiting (they keep their
 * order: one added here is sent first) or when the upload loses the connection, and goes when the device syncs; for anything else the
 * buttons are off while the server is out of reach and say why, and an upload that loses the connection says so calmly.
 */
export function ImagePicker({
  preview,
  hasImage,
  path,
  maxEdge,
  square,
  disabled,
  keep,
  onChanged,
}: {
  preview: ReactNode
  hasImage: boolean
  path: string
  maxEdge: number
  square?: boolean
  disabled?: boolean
  /** A vehicle's picture: changes are kept on this device when they cannot be sent now (see above). */
  keep?: { vehicleId: string }
  onChanged: () => void | Promise<unknown>
}) {
  const { t } = useTranslation()
  const input = useRef<HTMLInputElement>(null)
  const hintId = useId()
  const [status, setStatus] = useState<string>()
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)
  const { reachable } = useConnectivity()
  const client = useApolloClient()
  const off = disabled || busy || (!reachable && !keep)

  /** The change waits on this device rather than going now: the server is out of reach, or the vehicle's earlier changes have not gone yet. */
  const waits = (vehicleId: string) => !connectivity.reachable || outbox.vehicleIds().has(vehicleId)

  /** Keeps the change of a vehicle's picture on this device (a picture: the file too); the screen shows it at once. */
  async function kept(vehicleId: string, change: { action: 'setPicture'; key: string } | { action: 'removePicture' }) {
    const input = change.action === 'setPicture' ? { key: change.key } : undefined
    await submitChange(client, { id: uuidV4(), entity: 'vehicles', action: change.action, vehicleId, targetId: vehicleId, ...(input ? { input } : {}) }, () =>
      change.action === 'setPicture' ? Promise.reject(new OfflineError()) : deleteImage(path),
    )
    return t(change.action === 'setPicture' ? 'image.keptOnDevice' : 'image.removedOnDevice')
  }

  async function choose(file: File) {
    const picture = await resizeImage(file, { maxEdge, square })
    if (keep && waits(keep.vehicleId)) return keepPicture(keep.vehicleId, picture)
    try {
      await uploadImage(path, picture)
      return t('image.uploaded')
    } catch (e) {
      if (keep && isConnectionFailure(e)) return keepPicture(keep.vehicleId, picture) // it may not have arrived: it goes with the next sync
      throw e
    }
  }

  async function keepPicture(vehicleId: string, picture: Blob) {
    const key = await keptPhotos.keep(picture, vehicleId)
    if (!key) throw new OfflineError() // no account's data is open on this device: nothing can be kept
    return kept(vehicleId, { action: 'setPicture', key })
  }

  async function remove() {
    if (keep && waits(keep.vehicleId)) return kept(keep.vehicleId, { action: 'removePicture' })
    await deleteImage(path)
    return t('image.removed')
  }

  async function run(work: () => Promise<string>) {
    setBusy(true)
    setError(undefined)
    setStatus(t('image.uploading'))
    try {
      const done = await work()
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
            if (file) void run(() => choose(file))
          }}
        />
        <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
          <Button size="large" variant="soft" disabled={off} aria-describedby={hintId} onClick={() => input.current?.click()}>
            <ImagePlus size={16} aria-hidden />
            {hasImage ? t('image.change') : t('image.choose')}
          </Button>
          {hasImage && (
            <Button size="large" variant="soft" color="error" disabled={off} aria-describedby={hintId} onClick={() => void run(remove)}>
              <Trash2 size={16} aria-hidden />
              {t('image.remove')}
            </Button>
          )}
        </Stack>
        <Typography id={hintId} variant="caption" sx={{ color: 'text.secondary' }}>
          {reachable ? t('image.hint') : keep ? t('image.keptHint') : t('image.needsServer')}
        </Typography>
        <div role="status" aria-label={t('a11y.uploadStatus')}>
          {status && <Typography variant="body2">{status}</Typography>}
        </div>
        {error !== undefined && <ErrorMessage error={error} />}
      </Stack>
    </Stack>
  )
}
