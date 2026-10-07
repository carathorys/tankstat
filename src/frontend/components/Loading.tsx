import CircularProgress from '@mui/material/CircularProgress'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useTranslation } from 'react-i18next'

/** "Loading…" with a spinner that shows only once the wait is noticeable (no flash on quick loads); a status for screen readers. */
export function Loading({ label }: { label?: string }) {
  const { t } = useTranslation()
  return (
    <Stack direction="row" role="status" sx={{ alignItems: 'center', gap: 1, my: 1 }}>
      <CircularProgress size={16} color="inherit" className="tk-delayed" aria-hidden />
      <Typography variant="body2" sx={{ color: 'text.secondary' }}>
        {label ?? t('app.loading')}
      </Typography>
    </Stack>
  )
}
