import { useQuery } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Divider from '@mui/material/Divider'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { Check } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { NotificationsDocument } from '../gql/generated.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { ErrorMessage } from '../messages.tsx'
import { NotificationItem } from '../notifications/NotificationItem.tsx'
import { useNotificationActions } from '../notifications/useNotificationActions.ts'
import { IconAction } from '../components/IconAction.tsx'
import { Loading } from '../components/Loading.tsx'
import { itemMotion } from '../components/motion.ts'
import { Segmented } from '../components/Segmented.tsx'
import { glass } from '../theme/components.ts'

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
      <Typography id="page-title" variant="h3" component="h1" sx={{ mb: 0.5 }}>
        {t('notifications.title')}
      </Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary', mb: 1.5 }}>
        {t('notifications.retentionHint')}
      </Typography>
      <Stack direction="row" sx={{ gap: 1.5, flexWrap: 'wrap', alignItems: 'center', mb: 1.5 }}>
        <Segmented
          label={t('notifications.filter')}
          value={filter}
          options={[
            { value: 'all', label: t('notifications.all') },
            { value: 'unread', label: t('notifications.unread') },
          ]}
          onChange={setFilter}
        />
        <Box sx={{ flexGrow: 1 }} />
        <Button size="large" variant="soft" onClick={() => void run(() => markRead(null))}>
          {t('notifications.markAllRead')}
        </Button>
      </Stack>
      {(error ?? actionError) !== undefined && <ErrorMessage error={error ?? actionError} />}
      {!data && !error && <Loading />}
      {data && items.length === 0 && (
        <Typography sx={{ color: 'text.secondary' }}>{filter === 'unread' ? t('notifications.emptyUnread') : t('notifications.empty')}</Typography>
      )}
      {items.length > 0 && (
        <Box component="ul" aria-label={t('notifications.title')} sx={(theme) => ({ ...glass(theme), boxShadow: theme.shadows[3], listStyle: 'none', m: 0, p: 0, borderRadius: '12px' })}>
          <AnimatePresence initial={false}>
            {items.map((n, i) => (
              <motion.li key={n.id} {...itemMotion} style={{ paddingInline: 12 }}>
                {i > 0 && <Divider />}
                <NotificationItem
                  notification={n}
                  actions={
                    !n.read && (
                      <IconAction label={t('notifications.markRead')} variant="ghost" size="large" onClick={() => void run(() => markRead([n.id]))}>
                        <Check size={18} aria-hidden />
                      </IconAction>
                    )
                  }
                />
              </motion.li>
            ))}
          </AnimatePresence>
        </Box>
      )}
      {items.length < total && (
        <Stack direction="row" sx={{ justifyContent: 'center', mt: 1.5 }}>
          <Button size="large" variant="soft" loading={loadingMore} onClick={() => void loadMore()}>
            {t('notifications.showMore')}
          </Button>
        </Stack>
      )}
    </section>
  )
}
