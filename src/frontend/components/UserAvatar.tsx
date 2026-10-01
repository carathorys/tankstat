import { Avatar, Flex, Text } from '@radix-ui/themes'

export interface UserLike {
  displayName: string
  avatarUrl?: string | null
}

const initials = (name: string) =>
  name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]!.toUpperCase())
    .join('') || '?'

/** A person's picture, or their initials when they have none. Decorative when the name is shown beside it. */
export function UserAvatar({ user, size = '2' }: { user: UserLike; size?: '1' | '2' | '3' | '4' | '5' | '6' }) {
  return <Avatar size={size} radius="full" src={user.avatarUrl ?? undefined} fallback={initials(user.displayName)} alt="" />
}

/** Avatar and name, e.g. in a grid cell. */
export function UserChip({ user }: { user: UserLike }) {
  return (
    <Flex align="center" gap="2">
      <UserAvatar user={user} />
      <Text size="2">{user.displayName}</Text>
    </Flex>
  )
}
