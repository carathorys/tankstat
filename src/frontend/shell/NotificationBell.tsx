import { useQuery } from '@apollo/client/react'
import { Badge, Box, Button, Flex, Heading, IconButton, Popover, Text, VisuallyHidden } from '@radix-ui/themes'
import { Bell } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link as RouterLink } from 'react-router'
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

  return (
    <>
      <VisuallyHidden role="status">{unread > 0 ? t('notifications.unreadStatus', { count: unread }) : ''}</VisuallyHidden>
      <Popover.Root>
        <Popover.Trigger>
          <IconButton
            size="3"
            variant="soft"
            color="gray"
            highContrast
            aria-label={unread > 0 ? t('notifications.bellUnread', { count: unread }) : t('notifications.bell')}
            style={{ position: 'relative' }}
          >
            <Bell size={18} aria-hidden />
            {unread > 0 && (
              <Badge size="1" variant="solid" color="red" radius="full" aria-hidden style={{ position: 'absolute', top: -6, right: -6 }}>
                {unread > 99 ? '99+' : unread}
              </Badge>
            )}
          </IconButton>
        </Popover.Trigger>
        <Popover.Content align="end" width="380px" maxWidth="calc(100vw - 32px)" aria-label={t('notifications.title')}>
          <LatestNotifications />
        </Popover.Content>
      </Popover.Root>
    </>
  )
}

/** Mounted only while the popover is open, so it always loads the current state. */
function LatestNotifications() {
  const { t } = useTranslation()
  const { data, error } = useQuery(LatestNotificationsDocument, { variables: { take: LATEST }, fetchPolicy: 'network-only' })
  const { markRead } = useNotificationActions()
  const items = data?.notifications

  return (
    <Flex direction="column" gap="2">
      <Flex justify="between" align="center" gap="3">
        <Heading as="h2" size="3">
          {t('notifications.title')}
        </Heading>
        <Button variant="soft" size="3" disabled={!data || data.notificationCount === 0} onClick={() => void markRead(null)}>
          {t('notifications.markAllRead')}
        </Button>
      </Flex>
      {error && <ErrorMessage error={error} />}
      {!data && !error && (
        <Text as="p" size="2" role="status">
          {t('app.loading')}
        </Text>
      )}
      {items?.length === 0 && (
        <Text as="p" size="2" color="gray">
          {t('notifications.empty')}
        </Text>
      )}
      {items && items.length > 0 && (
        <Box asChild m="0" p="0" style={{ listStyle: 'none' }}>
          <ul aria-label={t('notifications.latest')}>
            {items.map((n) => (
              <li key={n.id}>
                <NotificationItem notification={n} wrapLink={(link) => <Popover.Close>{link}</Popover.Close>} />
              </li>
            ))}
          </ul>
        </Box>
      )}
      <Popover.Close>
        <Button asChild variant="soft" size="3">
          <RouterLink to="/notifications">{t('notifications.showAll')}</RouterLink>
        </Button>
      </Popover.Close>
    </Flex>
  )
}
