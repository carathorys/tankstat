import Box from '@mui/material/Box'
import { placeholderGradient } from '../dashboard/chartFormat.ts'

const layer = { position: 'absolute', inset: 0 } as const

/**
 * The background of a banner or card: the vehicle's picture (a CSS background, so it is cropped to fill the box and never stretched; it is
 * decorative, the vehicle's name is next to it) or a gradient, under a dark scrim so white text stays readable. The parent must be
 * `position: relative` with `overflow: hidden`.
 */
export function CoverLayers({ pictureUrl, id }: { pictureUrl?: string | null; id: string }) {
  return (
    <>
      <Box className="cover" sx={layer} style={{ background: placeholderGradient(id) }} />
      {pictureUrl && (
        <Box
          className="cover cover-picture"
          sx={{ ...layer, backgroundSize: 'cover', backgroundPosition: 'center', backgroundRepeat: 'no-repeat' }}
          style={{ backgroundImage: `url("${pictureUrl.replace(/"/g, '%22')}")` }}
        />
      )}
      <Box className="scrim" sx={{ ...layer, background: 'linear-gradient(to top, rgba(0, 0, 0, 0.72), rgba(0, 0, 0, 0.18))' }} />
    </>
  )
}
