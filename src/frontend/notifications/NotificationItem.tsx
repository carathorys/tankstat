import { Badge, Box, Flex, Link, Text } from '@radix-ui/themes'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link as RouterLink } from 'react-router'
import type { NotificationFieldsFragment } from '../gql/generated.ts'
import { useFormat } from '../i18n/format.ts'
import { useNotificationText } from './useNotificationText.ts'

/**
 * One notification: what happened (a link when there is something to look at), when, and whether it is new (as a word, not only bold).
 * `wrapLink` lets the bell's popover close itself when the link is followed.
 */
export function NotificationItem({
  notification,
  onOpen,
  wrapLink = (link) => link,
  actions,
}: {
  notification: NotificationFieldsFragment
  onOpen: () => void
  wrapLink?: (link: ReactNode) => ReactNode
  actions?: ReactNode
}) {
  const { t } = useTranslation()
  const { dateTime } = useFormat()
  const { text, href } = useNotificationText()(notification)

  return (
    <Flex gap="3" align="start" py="2">
      <Box flexGrow="1" minWidth="0">
        <Text as="p" size="2" weight={notification.read ? 'regular' : 'bold'}>
          {!notification.read && (
            <Badge color="blue" mr="2">
              {t('notifications.new')}
            </Badge>
          )}
          {href
            ? wrapLink(
                <Link asChild>
                  <RouterLink to={href} onClick={onOpen}>
                    {text}
                  </RouterLink>
                </Link>,
              )
            : text}
        </Text>
        <Text as="p" size="1" color="gray">
          <time dateTime={notification.updatedAt}>{dateTime(notification.updatedAt)}</time>
        </Text>
      </Box>
      {actions}
    </Flex>
  )
}
