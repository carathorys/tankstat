import { CombinedGraphQLErrors } from '@apollo/client/errors'

/** The setting's name as an environment variable (`Auth:Mode` is `Auth__Mode`), which the docs use and an operator types. */
export const envName = (key: string) => key.replaceAll(':', '__')

/** The translation key of a setting's label: no colons (i18next's namespace separator) and no dots (its nesting separator). */
export const labelKey = (key: string) => `systemSettings.fields.${envName(key).replaceAll('.', '_')}`

/** The problems a refused save named (`settings.invalid` carries them as `problems`), to list them; empty for any other failure. */
export function problemsOf(error: unknown): string[] {
  if (!CombinedGraphQLErrors.is(error)) return []
  const first = error.errors[0]
  if (first?.extensions?.key !== 'settings.invalid') return []
  const problems = (first.extensions?.args as { problems?: unknown } | undefined)?.problems
  return Array.isArray(problems) ? problems.filter((p): p is string => typeof p === 'string') : []
}
