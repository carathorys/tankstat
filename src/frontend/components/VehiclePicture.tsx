import Box from '@mui/material/Box'
import { Car } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { usePictureSrc } from '../offline/keptPictures.ts'

/**
 * A vehicle's picture (the copy kept on the device when there is one, `keptPictures.ts`), or a neutral placeholder, also when the picture
 * cannot be loaded (offline, and not kept). Square thumbnails in grids, wide on the vehicle page.
 */
export function VehiclePicture({ url, name, width = 48, ratio = '1 / 1' }: { url?: string | null; name: string; width?: number | string; ratio?: string }) {
  const { t } = useTranslation()
  const { src, waiting } = usePictureSrc(url)
  const [failed, setFailed] = useState<string | null>(null)
  const frame = { width, aspectRatio: ratio, borderRadius: '9px', boxShadow: 'var(--tk-shadow-2)', overflow: 'hidden', flexShrink: 0 }
  if (waiting) return <Box sx={frame} /> // a moment: the device is asked whether it keeps the picture
  return src && failed !== src ? (
    <Box sx={frame}>
      <img
        src={src}
        alt={t('vehicles.pictureAlt', { name })}
        loading="lazy"
        onError={() => setFailed(src)}
        style={{ width: '100%', height: '100%', objectFit: 'cover', display: 'block' }}
      />
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
