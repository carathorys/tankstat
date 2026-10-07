import Avatar from '@mui/material/Avatar'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'

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

/** The old Radix avatar sizes 1-6: the side and the initials' size, in pixels. */
const SIZES = { '1': [24, 12], '2': [32, 14], '3': [40, 16], '4': [48, 18], '5': [64, 24], '6': [80, 28] } as const

/** A person's picture, or their initials when they have none. Decorative when the name is shown beside it. */
export function UserAvatar({ user, size = '2' }: { user: UserLike; size?: keyof typeof SIZES }) {
  const [side, text] = SIZES[size]
  return (
    <Avatar component="span" src={user.avatarUrl ?? undefined} alt="" sx={{ width: side, height: side, fontSize: text }}>
      {initials(user.displayName)}
    </Avatar>
  )
}

/** Avatar and name, e.g. in a grid cell; inline, so it fits in a line of text too. */
export function UserChip({ user }: { user: UserLike }) {
  return (
    <Stack component="span" direction="row" sx={{ display: 'inline-flex', alignItems: 'center', gap: 1, verticalAlign: 'middle' }}>
      <UserAvatar user={user} />
      <Typography component="span" variant="body2">
        {user.displayName}
      </Typography>
    </Stack>
  )
}
