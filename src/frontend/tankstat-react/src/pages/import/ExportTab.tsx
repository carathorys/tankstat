import { useMutation, useQuery } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Card from '@mui/material/Card'
import LinearProgress from '@mui/material/LinearProgress'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { Download } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Loading } from '../../components/Loading.tsx'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { CancelExportDocument, DeleteExportDocument, ExportCarsDocument, MyExportDocument, type ExportFileFieldsFragment } from '../../gql/generated.ts'
import { useKeyText } from '../../i18n/errors.ts'
import { useFormat } from '../../i18n/format.ts'
import { ErrorMessage, SuccessMessage, WarningMessage } from '../../messages.tsx'
import { ExportForm } from './ExportForm.tsx'
import { downloadExport } from './exportFiles.ts'

/** How often the tab asks about an export being built. */
export const EXPORT_POLL_MS = 2000

/**
 * The Export tab of the Import and export page (#341): the person's file (being built, ready, or how the last one ended) above the form
 * that makes a new one; a ready file's form waits behind *Export again*.
 */
export function ExportTab() {
  const { t } = useTranslation()
  const mine = useQuery(MyExportDocument, { fetchPolicy: 'network-only' })
  const cars = useQuery(ExportCarsDocument, { fetchPolicy: 'network-only' })
  const file = mine.data?.myExport ?? null
  // Asked again while it is built, never otherwise.
  const building = file?.state === 'BUILDING'
  const [polling, setPolling] = useState(false)
  if (building !== polling) {
    setPolling(building)
    if (building) mine.startPolling(EXPORT_POLL_MS)
    else mine.stopPolling()
  }
  const [again, setAgain] = useState(false)

  if (mine.error || cars.error) return <ErrorMessage error={mine.error ?? cars.error} />
  if (!mine.data || !cars.data) return <Loading />
  const offers = cars.data.exportCars
  const kept = file?.kept ?? (file?.state === 'READY' ? file : null)
  const showForm = !building && (kept === null || again)

  return (
    <Stack sx={{ gap: 2 }}>
      <Typography variant="body2" sx={{ color: 'text.secondary' }}>
        {t('export.intro')}
      </Typography>
      {file && <FileState file={file} />}
      {kept && kept !== file && <FileState file={kept} />}
      {kept && !again && (
        <Card sx={{ p: 1.5 }}>
          <Typography component="h2" variant="h6">
            {t('export.again')}
          </Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary', mb: 1 }}>
            {t('export.againText')}
          </Typography>
          <Button size="large" variant="soft" onClick={() => setAgain(true)}>
            {t('export.again')}
          </Button>
        </Card>
      )}
      {showForm &&
        (offers.length === 0 ? (
          <Typography>{t('export.noCars')}</Typography>
        ) : (
          <ExportForm cars={offers} minLength={cars.data.exportPassphraseMinLength} current={kept ?? file} onStarted={() => setAgain(false)} />
        ))}
    </Stack>
  )
}

/** One export as the tab says it: being built (with its progress and Cancel), ready (Download, Delete), or how it ended. */
function FileState({ file }: { file: ExportFileFieldsFragment }) {
  const { t } = useTranslation()
  const { bytes, dateTime } = useFormat()
  const keyText = useKeyText()
  const [cancel, cancelling] = useMutation(CancelExportDocument, { refetchQueries: ['MyExport'] })
  const [remove, removing] = useMutation(DeleteExportDocument, { refetchQueries: ['MyExport'], awaitRefetchQueries: true })
  const [error, setError] = useState<unknown>()

  switch (file.state) {
    case 'BUILDING': {
      const p = file.progress
      const status = [t('export.building'), p && p.cars > 0 ? t('export.buildingCar', { car: p.car, cars: p.cars }) : null, p && p.total > 0 ? t('export.buildingPhotos', { done: p.done, total: p.total }) : null]
        .filter(Boolean)
        .join(' · ')
      return (
        <Card sx={{ p: 1.5 }}>
          <Stack sx={{ gap: 1, alignItems: 'flex-start' }}>
            <Typography role="status">{status}</Typography>
            <LinearProgress aria-hidden variant={p && p.total > 0 ? 'determinate' : 'indeterminate'} value={p && p.total > 0 ? (100 * p.done) / p.total : undefined} sx={{ width: '100%' }} />
            <Typography variant="body2" sx={{ color: 'text.secondary' }}>
              {t('export.leave')}
            </Typography>
            {cancelling.error && <ErrorMessage error={cancelling.error} />}
            <ConfirmDialog
              trigger={
                <Button size="large" variant="soft" color="neutral" disabled={cancelling.loading}>
                  {t('export.cancel')}
                </Button>
              }
              title={t('export.cancelConfirm')}
              description={t('export.cancelText')}
              confirmLabel={t('export.cancel')}
              onConfirm={() => void cancel().catch(() => undefined)}
            />
          </Stack>
        </Card>
      )
    }
    case 'READY': {
      const line = [
        t('export.fileCars', { count: file.carNames.length }),
        bytes(file.bytes ?? 0),
        file.encrypted ? t('export.encrypted') : t('export.plain'),
        file.keptUntil ? t('export.keptUntil', { time: dateTime(file.keptUntil) }) : null,
      ]
        .filter(Boolean)
        .join(' · ')
      return (
        <Card sx={{ p: 1.5 }}>
          <Stack sx={{ gap: 1, alignItems: 'flex-start' }}>
            <SuccessMessage>{t('export.ready')}</SuccessMessage>
            <Typography sx={{ fontWeight: 700 }}>{file.carNames.join(', ')}</Typography>
            <Typography variant="body2">{line}</Typography>
            {file.largerThanImport && <WarningMessage>{t('export.tooLarge', { maxMb: file.importMaxMb })}</WarningMessage>}
            {error !== undefined && <ErrorMessage error={error} />}
            <Stack direction="row" sx={{ gap: 1.5, flexWrap: 'wrap' }}>
              <Button
                size="large"
                onClick={() => {
                  setError(undefined)
                  downloadExport(file.downloadUrl!).catch(setError)
                }}
              >
                <Download size={16} aria-hidden />
                {t('export.download')}
              </Button>
              <ConfirmDialog
                trigger={
                  <Button size="large" variant="soft" color="error" disabled={removing.loading}>
                    {t('export.delete')}
                  </Button>
                }
                title={t('export.deleteConfirm')}
                description={t('export.deleteText')}
                confirmLabel={t('export.delete')}
                onConfirm={() => void remove().catch(setError)}
              />
            </Stack>
          </Stack>
        </Card>
      )
    }
    case 'FAILED':
      return <ErrorMessage>{t('export.failed', { reason: keyText(file.failureKey ?? 'export.failed', {}) ?? '' })}</ErrorMessage>
    case 'CANCELLED':
      return <ErrorMessage>{t('export.cancelled')}</ErrorMessage>
    case 'INTERRUPTED':
      return <ErrorMessage>{t('export.interrupted')}</ErrorMessage>
    case 'EXPIRED':
      return <Typography>{t('export.expired', { time: file.deletedAt ? dateTime(file.deletedAt) : '' })}</Typography>
  }
}
