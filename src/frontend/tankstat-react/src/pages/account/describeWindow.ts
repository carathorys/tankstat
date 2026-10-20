import type { TFunction } from 'i18next'
import { parseRule, type Span } from '../../offline/offlineWindow.ts'

const PARTS = ['years', 'months', 'days', 'hours', 'minutes', 'seconds'] as const satisfies readonly (keyof Span)[]

/** A window rule in words: "everything", "since 1 Mar 2026", "the last 2 years and 6 months". */
export function describeWindow(rule: string, t: TFunction, format: { date: (iso: string) => string; list: (items: string[]) => string }): string {
  const window = parseRule(rule)
  if (!window) return rule
  switch (window.kind) {
    case 'from':
      return t('account.offline.describe.from', { date: format.date(window.date) })
    case 'span':
      return t('account.offline.describe.span', {
        parts: format.list(PARTS.filter((part) => window[part] > 0).map((part) => t(`account.offline.units.${part}`, { count: window[part] }))),
      })
    default:
      return t(`account.offline.describe.${window.kind}`)
  }
}

export { PARTS as SPAN_PARTS }
