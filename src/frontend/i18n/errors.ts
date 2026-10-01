import { CombinedGraphQLErrors } from '@apollo/client/errors'
import { useTranslation } from 'react-i18next'

/**
 * Turns a failed request into text in the UI language. The API sends a stable `key` (e.g. "vehicle.nameRequired")
 * and `args` in the error extensions; the message itself is only an English fallback for unknown keys.
 */
export function useErrorText() {
  const { t, i18n } = useTranslation()

  return (error: unknown): string => {
    if (CombinedGraphQLErrors.is(error)) {
      const first = error.errors[0]
      const key = first?.extensions?.key
      const path = `errors.${String(key)}`
      if (typeof key === 'string' && i18n.exists(path)) {
        return String(t(path as never, (first.extensions?.args ?? {}) as never))
      }
      return first?.message ?? t('errors.network')
    }
    return t('errors.network')
  }
}
