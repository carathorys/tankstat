import { useTranslation } from 'react-i18next'
import type { AccessLevel, NotificationFieldsFragment } from '../gql/generated.ts'
import type en from '../i18n/locales/en.json'

type KindText = Exclude<keyof (typeof en)['notifications']['kinds'], `${string}_one` | `${string}_other`>

const LEVELS: AccessLevel[] = ['NONE', 'VIEW', 'EDIT', 'DELETE']
const asLevel = (value: string | undefined): AccessLevel => ((LEVELS as string[]).includes(value ?? '') ? (value as AccessLevel) : 'NONE')

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
    // An access change: one text for a level that was given, another (if any) for access taken away; folded changes say how many.
    const access = (granted: KindText, taken: KindText | null, levelText: string, href: string | null): NotificationText => {
      const text = revoked && taken ? t(`notifications.kinds.${taken}`, names) : t(`notifications.kinds.${granted}`, { ...names, level: levelText })
      return { text: n.count > 1 ? `${text} ${t('notifications.folded', { count: n.count })}` : text, href }
    }
    const logLevel = t(`sharing.levels.${level === 'DELETE' ? 'DELETE' : 'EDIT'}`)
    const dataLevel = t(`level.${level}`)

    switch (n.kind) {
      case 'LOG_ACCESS_CHANGED':
        return access('LOG_ACCESS_CHANGED', 'LOG_ACCESS_REVOKED', logLevel, revoked ? null : vehicle)
      case 'VEHICLE_SHARED':
        return access('VEHICLE_SHARED', 'VEHICLE_SHARE_REVOKED', logLevel, vehicle)
      case 'DATA_ACCESS_CHANGED':
        return access('DATA_ACCESS_CHANGED', 'DATA_ACCESS_REVOKED', dataLevel, revoked ? null : '/')
      case 'DATA_SHARED':
        return access('DATA_SHARED', 'DATA_SHARE_REVOKED', dataLevel, null)
      case 'DEFAULT_ACCESS_CHANGED':
        return access('DEFAULT_ACCESS_CHANGED', null, dataLevel, '/')
      case 'RECURRING_DUE_SOON':
      case 'RECURRING_OVERDUE':
        return { text: t(`notifications.kinds.${n.kind}`, names), href: vehicle && `${vehicle}?tab=recurring` }
      case 'MORE_ACTIVITY':
        return { text: t('notifications.kinds.MORE_ACTIVITY', { count: n.count }), href: null }
    }
  }
}
