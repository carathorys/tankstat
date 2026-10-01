import { Badge, Box, Flex, Heading, Text } from '@radix-ui/themes'
import { UserChip } from './UserAvatar.tsx'
import { CoverLayers } from './CoverLayers.tsx'

/** The top of a vehicle page: the vehicle's picture as a banner (a gradient without one) with its name, plate and owner on top. */
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
  badges?: React.ReactNode
}) {
  return (
    <Box className="hero">
      <CoverLayers pictureUrl={pictureUrl} id={id} />
      <Flex direction="column" justify="end" gap="1" className="hero-content">
        <Heading id="page-title" size={{ initial: '6', md: '8' }} style={{ color: 'white', textShadow: '0 1px 6px rgba(0,0,0,0.6)' }}>
          {name}
        </Heading>
        <Flex gap="3" align="center" wrap="wrap">
          {plate && (
            <Badge size="2" color="gray" variant="solid" highContrast>
              {plate}
            </Badge>
          )}
          {owner && (
            <Text size="2" style={{ color: 'white' }}>
              <UserChip user={owner} />
            </Text>
          )}
          {badges}
        </Flex>
      </Flex>
    </Box>
  )
}
