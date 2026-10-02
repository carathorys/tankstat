import { useMutation } from '@apollo/client/react'
import { MarkNotificationsReadDocument, type NotificationFieldsFragment } from '../gql/generated.ts'

/**
 * Marking notifications read (users cannot delete them; the system removes them a while after). The mutation returns what it changed, so
 * the loaded lists update in place (pages loaded with "Show more" stay); only the counts are asked again, plus `refetch` when a list
 * depends on the read state (e.g. the unread filter).
 */
export function useNotificationActions(refetch: string[] = []) {
  const [mutate] = useMutation(MarkNotificationsReadDocument, { refetchQueries: ['UnreadNotificationCount', 'LatestNotifications', ...refetch] })
  const markRead = (ids: string[] | null) => mutate({ variables: { ids } })
  return {
    markRead,
    /** Following a notification counts as reading it. */
    open: (n: NotificationFieldsFragment) => void (!n.read && markRead([n.id])),
  }
}
