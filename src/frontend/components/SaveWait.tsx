import Typography from '@mui/material/Typography'
import { useTranslation } from 'react-i18next'

/** Next to a Save button that waits for photos to finish uploading: says why it waits. */
export function SaveWait({ waiting }: { waiting: boolean }) {
  const { t } = useTranslation()
  if (!waiting) return null
  return (
    <Typography variant="caption" className="tk-appear" sx={{ color: 'text.secondary', mr: 'auto', alignSelf: 'center' }}>
      {t('photos.waitUpload')}
    </Typography>
  )
}
