import type { SystemSettingChangeInput, SystemSettingFieldsFragment } from '../gql/generated.ts'

type Setting = SystemSettingFieldsFragment

/** What a group's form holds: every value as typed (a secret blank until typed), and the secrets to remove. */
export interface GroupDraft {
  values: Readonly<Record<string, string>>
  cleared: ReadonlySet<string>
}

/**
 * The value a setting runs with as far as the page can tell: what the settings file and the sources below it give, else the built-in
 * default (which the server sends for a setting nothing sets), else nothing.
 */
export const effectiveOf = (s: Setting): string | null => s.value ?? s.defaultValue ?? null

/** What a group's form starts with: every value as it runs (a setting nobody set shows its built-in default), a secret blank (it is never shown back). */
export const draftOf = (settings: readonly Setting[]): GroupDraft => ({
  values: Object.fromEntries(settings.map((s) => [s.key, s.isSecret ? '' : (effectiveOf(s) ?? '')])),
  cleared: new Set(),
})

/** A switch's value as the server reads it: true in any case is on (.NET reads a JSON `true` written in the file as "True"). */
export const isOn = (value: string) => value.trim().toLowerCase() === 'true'

/** A value as it is sent: a decimal with a dot, which is what the server parses (invariantly), however it was typed ("0,5"). */
const sent = (s: Setting, value: string) => (s.kind === 'DECIMAL' ? value.trim().replace(',', '.') : value)

/** The week from Monday, as a days setting writes it (`Backups:Days`). */
export const WEEK = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'] as const
/** The value of a days setting that chooses no day (an empty one would read as unset: every day). */
export const NO_DAY = 'None'

/** The days a days setting holds, from Monday: the whole week when empty, none for `None`, names or their first three letters in any case. */
export function daysOf(value: string): string[] {
  const parts = value.split(/[,; ]+/).filter(Boolean).map((p) => p.toLowerCase())
  if (parts.length === 0) return [...WEEK]
  return WEEK.filter((day) => parts.includes(day.toLowerCase()) || parts.includes(day.slice(0, 3).toLowerCase()))
}

/** A days setting's value for the days chosen: `None` for none, the names from Monday otherwise. */
export const daysValue = (days: readonly string[]) => (days.length === 0 ? NO_DAY : WEEK.filter((day) => days.includes(day)).join(','))

/**
 * Whether a value says what the server has: a switch by on or off (not set is off), a choice in any case (as the server takes it), days in
 * any order, a decimal as sent. A setting nobody set is unchanged at its built-in default, and also when emptied: there is nothing to clear.
 */
function unchanged(s: Setting, value: string): boolean {
  if (s.value == null && value.trim() === '') return true
  const server = effectiveOf(s) ?? ''
  if (s.kind === 'BOOLEAN') return isOn(value) === isOn(server)
  if (s.kind === 'CHOICE') return value.toLowerCase() === server.toLowerCase()
  if (s.kind === 'DAYS') return daysValue(daysOf(value)) === daysValue(daysOf(server))
  return sent(s, value) === sent(s, server)
}

/**
 * What a group's form would save: only what changed, a secret only when typed or removed. A setting that is not used now is saved too when
 * it was changed: it is shown as it is, and saving keeps what was typed before a choice left it unused.
 */
export function changesOf(settings: readonly Setting[], draft: GroupDraft): SystemSettingChangeInput[] {
  return settings
    .filter((s) => s.editable)
    .flatMap((s) => {
      if (s.isSecret) {
        if (draft.cleared.has(s.key)) return [{ key: s.key, value: null, clear: true }]
        return draft.values[s.key] ? [{ key: s.key, value: draft.values[s.key], clear: false }] : []
      }
      const value = sent(s, draft.values[s.key] ?? '')
      if (unchanged(s, value)) return []
      return [{ key: s.key, value: value || null, clear: value === '' }]
    })
}
