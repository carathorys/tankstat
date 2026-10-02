import { useQuery } from '@apollo/client/react'
import { Box, Button, Flex, Heading, IconButton, SegmentedControl, Separator, Text } from '@radix-ui/themes'
import { Check } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { NotificationsDocument } from '../gql/generated.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { ErrorMessage } from '../messages.tsx'
import { NotificationItem } from '../notifications/NotificationItem.tsx'
import { useNotificationActions } from '../notifications/useNotificationActions.ts'

const PAGE_SIZE = 20

/**
 * The whole inbox: everything or only what is unread, newest change first, a page at a time. Notifications are only marked read; the
 * system removes them a while after that.
 */
export function NotificationsPage() {
  const { t } = useTranslation()
  usePageTitle(t('notifications.title'))
  const [filter, setFilter] = useState<'all' | 'unread'>('all')
  const { data: current, previousData, error, fetchMore } = useQuery(NotificationsDocument, {
    variables: { unreadOnly: filter === 'unread', skip: 0, take: PAGE_SIZE },
    fetchPolicy: 'cache-and-network',
  })
  const data = current ?? previousData
  // Marking read changes the loaded rows in place; only the unread list itself changes shape, so it alone is asked again.
  const { markRead } = useNotificationActions(filter === 'unread' ? ['Notifications'] : [])
  const [actionError, setActionError] = useState<unknown>()
  const [loadingMore, setLoadingMore] = useState(false)

  const items = data?.notifications ?? []
  const total = data?.notificationCount ?? 0

  async function run(action: () => Promise<unknown>) {
    setActionError(undefined)
    try {
      await action()
    } catch (e) {
      setActionError(e)
    }
  }

  async function loadMore() {
    setLoadingMore(true)
    await run(() =>
      fetchMore({
        variables: { skip: items.length },
        updateQuery: (previous, { fetchMoreResult }) =>
          fetchMoreResult ? { ...fetchMoreResult, notifications: [...previous.notifications, ...fetchMoreResult.notifications] } : previous,
      }),
    )
    setLoadingMore(false)
  }

  return (
    <section aria-labelledby="page-title">
      <Heading id="page-title" mb="1">
        {t('notifications.title')}
      </Heading>
      <Text as="p" color="gray" size="2" mb="3">
        {t('notifications.retentionHint')}
      </Text>
      <Flex gap="3" wrap="wrap" align="center" mb="3">
        <SegmentedControl.Root size="3" value={filter} onValueChange={(v) => setFilter(v === 'unread' ? 'unread' : 'all')} aria-label={t('notifications.filter')}>
          <SegmentedControl.Item value="all">{t('notifications.all')}</SegmentedControl.Item>
          <SegmentedControl.Item value="unread">{t('notifications.unread')}</SegmentedControl.Item>
        </SegmentedControl.Root>
        <Box flexGrow="1" />
        <Button size="3" variant="soft" onClick={() => void run(() => markRead(null))}>
          {t('notifications.markAllRead')}
        </Button>
      </Flex>
      {(error ?? actionError) !== undefined && <ErrorMessage error={error ?? actionError} />}
      {!data && !error && (
        <Text as="p" role="status">
          {t('app.loading')}
        </Text>
      )}
      {data && items.length === 0 && (
        <Text as="p" color="gray">
          {filter === 'unread' ? t('notifications.emptyUnread') : t('notifications.empty')}
        </Text>
      )}
      {items.length > 0 && (
        <Box asChild m="0" p="0" className="glass" style={{ listStyle: 'none', borderRadius: 'var(--radius-4)' }}>
          <ul aria-label={t('notifications.title')}>
            {items.map((n, i) => (
              <Box asChild px="3" key={n.id}>
                <li>
                  {i > 0 && <Separator size="4" />}
                  <NotificationItem
                    notification={n}
                    actions={
                      !n.read && (
                        <IconButton size="3" variant="ghost" color="gray" aria-label={t('notifications.markRead')} onClick={() => void run(() => markRead([n.id]))}>
                          <Check size={18} aria-hidden />
                        </IconButton>
                      )
                    }
                  />
                </li>
              </Box>
            ))}
          </ul>
        </Box>
      )}
      {items.length < total && (
        <Flex justify="center" mt="3">
          <Button size="3" variant="soft" disabled={loadingMore} onClick={() => void loadMore()}>
            {t('notifications.showMore')}
          </Button>
        </Flex>
      )}
    </section>
  )
}
