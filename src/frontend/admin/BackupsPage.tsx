import Typography from '@mui/material/Typography'
import { useTranslation } from 'react-i18next'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { BackupsPanel } from './BackupsPanel.tsx'

/** The administrators' Backups page: this server, Back up now, Upload, the backups in the folder and the key set (all in `BackupsPanel`). */
export function BackupsPage() {
  const { t } = useTranslation()
  usePageTitle(t('admin.backups.title'))
  return (
    <section aria-labelledby="page-title">
      <Typography id="page-title" component="h1" variant="h3" sx={{ mb: 1.5 }}>
        {t('admin.backups.title')}
      </Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary', mb: 2 }}>
        {t('admin.backups.description')}
      </Typography>
      <BackupsPanel />
    </section>
  )
}
