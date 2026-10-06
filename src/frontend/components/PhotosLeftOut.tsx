import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import { AlertTriangle } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { DialogButtons } from '../dialogs/DialogFrame.tsx'
import { useDialogClose } from '../dialogs/dialogContext.ts'

/**
 * Shown in place of the form when a new log was saved but the server could not attach some of its photos. The log exists, so it must
 * not be saved again; the photos can be added once more by editing it.
 */
export function PhotosLeftOut({ count }: { count: number }) {
  const { t } = useTranslation()
  const close = useDialogClose()
  return (
    <Stack sx={{ gap: 1.5 }}>
      <Alert severity="warning" role="alert" icon={<AlertTriangle size={16} aria-hidden />}>
        {t('photos.leftOut', { count })}
      </Alert>
      <DialogButtons>
        <Button type="button" onClick={close}>
          {t('photos.done')}
        </Button>
      </DialogButtons>
    </Stack>
  )
}
