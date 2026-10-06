import Box from '@mui/material/Box'
import { Car } from 'lucide-react'
import { useTranslation } from 'react-i18next'

/** A vehicle's picture, or a neutral placeholder. Square thumbnails in grids, wide on the vehicle page. */
export function VehiclePicture({ url, name, width = 48, ratio = '1 / 1' }: { url?: string | null; name: string; width?: number | string; ratio?: string }) {
  const { t } = useTranslation()
  const frame = { width, aspectRatio: ratio, borderRadius: '9px', boxShadow: 'var(--tk-shadow-2)', overflow: 'hidden', flexShrink: 0 }
  return url ? (
    <Box sx={frame}>
      <img src={url} alt={t('vehicles.pictureAlt', { name })} loading="lazy" style={{ width: '100%', height: '100%', objectFit: 'cover', display: 'block' }} />
    </Box>
  ) : (
    <Box
      role="img"
      aria-label={t('vehicles.noPicture')}
      sx={(theme) => ({ ...frame, display: 'flex', alignItems: 'center', justifyContent: 'center', backgroundColor: theme.vars.palette.neutral.soft, color: theme.vars.palette.text.secondary })}
    >
      <Car size={20} aria-hidden />
    </Box>
  )
}
