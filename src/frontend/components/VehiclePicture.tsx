import { Box, Flex, Text } from '@radix-ui/themes'
import { Car } from 'lucide-react'
import { useTranslation } from 'react-i18next'

/** A vehicle's picture, or a neutral placeholder. Square thumbnails in grids, wide on the vehicle page. */
export function VehiclePicture({ url, name, width = 48, ratio = '1 / 1' }: { url?: string | null; name: string; width?: number | string; ratio?: string }) {
  const { t } = useTranslation()
  const frame = { width, aspectRatio: ratio, borderRadius: 'var(--radius-3)', boxShadow: 'var(--shadow-2)', overflow: 'hidden', flexShrink: 0 }
  return url ? (
    <Box style={frame}>
      <img src={url} alt={t('vehicles.pictureAlt', { name })} loading="lazy" style={{ width: '100%', height: '100%', objectFit: 'cover', display: 'block' }} />
    </Box>
  ) : (
    <Flex align="center" justify="center" style={{ ...frame, background: 'var(--gray-a3)' }} role="img" aria-label={t('vehicles.noPicture')}>
      <Text color="gray">
        <Car size={20} aria-hidden />
      </Text>
    </Flex>
  )
}
