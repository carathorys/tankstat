import type { SettingsGroup } from '../gql/generated.ts'
import { keyIn } from './settingsRelevance.ts'

/**
 * How a group's settings are laid out: the ones that decide what the rest is used for (the sign-in mode, the photo provider) above
 * everything, then a section per kind, each of which can be folded away. A group with one kind of settings has no sections. A setting no
 * section names is shown with the deciding ones, so a setting added on the server is never hidden.
 */
export type SectionId = 'sessions' | 'standalone' | 'oidc' | 'proxy' | 'admins' | 'mailServer' | 'mailLogin' | 'modelServer' | 'reading'

/** A section's settings: keys, or every key below a prefix ending with a colon. */
const SECTIONS: Partial<Record<SettingsGroup, readonly { id: SectionId; keys: readonly string[] }[]>> = {
  AUTH: [
    { id: 'sessions', keys: ['Auth:AccessTokenMinutes', 'Auth:RefreshTokenDays', 'Auth:RefreshRotationGraceSeconds'] },
    { id: 'standalone', keys: ['Auth:PublicUrl', 'Auth:Standalone:'] },
    { id: 'oidc', keys: ['Auth:Oidc:'] },
    { id: 'proxy', keys: ['Auth:ProxyHeader:'] },
    { id: 'admins', keys: ['Auth:AdminEmails'] },
  ],
  SMTP: [
    { id: 'mailServer', keys: ['Smtp:Host', 'Smtp:From', 'Smtp:Port', 'Smtp:Security'] },
    { id: 'mailLogin', keys: ['Smtp:Username', 'Smtp:Password'] },
  ],
  PHOTO_READING: [
    { id: 'modelServer', keys: ['Recognition:OpenAiCompatible:'] },
    { id: 'reading', keys: ['Recognition:MinConfidence', 'Recognition:MaxConcurrent'] },
  ],
}

export interface Layout<T> {
  /** Shown above the sections, never folded: the deciding settings, and any no section names. */
  lead: T[]
  /** The sections with settings in them, in their order. */
  sections: { id: SectionId; settings: T[] }[]
}

/** A group's settings laid out in its sections, each in the order the server lists them. */
export function layoutOf<T extends { key: string }>(group: SettingsGroup, settings: readonly T[]): Layout<T> {
  const defined = SECTIONS[group] ?? []
  const sections = defined.map((s) => ({ id: s.id, settings: settings.filter((setting) => keyIn(s.keys, setting.key)) })).filter((s) => s.settings.length > 0)
  const placed = new Set(sections.flatMap((s) => s.settings.map((setting) => setting.key)))
  return { lead: settings.filter((s) => !placed.has(s.key)), sections }
}
