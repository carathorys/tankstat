import type { RestoreTargetFieldsFragment } from '../gql/generated.ts'

/** What a restore of a backup can do to this server, in two tiers: a blocker refuses it (no acknowledgement helps), a risk is accepted knowingly. */
export type RestoreRisk = {
  tier: 'blocker' | 'risk'
  /** The words of it: `admin.backups.risks.<key>.short` (in the backup's row) and `.text` (in its restore dialog). */
  key: 'missing' | 'mayBeMissing' | 'noSettingsOlder' | 'noSettingsNone' | 'noSettingsIgnored' | 'secretsLeftOut' | 'signInOverridden' | 'overridden' | 'environmentUnknown'
  /** The settings it is about, by variable name, joined. */
  names?: string
}

const SIGN_IN = 'Auth__Mode'

/**
 * What a restore of `point` would leave missing or changed here (#140), from what the server worked out of the backup and of this server:
 * the settings its server set in the environment that this server lacks (a blocker: the server refuses the restore; only a risk where an
 * encrypted backup's file, read only with its passphrase, may hold them), no settings file and why, its secrets left out, settings this
 * server's environment overrides (the sign-in mode apart: the restored users may have no way to sign in), and an environment it says nothing of.
 */
export function restoreRisks(point: Pick<RestoreTargetFieldsFragment, 'settingsFile' | 'environment'>): RestoreRisk[] {
  const risks: RestoreRisk[] = []
  const { settingsFile: file, environment } = point
  if (environment && environment.missing.length > 0)
    risks.push({ tier: environment.missingCertain ? 'blocker' : 'risk', key: environment.missingCertain ? 'missing' : 'mayBeMissing', names: environment.missing.join(', ') })
  if (!file) risks.push({ tier: 'risk', key: 'noSettingsOlder' })
  else if (file.state === 'NONE') risks.push({ tier: 'risk', key: 'noSettingsNone' })
  else if (file.state === 'IGNORED') risks.push({ tier: 'risk', key: 'noSettingsIgnored' })
  else if (!file.secrets && file.secretsLeftOut.length > 0) risks.push({ tier: 'risk', key: 'secretsLeftOut', names: file.secretsLeftOut.join(', ') })
  if (environment?.overridden.includes(SIGN_IN)) risks.push({ tier: 'risk', key: 'signInOverridden' })
  const overridden = environment?.overridden.filter((n) => n !== SIGN_IN) ?? []
  if (overridden.length > 0) risks.push({ tier: 'risk', key: 'overridden', names: overridden.join(', ') })
  // A backup with a settings file but no word of its environment (a release between the two); an older one's note says it already.
  if (file && !environment) risks.push({ tier: 'risk', key: 'environmentUnknown' })
  return risks
}
