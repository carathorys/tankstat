import Box from '@mui/material/Box'
import Chip from '@mui/material/Chip'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import type { ReactNode } from 'react'
import { CoverLayers } from './CoverLayers.tsx'
import { UserChip } from './UserAvatar.tsx'
import { PendingBadge } from './PendingBadge.tsx'

/**
 * The top of a vehicle page: the vehicle's picture as a banner (a gradient without one) with its name, plate and owner on top. White text
 * on a dark scrim in either colour scheme, so it is drawn in the dark one (the `dark` class).
 */
export function VehicleHero({
  id,
  name,
  plate,
  pictureUrl,
  owner,
  badges,
}: {
  id: string
  name: string
  plate?: string | null
  pictureUrl?: string | null
  owner?: { displayName: string; avatarUrl?: string | null } | null
  badges?: ReactNode
}) {
  return (
    <Box
      className="hero dark"
      sx={{ position: 'relative', minHeight: { xs: '9rem', md: '13rem' }, borderRadius: '12px', boxShadow: 'var(--tk-shadow-4)', overflow: 'hidden', color: 'white' }}
    >
      <CoverLayers pictureUrl={pictureUrl} id={id} />
      <Stack className="hero-content" sx={{ position: 'relative', height: '100%', minHeight: 'inherit', p: 2, justifyContent: 'flex-end', gap: 0.5 }}>
        <Typography
          id="page-title"
          component="h1"
          variant="h3"
          sx={(theme) => ({ color: 'white', textShadow: '0 1px 6px rgba(0,0,0,0.6)', [theme.breakpoints.up('lg')]: theme.typography.h1 })}
        >
          {name}
        </Typography>
        <Stack direction="row" sx={{ gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
          {plate && <Chip size="medium" variant="solid" color="neutral" label={plate} />}
          <PendingBadge entity="vehicles" id={id} />
          {owner && <UserChip user={owner} />}
          {badges}
        </Stack>
      </Stack>
    </Box>
  )
}
