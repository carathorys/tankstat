import { useTranslation } from 'react-i18next'
import type { AccessLevel, NotificationFieldsFragment } from '../gql/generated.ts'

const LEVELS: AccessLevel[] = ['NONE', 'VIEW', 'EDIT', 'DELETE']
const asLevel = (value: string | undefined): AccessLevel => (LEVELS as string[]).includes(value ?? '') ? (value as AccessLevel) : 'NONE'

export interface NotificationText {
  /** The sentence the notification says. */
  text: string
  /** Where it leads, when there is something to look at (not after access was taken away). */
  href: string | null
}

/**
 * The text and link of a notification. The server sends only the kind and display-ready arguments (names as they were at the time), so
 * every language words it itself; access levels reuse the sharing and access texts.
 */
export function useNotificationText() {
  const { t } = useTranslation()

  return (n: NotificationFieldsFragment): NotificationText => {
    const args: Record<string, string | undefined> = Object.fromEntries(n.args.map((a) => [a.name, a.value]))
    const names = { actor: args.actorName ?? '', user: args.userName ?? '', vehicle: args.vehicleName ?? '', title: args.title ?? '' }
    const level = asLevel(args.level)
    const revoked = level === 'NONE'
    const vehicle = n.context?.type === 'VEHICLE' ? `/vehicles/${n.context.id}` : null
    const folded = (text: string) => (n.count > 1 ? `${text} ${t('notifications.folded', { count: n.count })}` : text)

    switch (n.kind) {
      case 'LOG_ACCESS_CHANGED':
        return revoked
          ? { text: folded(t('notifications.kinds.LOG_ACCESS_REVOKED', names)), href: null }
          : { text: folded(t('notifications.kinds.LOG_ACCESS_CHANGED', { ...names, level: t(`sharing.levels.${level === 'DELETE' ? 'DELETE' : 'EDIT'}`) })), href: vehicle }
      case 'VEHICLE_SHARED':
        return revoked
          ? { text: folded(t('notifications.kinds.VEHICLE_SHARE_REVOKED', names)), href: vehicle }
          : { text: folded(t('notifications.kinds.VEHICLE_SHARED', { ...names, level: t(`sharing.levels.${level === 'DELETE' ? 'DELETE' : 'EDIT'}`) })), href: vehicle }
      case 'DATA_ACCESS_CHANGED':
        return revoked
          ? { text: folded(t('notifications.kinds.DATA_ACCESS_REVOKED', names)), href: null }
          : { text: folded(t('notifications.kinds.DATA_ACCESS_CHANGED', { ...names, level: t(`level.${level}`) })), href: '/' }
      case 'DATA_SHARED':
        return revoked
          ? { text: folded(t('notifications.kinds.DATA_SHARE_REVOKED', names)), href: null }
          : { text: folded(t('notifications.kinds.DATA_SHARED', { ...names, level: t(`level.${level}`) })), href: null }
      case 'DEFAULT_ACCESS_CHANGED':
        return { text: folded(t('notifications.kinds.DEFAULT_ACCESS_CHANGED', { ...names, level: t(`level.${level}`) })), href: '/' }
      case 'RECURRING_DUE_SOON':
        return { text: t('notifications.kinds.RECURRING_DUE_SOON', names), href: vehicle && `${vehicle}?tab=recurring` }
      case 'RECURRING_OVERDUE':
        return { text: t('notifications.kinds.RECURRING_OVERDUE', names), href: vehicle && `${vehicle}?tab=recurring` }
      case 'MORE_ACTIVITY':
        return { text: t('notifications.kinds.MORE_ACTIVITY', { count: n.count }), href: null }
    }
  }
}
