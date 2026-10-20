import { useState } from 'react'
import type { SettingsGroup, SystemSettingsQuery } from '../gql/generated.ts'
import { valueOfDraft } from './settingsRelevance.ts'
import { draftOf, effectiveOf, type GroupDraft } from './settingsDraft.ts'

export type SettingsGroupData = SystemSettingsQuery['systemSettings']['groups'][number]

/** A group's values as the form holds them, with the server's settings they started from: a new answer of the server starts them afresh. */
interface HeldDraft {
  basis: string
  draft: GroupDraft
}

/**
 * What is typed into each group of the settings, held above the forms (which only show one group at a time: a tab, a wizard's step), so it
 * outlasts a switch, and every group's held values together decide which settings of the others are used (`valueOf`: a sign-in mode chosen
 * in one group greys out the e-mail settings in another at once). `blank`: settings a form starts empty although the shipped settings or the
 * built-in default give them a value (a choice that must be made, never preselected).
 */
export function useGroupDrafts(groups: readonly SettingsGroupData[], blank: readonly string[] = []) {
  const [held, setHeld] = useState<Partial<Record<SettingsGroup, HeldDraft>>>({})

  // The draft of a group, or a fresh one when there is none or the server's answer changed since (after a save, or a change made elsewhere
  // seen on return): worked out as the page is drawn, so an answer never meets a draft of the settings before it.
  const basisOf = (group: SettingsGroupData) => JSON.stringify(group.settings)
  const draftFor = (group: SettingsGroupData): GroupDraft => {
    const kept = held[group.name]
    if (kept && kept.basis === basisOf(group)) return kept.draft
    const unset = group.settings.filter((s) => blank.includes(s.key) && (s.source === 'SHIPPED' || s.source === 'DEFAULT')).map((s) => s.key)
    const fresh = draftOf(group.settings)
    return unset.length === 0 ? fresh : { ...fresh, values: { ...fresh.values, ...Object.fromEntries(unset.map((key) => [key, ''])) } }
  }
  const server = Object.fromEntries(groups.flatMap((g) => g.settings.map((s) => [s.key, { value: effectiveOf(s), inFile: s.source === 'FILE' }] as const)))
  const valueOf = valueOfDraft(server, Object.assign({}, ...groups.map((g) => draftFor(g).values)) as Record<string, string>)
  const hold = (group: SettingsGroupData, draft: GroupDraft | undefined) =>
    setHeld((h) => {
      const next = { ...h }
      if (draft) next[group.name] = { basis: basisOf(group), draft }
      else delete next[group.name]
      return next
    })

  return { draftFor, hold, valueOf, basisOf }
}
