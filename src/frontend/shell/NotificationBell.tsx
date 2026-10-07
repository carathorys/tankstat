import { useQuery } from '@apollo/client/react'
import Badge from '@mui/material/Badge'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Popover from '@mui/material/Popover'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { Bell } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link as RouterLink } from 'react-router'
import { IconAction } from '../components/IconAction.tsx'
import { visuallyHidden } from '../components/visuallyHidden.ts'
import { LatestNotificationsDocument, UnreadNotificationCountDocument } from '../gql/generated.ts'
import { ErrorMessage } from '../messages.tsx'
import { NotificationItem } from '../notifications/NotificationItem.tsx'
import { useNotificationActions } from '../notifications/useNotificationActions.ts'

const POLL_MS = 60_000
const LATEST = 5

/**
 * The bell in the top bar: how many notifications are unread (asked again every minute while the page is visible) and, when opened,
 * the latest few with a way to the whole inbox.
 */
export function NotificationBell() {
  const { t } = useTranslation()
  const { data } = useQuery(UnreadNotificationCountDocument, {
    pollInterval: POLL_MS,
    skipPollAttempt: () => document.hidden,
    fetchPolicy: 'cache-and-network',
  })
  const unread = data?.notificationCount ?? 0
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const close = () => setAnchor(null)

  return (
    <>
      <span role="status" style={visuallyHidden}>
        {unread > 0 ? t('notifications.unreadStatus', { count: unread }) : ''}
      </span>
      {/* The count is in the button's name; the badge only shows it. */}
      <Badge badgeContent={unread} max={99} color="error" slotProps={{ badge: { 'aria-hidden': true } }}>
        <IconAction
          label={unread > 0 ? t('notifications.bellUnread', { count: unread }) : t('notifications.bell')}
          size="large"
          aria-haspopup="dialog"
          aria-expanded={anchor !== null}
          onClick={(e) => setAnchor(e.currentTarget)}
        >
          <Bell size={18} aria-hidden />
        </IconAction>
      </Badge>
      <Popover
        open={anchor !== null}
        anchorEl={anchor}
        onClose={close}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
        slotProps={{ paper: { role: 'dialog', 'aria-label': t('notifications.title'), sx: { width: 380, maxWidth: 'calc(100vw - 32px)', p: 2, mt: 1 } } }}
      >
        <LatestNotifications onClose={close} />
      </Popover>
    </>
  )
}

/** Mounted only while the popover is open, so it always loads the current state. */
function LatestNotifications({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation()
  const { data, error } = useQuery(LatestNotificationsDocument, { variables: { take: LATEST }, fetchPolicy: 'network-only' })
  const { markRead } = useNotificationActions()
  const items = data?.notifications

  return (
    <Stack sx={{ gap: 1 }}>
      <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', gap: 1.5 }}>
        <Typography component="h2" variant="h6">
          {t('notifications.title')}
        </Typography>
        <Button variant="soft" size="large" disabled={!data || data.notificationCount === 0} onClick={() => void markRead(null)}>
          {t('notifications.markAllRead')}
        </Button>
      </Stack>
      {error && <ErrorMessage error={error} />}
      {!data && !error && (
        <Typography variant="body2" role="status">
          {t('app.loading')}
        </Typography>
      )}
      {items?.length === 0 && (
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {t('notifications.empty')}
        </Typography>
      )}
      {items && items.length > 0 && (
        <Box component="ul" aria-label={t('notifications.latest')} sx={{ listStyle: 'none', m: 0, p: 0 }}>
          {items.map((n) => (
            <li key={n.id}>
              <NotificationItem notification={n} onFollow={onClose} />
            </li>
          ))}
        </Box>
      )}
      <Button component={RouterLink} to="/notifications" variant="soft" size="large" onClick={onClose}>
        {t('notifications.showAll')}
      </Button>
    </Stack>
  )
}
