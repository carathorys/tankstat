import { CombinedGraphQLErrors } from '@apollo/client/errors'
import { useTranslation } from 'react-i18next'
import { UnreadableImageError } from '../pictures/resizeImage.ts'
import { ApiError } from '../pictures/ApiError.ts'

/**
 * Turns a failed request into text in the UI language. The API sends a stable `key` (e.g. "vehicle.nameRequired")
 * and `args` in the error extensions; the message itself is only an English fallback for unknown keys.
 */
export function useErrorText() {
  const { t, i18n } = useTranslation()

  const byKey = (key: unknown, args: unknown): string | undefined => {
    const path = `errors.${String(key)}`
    return typeof key === 'string' && i18n.exists(path) ? String(t(path as never, (args ?? {}) as never)) : undefined
  }

  return (error: unknown): string => {
    if (error instanceof ApiError) return byKey(error.key, error.args) ?? error.message
    if (error instanceof UnreadableImageError) return t('image.unreadable')
    if (CombinedGraphQLErrors.is(error)) {
      const first = error.errors[0]
      return byKey(first?.extensions?.key, first?.extensions?.args) ?? first?.message ?? t('errors.network')
    }
    return t('errors.network')
  }
}
