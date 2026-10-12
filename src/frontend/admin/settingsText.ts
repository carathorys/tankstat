import { CombinedGraphQLErrors } from '@apollo/client/errors'

/** The setting's name as an environment variable (`Auth:Mode` is `Auth__Mode`), which the docs use and an operator types. */
export const envName = (key: string) => key.replaceAll(':', '__')

/** The translation key of a setting's label: no colons (i18next's namespace separator) and no dots (its nesting separator). */
export const labelKey = (key: string) => `systemSettings.fields.${envName(key).replaceAll('.', '_')}`

/** The translation key of what a setting does, shown behind its question mark; named like its label's. */
export const helpKey = (key: string) => `systemSettings.help.${envName(key).replaceAll('.', '_')}`

/** The refusals that name the settings at fault as `problems`: a save the server could not take, a restart the next start would refuse. */
const NAMES_PROBLEMS = new Set(['settings.invalid', 'settings.restartWouldFail'])

/** The problems a refused save or restart named, to list them; empty for any other failure. */
export function problemsOf(error: unknown): string[] {
  if (!CombinedGraphQLErrors.is(error)) return []
  const first = error.errors[0]
  if (typeof first?.extensions?.key !== 'string' || !NAMES_PROBLEMS.has(first.extensions.key)) return []
  const problems = (first.extensions?.args as { problems?: unknown } | undefined)?.problems
  return Array.isArray(problems) ? problems.filter((p): p is string => typeof p === 'string') : []
}
