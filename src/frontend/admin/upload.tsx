import Button from '@mui/material/Button'
import Typography from '@mui/material/Typography'
import { Upload } from 'lucide-react'
import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useFormat } from '../i18n/format.ts'
import { ErrorMessage } from '../messages.tsx'
import { ApiError } from '../pictures/ApiError.ts'
import { uploadBackup } from './backupFiles.ts'

/**
 * A backup file from this device, sent into the server's backups folder: one the folder does not hold yet (a backup of another server, one
 * kept elsewhere), then listed with the folder's. On the Backups page, and on the setup page with the one-time `code` while the setup
 * waits, since no administrator exists yet.
 */
export function UploadBackup({ maxUploadMb, code, onUploaded }: { maxUploadMb: number; code?: string; onUploaded: () => void }) {
  const { t } = useTranslation()
  const { bytes } = useFormat()
  const input = useRef<HTMLInputElement>(null)
  const [busy, setBusy] = useState(false)
  const [status, setStatus] = useState<string>()
  const [error, setError] = useState<unknown>()

  const send = async (file: File) => {
    // Larger than the server takes (its rule exactly): said at once, before anything is sent. The server would refuse it as soon as it
    // starts to arrive, which a browser still sending may only report as a lost connection.
    if (file.size > maxUploadMb * 1024 * 1024) {
      setStatus(undefined)
      setError(new ApiError('backup.tooLarge', { maxMb: maxUploadMb }, 'The backup is larger than an upload may be.', 413))
      return
    }
    setBusy(true)
    setError(undefined)
    setStatus(t('admin.backups.uploading', { file: file.name, size: bytes(file.size) }))
    try {
      const stored = await uploadBackup(file, code)
      setStatus(t('admin.backups.uploaded', { file: stored.file }))
      onUploaded()
    } catch (e) {
      setStatus(undefined)
      setError(e)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section aria-labelledby="backups-upload">
      <Typography id="backups-upload" component="h2" variant="h5" sx={{ mb: 1 }}>
        {t('admin.backups.uploadTitle')}
      </Typography>
      <Typography id="backups-upload-hint" variant="body2" sx={{ color: 'text.secondary', mb: 1.5 }}>
        {t('admin.backups.uploadHint', { max: maxUploadMb })}
      </Typography>
      <input
        ref={input}
        type="file"
        accept=".zip,application/zip"
        hidden
        tabIndex={-1}
        aria-hidden
        data-testid="backup-file"
        onChange={(e) => {
          const file = e.target.files?.[0]
          e.target.value = '' // choosing the same file again must still fire
          if (file) void send(file)
        }}
      />
      <Button variant="soft" loading={busy} aria-describedby="backups-upload-hint" onClick={() => input.current?.click()}>
        <Upload size={16} aria-hidden />
        {t('admin.backups.upload')}
      </Button>
      <div role="status">{status && <Typography variant="body2" sx={{ mt: 1 }}>{status}</Typography>}</div>
      {error !== undefined && <ErrorMessage error={error} />}
    </section>
  )
}
